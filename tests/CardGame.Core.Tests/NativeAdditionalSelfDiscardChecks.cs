using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class NativeAdditionalSelfDiscardChecks
{
    private static readonly Lazy<ContentRegistry> Classic = new(() => StandardContentRegistry.CreateWithClassicGenerals());
    private const string Driver = "fixture:additional-discard-driver", Watch = "fixture:additional-discard-watch",
        Quiet = "fixture:additional-discard-quiet", Peer = "fixture:additional-discard-peer", Deck = "fixture:additional-discard-deck";

    public static void LiYiTwoCardGroupSelectionAndPaidChildCold()
    {
        var s = Scene.Start("ol:li-yi"); s.Play();
        s.Use("ol:chanshuang", "simultaneous-options", [1]);
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "discard") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "discard");
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "discard");
        s.Reach(() => s.P is { PlayerSeat: 0 } && s.G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.Chanshuang?.Stage == ChanshuangStage.DiscardCards);
        s.Need(s.P!.RequiredCardCount == 2 && s.P.MinimumCardCount == 2, "Chanshuang publishes the human executor's complete two-card unpaid group.");
        var frame = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Last().Id;
        var count = s.G.CardMovements.Count; var first = s.P.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        s.Answer(c => c.Cards.SequenceEqual([first]));
        s.Need(s.G.CardMovements.Count == count && s.P!.RequiredCardCount == 1 &&
            s.P.Choices.All(c => c.Parameters.GetValueOrDefault("owned-selection-selected") == first.ToString()),
            "Choosing the first Chanshuang material retains the same unpaid group, exposes its remaining count and keeps the native selected entity highlighted.");
        s.Cold(); s.Answer(c => c.Cards.Count == 1); s.Pause();
        var paid = s.G.CardMovements.Where(m => m.Reason.Value == "skill-program.li-yi.chanshuang.discard" && m.From.OwnerSeat == 0).ToArray();
        s.Need(paid.Length == 2 && s.E<ChanshuangCostIssuedEvent>().Single(e => e.FrameId == frame && e.ActorSeat == 0).Count == 2,
            "The last native pick pays exactly one real two-card discard before its observer child.");
        s.Cold(); s.Continue(); s.Play();
        s.Need(s.G.CardMovements.Count(m => m.Reason.Value == "skill-program.li-yi.chanshuang.discard" && m.From.OwnerSeat == 0) == 2,
            "Cold recovery of the paid Chanshuang child does not pay the human two-card cost again.");
        s.Replay();
    }

    public static void YangFengSelfGroupKeepsForeignOpaqueAndColdPayment()
    {
        var s = Scene.Start("ol:yang-feng"); s.Play(); s.End(); s.Play(); s.End(); s.Play();
        s.Use("ol:jiawei", "discard-reveal-partition", [0]);
        s.Reach(() => s.G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.YangFeng is { Stage: YangFengStage.DiscardChoice });
        s.Need(s.P is { PlayerSeat: 0, RequiredCardCount: 2, MinimumCardCount: 2 }, "After two genuine completed turns, self-target Jiawei exposes its whole two-card discard.");
        var before = s.G.CardMovements.Count; var id = s.P!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        s.Answer(c => c.Cards.SequenceEqual([id]));
        s.Need(s.G.CardMovements.Count == before && s.P!.RequiredCardCount == 1 && s.P.Choices.All(c => c.Parameters.GetValueOrDefault("owned-selection-selected") == id.ToString()),
            "The first Jiawei self material is buffered until the entire originally required group is selected.");
        s.Cold(); s.Answer(c => c.Cards.Count == 1); s.Pause();
        s.Need(s.G.CardMovements.Count(m => m.Reason.Value == "skill-program.yang-feng.jiawei.discard") == 2,
            "Jiawei pays its actual complete self group before the paused discard child.");
        s.Cold(); s.Continue(); s.Play();
        s.Need(s.G.CardMovements.Count(m => m.Reason.Value == "skill-program.yang-feng.jiawei.discard") == 2,
            "The original Jiawei paid child returns exactly once after cold recovery.");
        s.End(); s.Play(); s.Use("ol:jiawei", "discard-reveal-partition", [1]);
        s.Reach(() => s.G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.YangFeng is { Stage: YangFengStage.DiscardChoice });
        s.Need(s.P!.RequiredCardCount == 0 && s.P.Choices.All(c => c.Cards.Count == 0 && !c.Parameters.ContainsKey("owned-selection-role")),
            "The same Jiawei skill preserves the other player's opaque hand slots instead of publishing them as the human's own multi-card material set.");
        s.Replay();
    }

    public static void BijingBuffersWholeDiscardAndResumesPaidBatchCold()
    {
        var s = Scene.Start("ol:lv-kai"); s.Play(); s.End();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "ol:bijing" && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        for (var i = 0; i < 2; i++) s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards" && c.Cards.Count == 1);
        if (s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards") == true)
            s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
        var marked = s.E<ProgramBijingMarkedEvent>().Single().CardIds.ToArray();
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Answer(c => c.Cards.Count == 1 && marked.Contains(c.Cards.Single()));
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "bijing-punish-discard") == true);
        s.Need(s.P is { PlayerSeat: 1, RequiredCardCount: 2, MinimumCardCount: 2 }, "The genuine current attacker receives one complete two-card Bijing discard group.");
        var lion = s.G.CreateSnapshot(1).Players.Single(p => p.Seat == 1).Equipment.Single(card => card.Kind == CardKind.SilverLion).Id;
        var before = s.G.CardMovements.Count; var first = lion;
        s.Answer(c => c.Cards.SequenceEqual([first]));
        s.Need(s.G.CardMovements.Count == before && s.P!.RequiredCardCount == 1 &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Last().BijingPunish is { Remaining: 2, SelectedCardIds.Count: 1, DiscardedCardIds.Count: 0 },
            "Bijing buffers the first equipment material on its original frame without moving, paying, recovering or treating it as already discarded.");
        s.Cold(); s.Answer(c => c.Cards.Count == 1); s.Pause();
        var fact = s.E<ProgramBijingPunishEvent>().Single();
        var moves = s.G.CardMovements.Where(m => m.Reason.Value == "skill-program.ol:bijing.bijing-punish").ToArray();
        s.Need(fact.DiscardedCardIds.Count == 2 && moves.Length == 2 && moves.All(m => m.From.OwnerSeat == 1 && m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment && m.To == CardLocation.DiscardPile) &&
            moves.Count(m => m.CardId == lion && m.From == CardLocation.Equipment(1)) == 1 &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Any(e => e.ParentFrameId == fact.FrameId && e.ExecutorSeat == 1 && e.ActualCount == 2 && e.HandCount == 1) &&
            s.E<SilverLionRemovedRecoveryEvent>() is [{ RecoveredAmount: 1, PlayerSeat: 1 }],
            "The second selection pays one genuine mixed hand/equipment two-card discard and reaches the real SilverLion recovery child after both materials moved.");
        s.Cold(); s.Continue();
        s.Reach(() => s.G.ResolutionStack.OfType<ProgramSkillFrame>().All(f => f.Id != fact.FrameId));
        s.Need(s.G.CardMovements.Count(m => m.Reason.Value == "skill-program.ol:bijing.bijing-punish") == 2,
            "Cold recovery of Bijing's already-paid SilverLion child returns to its original discard phase exactly once.");
        s.Replay();
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Use(string skill, string activation, int[] targets) => Accept(new UseProgramSkillCommand(0, skill, activation, [], targets, G.Revision, P!.PromptId));
        public void Pause() => Reach(() => P?.SkillPrompt?.SkillId == Watch);
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(c is not null, "The exact native discard choice exists."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 250; i++)
            {
                if (ready()) return;
                if (P is { } p)
                {
                    if (p.SkillPrompt?.SkillId == Watch) { Continue(); continue; }
                    if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                    if (p.PlayerSeat == 0)
                    {
                        if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) { Answer(c => c.Cards.Count == 0); continue; }
                        throw new InvalidOperationException("Unexpected native discard human boundary. " + Dump());
                    }
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The native discard boundary remains bounded.");
        }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Native selected IDs, private projected counts and paid children cold-restore exactly."); }
        public void Replay()
        { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "The accepted real native group commands replay exactly."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), G.CardMovements,
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Need(bool yes, string message) { if (!yes) throw new InvalidOperationException(message + " " + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, P, Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray() });
        private void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Native group command rejected."); }
        public static Scene Start(string hero)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(hero));
            var setup = new ShanheBattleSetup(39, 1, "additional-native-self-discard", hero, 3, 0, 0, 0, 0, [Driver, Watch],
                [new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Club, 3), new(CardKind.Dodge, Suit.Diamond, 4), new(CardKind.Dodge, Suit.Spade, 5)], [],
                Enumerable.Range(1, 3).Select(i => new ShanheCombatantDefinition(hero == "ol:lv-kai" && i == 1 ? Peer + "-attacker" : Peer, Hp: hero == "ol:lv-kai" && i == 1 ? 7 : 8,
                    ExtraSkillIds: hero == "ol:lv-kai" && i == 1 ? [Watch] : []) { StartingHand = hero == "ol:lv-kai" && i == 1
                        ? [new(CardKind.SilverLion, Suit.Club, 1), new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Dodge, Suit.Heart, 6), new(CardKind.Dodge, Suit.Club, 7)]
                        : [new(CardKind.Dodge, Suit.Heart, 6), new(CardKind.Dodge, Suit.Club, 7), new(CardKind.Dodge, Suit.Spade, 8)] }).ToArray(), [], new string('b', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry);
            var scene = new Scene(g, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }
    private sealed class Fixture(string hero) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:additional-native-self-discard", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, hero);
            if (hero == "ol:lv-kai") b.AddCard(Classic.Value.GetCard("classic:silver-lion"));
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"hold","query":"handLimit","operation":"add","value":30,"priority":0}]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"paid-group","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementReasons":["skill-program.li-yi.chanshuang.discard","skill-program.yang-feng.jiawei.discard","skill-program.ol:bijing.bijing-punish"],"movementOccurrence":"perBatch","optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]},
                {"id":"paid-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"healed","options":[{"id":"continue"}]}]}
              ]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            { [Driver] = new { name = "保留真实材料", description = "稳定小局面。" }, [Watch] = new { name = "整组付款子窗", description = "完成实体移动后暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }, [Quiet] = new { name = "安静同桌", description = "真实回合跳过出牌。" } } }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "实际同桌", "", Quiet, "wei", 8));
            b.AddGeneral(new(Peer + "-attacker", "真实当前使用者", "", Driver, "wei", 8));
            b.AddDeck(new(Deck, "固定闪实体牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 90).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, 9)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "原生自弃整组选择", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [hero, Peer, Peer + "-attacker"], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
