using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class HappyShenGanNingChecks
{
    private const string Hero = "happy:shen-gan-ning", Poxi = "happy:poxi", Watch = "fixture:happy-gn-paid",
        HpWatch = "fixture:happy-gn-recovered", Quiet = "fixture:happy-gn-quiet", Peer = "fixture:happy-gn-peer", Deck = "fixture:happy-gn-deck";
    private const string CostReason = "skill-program.happy:poxi.discard", DrawReason = "skill-program.happy:poxi.draw";

    public static void ThreeSuitSharesAndRecoveryBeforeNativeDrawCold()
    {
        foreach (var ownCount in new[] { 0, 1, 2, 3 })
        {
            var s = Scene.Start(); s.Play();
            s.Need(s.V(0) is { Hp: 3, MaxHp: 6 }, "The distinct Happy general really starts at three HP out of six.");
            var handBefore = s.V(0).HandCount; var targetBefore = s.V(2).HandCount; var limitBefore = s.V(0).HandLimit;
            s.Use(); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "strategic-choice") == true);
            var draft = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Poxi);
            s.Need(draft.StrategicDraft is { Maximum: 3, Selected.Count: 0 }, "The shared private draft asks for precisely three distinct suits for the Happy rule.");
            var selected = new List<int>();
            var suits = new[] { Suit.Heart, Suit.Diamond, Suit.Spade };
            for (var i = 0; i < 3; i++)
            {
                var seat = i < ownCount ? 0 : 2;
                var id = s.V(seat).Hand.First(c => c.Suit == suits[i]).Id; selected.Add(id);
                s.Answer(c => c.Cards.SequenceEqual([id]));
            }

            long paidId;
            HappyPoxiReceipt receipt;
            if (ownCount > 0)
            {
                s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
                var paid = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.HappyPoxi is not null);
                paidId = paid.Id; receipt = paid.HappyPoxi!;
                s.Need(receipt is { Stage: HappyPoxiStage.DiscardChildren, Cost.Count: 3 } && receipt.OwnCount == ownCount &&
                    receipt.Cost.Select(c => c.CardId).SequenceEqual(selected) && paid.PendingMovementContinuation is not null &&
                    s.G.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(w => w.Batch.Id == receipt.BatchId && w.Batch.Movements.Count == 3 && w.Batch.Movements.Select(m => m.CardId).SequenceEqual(selected)) &&
                    s.V(0).HandCount == handBefore - ownCount && s.V(0).Hp == 3,
                    "The physical three-card cost has paid once before its original movement child and before any benefit.");
                if (ownCount == 2) s.Cold();
                s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
                if (ownCount == 2)
                {
                    s.Reach(() => s.P?.SkillPrompt?.SkillId == HpWatch);
                    s.Need(s.V(0).Hp == 4 && s.V(0).HandCount == handBefore - 2 &&
                        s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == paidId).HappyPoxi is { Stage: HappyPoxiStage.RecoveryChildren } &&
                        !s.E<HappyPoxiDrawReturnedEvent>().Any(),
                        "The native one-HP recovery and its actual child finish before the one-card draw can start.");
                    s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
                }
                s.Play();
            }
            else
            {
                s.Play();
                var paid = s.E<HappyPoxiPaidEvent>().Single(); paidId = paid.FrameId;
                receipt = new(paid.Source, paid.GameplayHash, paid.TargetSeat, paid.OwnCount, paid.ActualTurn, HappyPoxiStage.DiscardChildren)
                { Before = paid.Before, After = paid.After, BatchId = paid.BatchId };
            }

            var actual = s.G.CardMovements.Where(m => m.Reason.Value == CostReason).ToArray();
            var invoice = s.E<NativeLogicalDiscardInvocationRecordedEvent>().Single(e => e.ParentFrameId == paidId && e.Reason.Value == CostReason);
            s.Need(actual.Length == 3 && actual.Select(m => m.CardId).SequenceEqual(selected) && actual.All(m => m.To == CardLocation.DiscardPile && m.Sequence > receipt.Before && m.Sequence <= receipt.After) &&
                invoice.ActualCount == 3 && invoice.HandCount == 3 && s.E<HappyPoxiPaidEvent>().Count(e => e.FrameId == paidId) == 1 &&
                s.V(2).HandCount == targetBefore - (3 - ownCount) && s.G.ResolutionStack.OfType<ProgramSkillFrame>().All(f => f.HappyPoxi is null),
                "All three selected hand entities share their real original movement batch and logical-discard invoice, and the paid parent retires without repeating its cost.");
            var draw = s.E<HappyPoxiDrawReturnedEvent>().SingleOrDefault(e => e.FrameId == paidId);
            if (ownCount == 0)
                s.Need(s.V(0).MaxHp == 5 && s.V(0).Hp == 3 && s.V(0).HandCount == handBefore && draw is null,
                    "Discarding no own card pays precisely one maximum HP and gives no extra draw.");
            else if (ownCount == 1)
                s.Need(s.V(0) is { Hp: 3, MaxHp: 6 } && s.V(0).HandCount == handBefore - 1 && s.V(0).HandLimit == limitBefore && draw is null,
                    "Discarding one own card has no mobile-style Play termination or hand-limit penalty.");
            else
            {
                var reward = ownCount == 2 ? 1 : 3;
                s.Need(s.V(0).MaxHp == 6 && s.V(0).Hp == (ownCount == 2 ? 4 : 3) && s.V(0).HandCount == handBefore - ownCount + reward &&
                    draw is not null && draw.Requested == reward && draw.Actual == reward &&
                    s.G.CardMovements.Count(m => m.Reason.Value == DrawReason && m.To == CardLocation.Hand(0)) == reward,
                    "The real native reward draws exactly one after two own discards, or three after three own discards.");
                if (ownCount == 2)
                {
                    var facts = s.G.Events.Select(e => e.Payload).ToArray();
                    var recovery = Array.FindIndex(facts, e => e is RecoveryAppliedEvent { SourceSeat: 0, TargetSeat: 0, Amount: 1 });
                    var firstDraw = Array.FindIndex(facts, e => e is CardMovedEvent move && move.Reason.Value == DrawReason);
                    s.Need(s.E<RecoveryAppliedEvent>().Count(e => e.TargetSeat == 0) == 1 && recovery >= 0 && firstDraw > recovery,
                        "Cold recovery preserves the single physical payment and single recovery before the actual later draw movement.");
                    s.Replay();
                }
            }
            s.Need(!s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Poxi), "The completed activation still consumes its real once-per-Play allowance.");
        }
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Use() => Accept(new UseProgramSkillCommand(0, Poxi, "three-suit-discard", [], [2], G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> match) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(match).Id, G.Revision)); }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 100; i++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p) throw new InvalidOperationException("Unexpected Happy Gan Ning boundary " + JsonSerializer.Serialize(p));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Happy Gan Ning missed its bounded native occurrence.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "The original paid three-card discard and native recovery child restore identically."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(before == State(), "Replay preserves all real three-card selections, paid costs, recovery and reward draws."); }
        private void Accept(GameCommand c) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Happy Gan Ning command."); }
        public void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Facts = G.Events.TakeLast(5).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] hand = [new(CardKind.Slash, Suit.Heart, 1), new(CardKind.Slash, Suit.Diamond, 2), new(CardKind.Slash, Suit.Spade, 3), new(CardKind.Slash, Suit.Club, 4)];
            var setup = new ShanheBattleSetup(37, 1, "happy-gan-ning", Hero, 3, 0, 0, 0, 0, [Watch, HpWatch], hand, [],
                [new(Peer) { StartingHand = hand }, new(Peer) { StartingHand = [] }], [new(Peer) { StartingHand = [] }], new string('a', 64)) { DefaultGodFactionId = "wu" };
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
            var s = new Scene(game, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-happy-shen-gan-ning", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid","isSkillInvocation":false,"window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.happy:poxi.discard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen-paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{HpWatch}}","revision":1,"triggers":[{"id":"recovered","isSkillInvocation":false,"window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen-recovered","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object>
            {
                [Quiet] = new { name = Quiet, description = "保留真实回合的安静角色" },
                [Watch] = new { name = Watch, description = "原子三牌支付后的原生移动子窗口", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [HpWatch] = new { name = HpWatch, description = "真实回复先于摸牌", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静的真实角色", "", Quiet, "wei", 12));
            b.AddDeck(new(Deck, "固定实体牌", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "欢乐三色魄袭小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
