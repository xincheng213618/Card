using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class OlLuShiChecks
{
    private const string Hero = "ol:lu-shi", Zhuyan = "ol:zhuyan", Leijie = "ol:leijie", Qiao = "happy:shen-da-qiao-xiao-qiao",
        Peer = "fixture:ol-lushi-peer", Driver = "fixture:ol-lushi-driver", NoDraw = "fixture:ol-lushi-no-draw",
        Quiet = "fixture:ol-lushi-quiet", HpHold = "fixture:ol-lushi-health", DrawHold = "fixture:ol-lushi-draw",
        UseHold = "fixture:ol-lushi-use", Deck = "fixture:ol-lushi-deck";

    public static void EndingHistoryLifetimeAdjustmentsAndNativeThunderPairResumeCold()
    {
        var s = Scene.Start(Suit.Club, 1, false); s.Play();
        s.Need(s.E<OlLuShiSnapshotEvent>().Count(e => e.Initial) == 4 && s.E<OlLuShiSnapshotEvent>().Single(e => e.Initial && e.Seat == 0) is { Hp: 3, HandCount: 1 },
            "Lu Shi freezes each real initial HP/hand count rather than inventing a history at activation.");
        s.Use("damage-one", [0]); s.Play(); s.End(); s.ZhuyanMenu(); s.Adjust(0, "hp");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == HpHold);
        var healing = s.Root!;
        s.Need(s.V(0).Hp == 3 && healing.OlLuShi is { Stage: OlLuShiStage.Children, Snapshot.Initial: true } &&
            s.E<RecoveryAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1) == 1,
            "The first ending restores initial HP through one actual recovery and pauses on its original health child.");
        s.Cold(); s.Skip(); s.Ended(1); s.Play();
        s.Need(s.E<OlLuShiSnapshotEvent>().Where(e => !e.Initial).All(e => e.BoundaryId > 0) &&
            s.E<OlLuShiSnapshotEvent>().Last(e => e.Seat == 1) is { Initial: false, Dual.DaQiaoHp: 3, Dual.XiaoQiaoHp: 3 },
            "A later participant's own ending records only that participant's real final values.");
        var qiaoHistory = s.E<OlLuShiSnapshotEvent>().Last(e => e.Seat == 1);
        s.Use("damage-one", [1]); s.Play();
        s.Need(s.V(1).HappyQiaoHealth == new HappyQiaoDualHealth(2, 3, 3) && s.V(1).HpUndefined,
            "The actual dual-health target is unequal before selecting Zhuyan; no default scalar HP can qualify it.");
        s.End(); s.ZhuyanMenu();
        s.Need(!s.P!.Choices.Any(c => c.Targets.SequenceEqual([0]) && c.Parameters.GetValueOrDefault("item") == "hp") &&
            s.P.Choices.Any(c => c.Targets.SequenceEqual([0]) && c.Parameters.GetValueOrDefault("item") == "hand") &&
            s.P.Choices.Any(c => c.Targets.SequenceEqual([1]) && c.Parameters.GetValueOrDefault("item") == "hp"),
            "Each participant/item is once per game, independently; an unequal two-pool participant remains a legal HP target.");
        s.Adjust(1, "hp"); s.Ended(2);
        var dualPaid = s.E<OlLuShiAdjustmentIssuedEvent>().Single(e => e.TargetSeat == 1 && e.Item == "hp");
        s.Need(dualPaid.Snapshot == qiaoHistory && s.V(1).HappyQiaoHealth == new HappyQiaoDualHealth(3, 3, 3) &&
            s.E<OlLuShiPoolAcceptedEvent>().Single() is { TargetSeat: 1, Pool: HappyQiaoPool.DaQiao, TargetHp: 3, Before.DaQiaoHp: 2, Before.XiaoQiaoHp: 3 } &&
            s.E<HappyQiaoRecoverToQueuedEvent>().Any(e => e.ProgramFrameId == dualPaid.FrameId && e.TargetHp == 3 && e.Pool == HappyQiaoPool.DaQiao && e.RequestedAmount == 1),
            "The real receiver chooses one pool and recovers that same pool to its own frozen historical value through the native RecoverTo invoice.");
        s.Play(); s.Use("recover-three", [2]); s.Play();
        s.Need(s.V(2).Hp == 5, "The next target's live HP differs from its previous own ending.");
        var lossHistory = s.E<OlLuShiSnapshotEvent>().Last(e => e.Seat == 2);
        s.End(); s.ZhuyanMenu(); s.Adjust(2, "hp"); s.Ended(3);
        var loss = s.E<OlLuShiAdjustmentIssuedEvent>().Single(e => e.TargetSeat == 2 && e.Item == "hp");
        s.Need(loss.Snapshot == lossHistory && lossHistory.Hp == 2 && s.V(2).Hp == 2 &&
            s.E<ProgramSkillHpLostEvent>().Count(e => e.FrameId == loss.FrameId && e.TargetSeat == 2 && e.Amount == 3) == 1 &&
            !s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 2),
            "A downward adjustment is one native three-HP loss to the last own-ending checkpoint, rather than damage or direct HP setting.");
        s.Play(); var handHistory = s.E<OlLuShiSnapshotEvent>().Last(e => e.Seat == 3);
        s.Use("draw-two", [3]); s.Play(); s.End(); s.ZhuyanMenu(); s.Adjust(3, "hand"); s.Ended(4);
        var discard = s.E<OlLuShiAdjustmentIssuedEvent>().Single(e => e.TargetSeat == 3 && e.Item == "hand");
        s.Need(discard.Snapshot == handHistory && handHistory.HandCount == 2 && s.V(3).HandCount == 2 &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ExecutorSeat == 3 && e.HandCount == 2 && e.Reason.Value == "skill.ol.zhuyan.discard") == 1,
            "A real receiver privately discards exactly the excess hand entities, with one original logical-discard batch.");

        var t = Scene.Start(Suit.Spade, 7, true); t.Play();
        t.UseLeijie(2); t.Reach(() => t.P?.SkillPrompt?.SkillId == UseHold);
        var first = t.Root!; var use = t.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.OlLuShiLeijie is not null);
        t.Need(first.OlLuShi is { SlashCount: 1, Stage: OlLuShiStage.NativeUse } && use is { CardId: 0, CardKind: CardKind.ThunderSlash, PhysicalCardIds.Count: 0 } &&
            use.Action is { EffectiveSuit: Suit.None, EffectiveRank: 0, PhysicalCards.Count: 0 } && use.TargetSeats.SequenceEqual([2]) &&
            t.E<JudgmentResolvedEvent>().Count(e => e.Reason == "skill.ol.lushi.leijie" && e.Suit == Suit.Spade && e.Rank == 5) == 1 &&
            !t.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == use.Id),
            "The finalized black-spade judgment issues one real zero-entity thunder Slash against the original distance-two target, then pauses before any second use.");
        t.Cold(); t.Skip(); t.Play();
        var issued = t.E<OlLuShiSlashIssuedEvent>().Select(e => e.Origin).ToArray();
        t.Need(issued.Length == 2 && issued.Select(e => e.Ordinal).SequenceEqual([1, 2]) && issued.All(e => e.TargetSeat == 2) &&
            t.E<OlLuShiSlashReturnedEvent>().Length == 2 && issued.All(e => t.E<CardUseFinishedEvent>().Count(f => f.ResolutionId == e.UseId && f.CardKind == CardKind.ThunderSlash) == 1) &&
            t.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Nature == DamageNature.Thunder && e.Amount == 1) == 2 && t.V(2).Hp == 4 &&
            Array.FindIndex(t.G.Events.ToArray(), e => e.Payload is CardUseFinishedEvent f && f.ResolutionId == issued[0].UseId) < Array.FindIndex(t.G.Events.ToArray(), e => e.Payload is OlLuShiSlashIssuedEvent f && f.Origin == issued[1]),
            "Cold preserves one judgment and two distinct whole native uses; the second is issued only after the first use really finishes.");
        t.End(); t.ZhuyanMenu(); t.Adjust(0, "hand"); t.Reach(() => t.P?.SkillPrompt?.SkillId == DrawHold);
        var cap = t.Root!;
        t.Need(cap.OlLuShi is { Stage: OlLuShiStage.Drawing, Amount: 2, Snapshot.HandCount: 7 } && t.V(0).HandCount == 3 &&
            t.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any(f => f.Context.ParentFrameId == cap.Id && f.Context.RequestedCount == 2),
            "Only the draw side is capped at five: historical seven and current three produce a real two-card native draw.");
        t.Cold(); t.Skip(); t.Ended(1);
        t.Need(t.V(0).HandCount == 5 && t.E<OlLuShiMovementEvent>().Count(e => e.FrameId == cap.Id && e.Draw && e.Count == 2) == 1,
            "The frozen native draw returns once after cold restore and completes the adjustment at five cards.");

        var n = Scene.Start(Suit.Club, 0, true); n.Play(); n.UseLeijie(0); n.Reach(() => n.P?.SkillPrompt?.SkillId == DrawHold);
        n.Need(n.Root!.OlLuShi is { Item: "draw", TargetSeat: 0, Amount: 2 } && n.E<OlLuShiSlashIssuedEvent>().Length == 0,
            "A self-targeted non-spade judgment follows the true two-card draw branch without inventing self damage or a different target.");
        n.Skip(); n.Play(); n.Need(n.V(0).HandCount == 2 && n.E<OlLuShiMovementEvent>().Single() is { Draw: true, Count: 2, TargetSeat: 0 },
            "The non-spade branch completes its actual two-card draw on the judged participant.");
    }

    private static string? Action(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private sealed class Fixture(Suit suit) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ol-lushi", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{HpHold}}","revision":1,"triggers":[{"id":"health","isSkillInvocation":false,"window":"afterHealthChanged","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{DrawHold}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{UseHold}}","revision":1,"triggers":[{"id":"use","isSkillInvocation":false,"window":"cardUseCommitted","cardKinds":["thunderSlash"],"ownerRelation":"actor","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"damage-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"recover-three","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"selectedTarget","amount":3}]},
                {"id":"draw-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":2}]}]}
            ]}
            """;
            var labels = new[] { NoDraw, Quiet, HpHold, DrawHold, UseHold, Driver }.ToDictionary(id => id, id => new { name = id, description = "卢氏原生历史、调整与真实雷杀" });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "真实结束阶段目标", "", Quiet, "wei", 8));
            b.AddGeneral(new(Qiao, "真实双池目标", "", Quiet, "wu", 3));
            b.AddDeck(new(Deck, "固定判定实体", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 96).Select(_ => new ContentDeckPhysicalCard("standard:slash", suit, 5)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "卢氏重制原生流程", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer, Qiao], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.OlLuShi is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Ended(int count) => Reach(() => E<TurnEndedEvent>().Count(e => e.ActorSeat == 0) >= count);
        public void Use(string id, int[] targets) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets, G.Revision, P!.PromptId));
        public void UseLeijie(int target) => Accept(new UseProgramSkillCommand(0, Leijie, "judge-target", [], [target], G.Revision, P!.PromptId));
        public void ZhuyanMenu()
        {
            Reach(() => P?.SkillPrompt?.SkillId == Zhuyan && P.Choices.Any(c => Action(c) == "activate")); Answer(c => Action(c) == "activate");
            Reach(() => P?.Choices.Any(c => Action(c) == "ol-lushi") == true);
        }
        public void Adjust(int target, string item) => Answer(c => c.Targets.SequenceEqual([target]) && c.Parameters.GetValueOrDefault("item") == item);
        public void Skip() => Answer(c => Action(c) == "skip");
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P!; var choice = p.Choices.FirstOrDefault(predicate); Need(choice is not null, "Missing exact current Lu Shi choice.");
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision));
        }
        public void Reach(Func<bool> done)
        {
            for (var step = 0; step < 240; step++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId is HpHold or DrawHold or UseHold or Zhuyan && p.Choices.Any(c => Action(c) == "skip")) Skip();
                else if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected bounded Lu Shi boundary: " + Dump());
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Lu Shi missed her bounded native flow: " + Dump());
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();
            var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) && frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))),
                "Cold must retain the exact historical checkpoint, lifetime target item, native draw/use cursor and typed child return.");
        }
        public void Accept(GameCommand command)
        {
            var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Lu Shi command.");
        }
        public void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Root = Root?.OlLuShi,
            Snapshots = E<OlLuShiSnapshotEvent>(), Adjustments = E<OlLuShiAdjustmentIssuedEvent>(), Hp = E<RecoveryAppliedEvent>(), Loss = E<ProgramSkillHpLostEvent>(), Slashes = E<OlLuShiSlashIssuedEvent>() });
        public static Scene Start(Suit suit, int handCount, bool healthyPeers)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(suit));
            var qiao = new ShanheCombatantDefinition(Qiao, Hp: 3, ExtraSkillIds: [NoDraw]) { StartingHand = [] };
            var low = new ShanheCombatantDefinition(Peer, Hp: healthyPeers ? 6 : 2, ExtraSkillIds: [NoDraw]) { StartingHand = [] };
            var ally = new ShanheCombatantDefinition(Peer, Hp: 6, ExtraSkillIds: [NoDraw]) { StartingHand = healthyPeers ? [] : [new(CardKind.Slash, Suit.Club, 7), new(CardKind.Slash, Suit.Club, 7)] };
            var setup = new ShanheBattleSetup(53, 1, "ol-lushi", Hero, 3, 0, 0, 0, 0, [NoDraw, Driver, HpHold, DrawHold, UseHold],
                Enumerable.Range(0, handCount).Select(_ => new ShanheStartingCard(CardKind.Slash, Suit.Club, 7)).ToArray(), [], [low, ally], [qiao], new string('c', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 24 }, registry);
            var scene = new Scene(game, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }
}
