using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheS5GongsunChecks
{
    private const string Qiaomeng = StandardShanheS5GongsunZanPackage.QiaomengId, Jiuzhu = StandardShanheS5GongsunZanPackage.JiuzhuId;
    private const string Driver = "fixture:s5-gongsun-driver", Watch = "fixture:s5-gongsun-watch", Armor = "fixture:s5-gongsun-armor",
        Quiet = "fixture:s5-gongsun-quiet", Peer = "fixture:s5-gongsun-peer", Target = "fixture:s5-gongsun-target", Deck = "fixture:s5-gongsun-deck";
    private const string Discard = "skill-program.shanhe-qiaomeng.discard", RescueCost = "skill-program." + Jiuzhu + ".SelectAndMoveOwnedCard";

    public static void NativeSlashRangeThreeAreaDiscardBeforeDodgeAndPaidCold()
    {
        var s = Scene.Start("qiaomeng"); s.Play(); s.Card(CardKind.QinglongCrescentBlade); s.Play(); s.Card(CardKind.Indulgence, 1); s.Play();
        Need(s.V().Equipment.Single().Kind == CardKind.QinglongCrescentBlade && s.G.GetAttackRange(0) == 3 &&
            s.V(1).Equipment.Single().Kind == CardKind.SilverLion && s.V(1).Judgment.Count == 1 && s.V(1).HandCount == 2,
            "An actual native weapon use establishes range three; the real target has two hidden Dodges, a real Silver Lion and a placed delayed trick.");
        s.Card(CardKind.Slash, 1); s.Until(() => s.P?.SkillPrompt?.SkillId == Qiaomeng); s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Until(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "shanhe-qiaomeng-card") == true);
        var started = s.E<ShanheS5QiaomengStartedEvent>().Single();
        Need(started is { OwnerSeat: 0, TargetSeat: 1, FrozenRange: 3, RequiredCount: 3 } &&
            !s.E<DamageAppliedEvent>().Any() && s.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).All(c => c.Cards.Count == 0),
            "The actual designated-target window freezes attack range, precedes any Slash damage and preserves foreign hand-slot privacy.");
        s.Answer(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Equipment));
        s.Answer(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Judgment)); s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
        s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
        var paid = s.E<ShanheS5QiaomengPaidEvent>().Single();
        var moves = s.G.CardMovements.Where(m => m.Reason.Value == Discard).ToArray();
        Need(paid.Count == 3 && paid.FrameId == started.FrameId && paid.UseFrameId == started.UseFrameId &&
            moves.Length == 3 && moves.Select(m => m.From.Zone).Order().SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment }.Order()) &&
            s.V(1).Hp == 2 && !s.E<DamageAppliedEvent>().Any() &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == started.FrameId).ShanheS5Qiaomeng is { Paid: true, RequiredCount: 3 },
            "The exact three HEJ materials enter one real logical discard before its movement child; Silver Lion's native removal recovery already returned to the original paid instruction.");
        s.Cold(); s.Continue(); s.Play();
        Need(!s.E<DamageAppliedEvent>().Any() && s.E<CardRespondedEvent>().Any(e => e.ResponderSeat == 1 && e.EffectiveCardKind == CardKind.Dodge) &&
            s.E<ShanheS5QiaomengPaidEvent>().Length == 1 && s.G.CardMovements.Count(m => m.Reason.Value == Discard) == 3 &&
            s.V().HandCount == 0 && s.V(1).Equipment.Count == 0 && s.V(1).Judgment.Count == 0,
            "The remaining real Dodge fully cancels the native Slash; range-three discards remain paid once and do not require damage or add an unrelated mount/draw reward.");
        s.Cold();
    }

    public static void JiuzhuActualRepeatedAllyRescuePaidColdAndQualification()
    {
        var s = Scene.Start("jiuzhu"); s.Play(); s.Use("fall", [1]); s.AtRescue(); s.Cold(); s.Rescue();
        s.Until(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
        s.Answer(c => c.Cards.Count == 1); s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
        var root = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Jiuzhu);
        Need(s.V().Hp == 4 && s.V(1).Hp == -1 && s.G.CardMovements.Count(m => m.Reason.Value == RescueCost) == 1 &&
            root.InstructionIndex == 2 && root.PendingMovementContinuation is not null,
            "A real other allied dying character publishes Jiuzhu; its first HE cost is truly discarded and paused before HP loss and recovery on that same frame.");
        s.Cold(); s.Continue(); s.AtRescue();
        Need(s.V().Hp == 3 && s.V(1).Hp == 0 && s.E<ProgramRecoveryAppliedEvent>().Single(e => e.ProgramFrameId == root.Id).Receipt is { HpBefore: -1, HpAfter: 0 },
            "The paid child returns once: the rescuer loses one HP and the victim gains exactly one rather than recovering to one.");
        s.Rescue(); s.Until(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
        s.Answer(c => c.Cards.Count == 1); s.Play();
        Need(s.V().Hp == 2 && s.V(1) is { Hp: 1, IsAlive: true } && s.G.CardMovements.Count(m => m.Reason.Value == RescueCost) == 2 &&
            s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == Jiuzhu) == 2,
            "The real same-seat rescue loop may pay another available card and HP while above one, then saves the ally with two independent one-point recoveries.");
        s.Cold(); s.Use("lose"); s.Play(); s.Use("hurt", [1]); s.Play();
        Need(s.V().Hp == 1 && !s.V(1).IsAlive && s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == Jiuzhu) == 2 &&
            s.G.CardMovements.Count(m => m.Reason.Value == RescueCost) == 2,
            "At one HP the remaining owned card cannot authorize Jiuzhu or create another payment.");
        s.Cold();
        var enemy = Scene.Start("jiuzhu"); enemy.Play(); enemy.Use("hurt", [2]); enemy.Play();
        Need(!enemy.V(2).IsAlive && enemy.V().Hp == 4 && !enemy.E<ProgramBindingStartedEvent>().Any(e => e.SkillId == Jiuzhu) &&
            !enemy.G.CardMovements.Any(m => m.Reason.Value == RescueCost),
            "A real enemy dying occurrence never offers or pays the ally-only rescue.");
    }

    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Accept(GameCommand c) { var r = G.Submit(c); Need(r.Accepted, r.Error?.ToString() ?? "Actual Gongsun command rejected."); }
        public void Answer(Func<PromptChoice, bool> pick) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, G.Revision)); }
        public void Until(Func<bool> done)
        {
            for (var i = 0; i < 160; i++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The small Gongsun fixture keeps its battle running.");
                if (P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Jiuzhu &&
                    p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"))
                { Answer(c => c.Targets.SequenceEqual([1])); continue; }
                if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Gongsun decision: " + JsonSerializer.Serialize(P));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The actual Gongsun continuation was not reached.");
        }
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Card(CardKind kind, int? target = null)
        {
            var card = V().Hand.First(c => c.Kind == kind);
            var a = G.GetHumanLegalActions().First(a => a.CardId == card.Id && (target is null || a.TargetSeats.SequenceEqual([target.Value])));
            Accept(new PlayCardCommand(0, card.Id, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
        }
        public void Use(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void AtRescue() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        public void Rescue() => Answer(c => c.Parameters.GetValueOrDefault("response") == "program-trigger" && c.Parameters.GetValueOrDefault("skill-id") == Jiuzhu);
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(G.CreateSnapshot(s))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint()) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "The actual use, private slots, paid discard, dying response and native children cold-restore identically."); }
        public static Scene Start(string mode)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
                new StandardClassicGeneralPackage(), new StandardShanheS5GongsunZanPackage(), new Fixture(mode));
            var rescue = mode == "jiuzhu";
            var setup = new ShanheBattleSetup(91, 1, "fixture-gongsun", rescue ? StandardShanheS5GongsunZanPackage.GongsunYueId : StandardShanheS5GongsunZanPackage.GongsunZanId,
                4, 0, 0, 0, 0, [Driver, ..(rescue ? new[] { Watch } : Array.Empty<string>())],
                rescue ? [new(CardKind.Dodge), new(CardKind.Dodge), new(CardKind.Dodge)] : [new(CardKind.Slash), new(CardKind.Indulgence), new(CardKind.QinglongCrescentBlade)], [],
                rescue ? [new(Peer, MaxHpBonus: 8, Hp: 1) { StartingHand = [] }, new(Peer, MaxHpBonus: 8) { StartingHand = [] }] :
                    [new(Target, Hp: 1) { StartingHand = [new(CardKind.Dodge), new(CardKind.Dodge)] }, new(Peer, MaxHpBonus: 8) { StartingHand = [] }, new(Peer, MaxHpBonus: 8) { StartingHand = [] }],
                rescue ? [new(Peer, MaxHpBonus: 8, Hp: 1) { StartingHand = [] }] : [], new string('a', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                HumanRole = null, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r);
            var s = new Scene(g, r); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture(string mode) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-gongsun", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":40,"priority":0}],"activations":[
                {"id":"fall","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
              {"id":"{{{Armor}}}","revision":1,"triggers":[{"id":"armor","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"useRandomDeckEquipment","target":"owner","targetRef":{"kind":"owner"},"resultBind":"armor"}]}]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment","judgment"],"movementReasons":["{{{Discard}}}","{{{RescueCost}}}"],"movementOccurrence":"perOwnerBatch","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var labels = new[] { Driver, Armor, Quiet, Watch }.ToDictionary(id => id, id => id == Watch
                ? (object)new { name = "真实付款子窗", description = "本帧移牌后继续", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                : new { name = "原生小局驱动", description = "真实装备与濒死入口" });
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] });
            b.AddGeneral(new(Peer, "静默同桌", "supporter", Quiet, "qun", 4));
            b.AddGeneral(new(Target, "真实防御目标", "supporter", Armor, "qun", 4, [Quiet, Watch]));
            b.AddDeck(new(Deck, "固定真实牌堆", 0, 0, []) { PhysicalCards = new[] { new ContentDeckPhysicalCard("classic:silver-lion", Suit.Club, 7) }
                .Concat(Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7))).ToArray() });
            var rescue = mode == "jiuzhu";
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "公孙原生小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [StandardShanheS5GongsunZanPackage.GongsunZanId, StandardShanheS5GongsunZanPackage.GongsunYueId, Peer, Target],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = rescue ? 2 : 1, [ShanheBattleSetup.EnemyTeamId] = rescue ? 2 : 3 }));
        }
    }
}
