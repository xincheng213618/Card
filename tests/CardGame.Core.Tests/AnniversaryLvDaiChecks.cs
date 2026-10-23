using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class AnniversaryLvDaiChecks
{
    private const string Hero = "anniversary:lv-dai", Qinguo = "anniversary:qinguo", Peer = "fixture:lv-dai-peer",
        Driver = "fixture:lv-dai-swap", NoDraw = "fixture:lv-dai-no-draw", Hold = "fixture:lv-dai-recovery-child", Deck = "fixture:lv-dai-deck";

    public static void AtomicEquipmentExitAndEntryRecoverOnceCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(79, 1, "lv-dai-atomic-equipment", Hero, 2, 0, 0, 0, 0, [NoDraw, Driver, Hold], [],
            [new(CardKind.BaguaFormation, Suit.Spade, 2)],
            [new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] }],
            [new(Peer) { StartingHand = [] }], new string('a', 64));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(new StartGameCommand()); Play(); Use("prepare", [1]); Play();
        var own = V(0).Equipment.Select(c => c.Id).ToArray(); var other = V(1).Equipment.Select(c => c.Id).ToArray();
        Need(own.Length == 1 && other.Length == 2 && V(0).Hp == 2, "The real setup retains one equipment versus two and two HP before the native atomic exchange.");
        Use("exchange"); Reach(() => P()?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-targets") == true);
        Answer(c => c.Targets.SequenceEqual([0, 1]));
        Reach(() => P()?.SkillPrompt?.SkillId == Hold);
        var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Batch.Movements.Any(m => m.Reason.Value.EndsWith("equipment-exchange", StringComparison.Ordinal)));
        var source = movement.Batch.SourceCounts.Single(c => c.Location == CardLocation.Equipment(0));
        var destination = movement.Batch.DestinationCounts!.Single(c => c.Location == CardLocation.Equipment(0));
        var bindings = movement.Candidates.Where(c => c.OwnerSeat == 0 && c.SkillId == Qinguo).Select(c => c.BindingId).Order().ToArray();
        var actual = E<ProgramBindingStartedEvent>().Where(e => e.OwnerSeat == 0 && e.SkillId == Qinguo).ToArray();
        Need(source is { CountBefore: 1, CountAfter: 2 } && destination is { CountBefore: 1, CountAfter: 2 } &&
            bindings.SequenceEqual(new[] { "equipment-entered", "equipment-left" }.Order()) &&
            movement.Contexts!.Where((_, i) => movement.Candidates[i].OwnerSeat == 0 && movement.Candidates[i].SkillId == Qinguo).All(c => c.Facts!.CurrentHp == 2) &&
            actual is [{ BindingId: "equipment-left" }] && V(0).Hp == 3,
            "Both prequalified paths retain frozen HP2; the local gate rejects only the duplicate entering path, and the real source path recovers exactly one.");
        var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(game.CreateSnapshot(i))).ToArray();
        var frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
        game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(game.CreateSnapshot(i)))) &&
            frames.SequenceEqual(game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))),
            "Cold preserves the frozen movement, actual recovery child and original candidate cursor.");
        Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Play();
        var moved = game.CardMovements.Where(m => m.Reason.Value.EndsWith("equipment-exchange", StringComparison.Ordinal)).ToArray();
        Need(V(0).Hp == 3 && V(0).Equipment.Select(c => c.Id).Order().SequenceEqual(other.Order()) && V(1).Equipment.Select(c => c.Id).SequenceEqual(own) &&
            moved.Length == 3 && moved.Select(m => m.CardId).Distinct().Count() == 3 && moved.All(m => m.From.Zone == CardZoneKind.Equipment && m.To.Zone == CardZoneKind.Equipment) &&
            E<RecoveryAppliedEvent>().Count(e => e.TargetSeat == 0 && e.Amount == 1) == 1 &&
            E<ProgramBindingStartedEvent>().Count(e => e.SkillId == Qinguo && e.OwnerSeat == 0) == 1 &&
            E<ProgramRecoveryAppliedEvent>().Count(e => e.ProgramFrameId == actual[0].FrameId && e.Receipt.HpBefore == 2 && e.Receipt.HpAfter == 3) == 1,
            "The one atomic three-entity swap completes after the cold child with one recovery and one actual Qinguo invocation; it cannot replay the separately frozen entering candidate.");

        PendingDecision? P() => Enumerable.Range(0, 4).Select(i => game.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        PlayerSnapshot V(int seat) => game.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        T[] E<T>() => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
        void Accept(GameCommand command) { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Lv Dai command."); }
        void Use(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], game.Revision, P()!.PromptId));
        void Answer(Func<PromptChoice, bool> select) { var p = P()!; var choice = p.Choices.FirstOrDefault(select); Need(p.PlayerSeat == 0 && choice is not null, "The original human's exact current choice is required."); Accept(new AnswerPromptCommand(0, p.PromptId, choice!.Id, game.Revision)); }
        void Play() => Reach(() => P() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        void Reach(Func<bool> ready) { for (var step = 0; step < 90; step++) { if (ready()) return; if (P() is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Lv Dai boundary: " + Dump()); Accept(new AdvanceOneStepCommand(game.Revision)); } Need(false, "The bounded atomic-equipment continuation was not reached."); }
        string Dump() => JsonSerializer.Serialize(new { P = P(), Hp = V(0).Hp, Own = V(0).Equipment, Other = V(1).Equipment, Started = E<ProgramBindingStartedEvent>().Where(e => e.SkillId == Qinguo), Recovery = E<RecoveryAppliedEvent>() });
        void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + " " + Dump()); }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-lv-dai", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero); var c = SkillProgramCatalog.Load(Rules, Presentation);
            foreach (var (id, program) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = program, ProgramPresentation = c.Presentations[id] });
            b.AddGeneral(new(Peer, "固定角色", "", NoDraw, "wei", 4));
            b.AddDeck(new(Deck, "固定牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 30).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "勤国原子批次去重", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
        public static string Presentation => JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
        { [NoDraw] = new { name = "不摸牌", description = "固定短局" }, [Driver] = new { name = "原生交换", description = "真实装备交换" },
          [Hold] = new { name = "回复子窗口", description = "冻结原移动批次与候选游标", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } });
        public static string Rules => $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
          {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
          {"id":"{{Driver}}","revision":1,"activations":[
            {"id":"prepare","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[
              {"op":"alterEquipmentSlots","target":"owner","equipmentSlots":["weapon"],"amount":2},
              {"op":"alterEquipmentSlots","target":"selectedTarget","equipmentSlots":["weapon"],"amount":2},
              {"op":"placeNamedWeapon","target":"selectedTarget","outputKind":"crossbow"},
              {"op":"placeNamedWeapon","target":"selectedTarget","outputKind":"qinggangSword"}]},
            {"id":"exchange","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[
              {"op":"selectTargets","target":"owner","targetKind":"equipmentExchangePair","minimumTargets":2,"maximumTargets":2,"targetAiOrder":"stable"},
              {"op":"exchangeSelectedTargetEquipment","target":"owner"}]}]},
          {"id":"{{Hold}}","revision":1,"triggers":[{"id":"native-health","window":"afterHpRecovered","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"native-health","options":[{"id":"continue"}]}]}]}
        ]}
        """;
    }
}
