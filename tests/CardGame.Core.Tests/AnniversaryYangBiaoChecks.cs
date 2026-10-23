using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class AnniversaryYangBiaoChecks
{
    private const string Hero = "anniversary:yang-biao", Zhaohan = "anniversary:zhaohan", Jinjie = "anniversary:jinjie", Juee = "anniversary:juee",
        Peer = "fixture:yang-biao-peer", Driver = "fixture:yang-biao-driver", Watch = "fixture:yang-biao-watch", Quiet = "fixture:yang-biao-quiet", Deck = "fixture:yang-biao-deck";
    public static void NormalDrawObligationDyingHePaymentAndWholeRandomUsesCold()
    {
        var s = Scene.Start(); s.Offer(Driver); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        s.Offer(Zhaohan); var initialHand = s.V(0).HandCount; var weapon = s.V(0).Hand!.Single(c => c.Kind == CardKind.Crossbow).Id;
        var victim = Enumerable.Range(1, 3).First(seat => s.V(seat).HandCount == 0);
        var endingTarget = Enumerable.Range(1, 3).Single(seat => s.V(seat).HandCount != 0);
        s.Activate(); s.Menu(AnniversaryYangBiaoStage.ZhaohanBranch);
        s.Need(s.V(0).HandCount == initialHand + 4 && s.E<AnniversaryYangBiaoDrawArmedEvent>().Length == 1,
            "Zhaohan adds two to the real two-card draw before the original-instance obligation is presented.");
        s.Cold(); s.Answer(c => Option(c) == "gift"); s.Menu(AnniversaryYangBiaoStage.Recipient); s.Answer(c => c.Targets.SequenceEqual([victim]));
        s.Menu(AnniversaryYangBiaoStage.Materials); s.Answer(c => c.Cards is [var id] && id != weapon); s.Menu(AnniversaryYangBiaoStage.Materials); s.Answer(c => c.Cards is [var id] && id != weapon); s.Play();
        s.Need(s.V(victim).HandCount == 2 && s.V(0).HandCount == initialHand + 2 &&
            s.G.CardMovements.Count(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(victim) && m.Reason.Value == "skill-program.anniversary:zhaohan.give") == 2,
            "The chosen empty-handed receiver gains both real hand entities; giving them is not a discard.");
        s.Native(weapon); s.Play(); var beforeHand = s.V(0).HandCount; var victimHand = s.V(victim).HandCount;
        s.Use("dying", [victim]); s.Offer(Jinjie); s.Activate(); s.Menu(AnniversaryYangBiaoStage.Amount); s.Answer(c => Option(c) == "amount-2"); s.Menu(AnniversaryYangBiaoStage.RescuePay);
        s.Need(s.V(victim).Hp == 0 && s.V(victim).HandCount == victimHand + 2, "The exact real Dying recipient draws two before Yang Biao is asked to pay.");
        s.Answer(c => Option(c) == "pay"); s.Menu(AnniversaryYangBiaoStage.Materials); s.Answer(c => c.Cards.SequenceEqual([weapon])); s.Menu(AnniversaryYangBiaoStage.Materials); s.Answer(c => c.Cards.Count == 1);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        var paid = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.AnniversaryYangBiao?.Op == SkillProgramEffectOp.ResolveAnniversaryYangBiaoJinjie); var paidId = paid.Id;
        s.Need(paid.AnniversaryYangBiao is { Paid: true, Stage: AnniversaryYangBiaoStage.CostChildren, Amount: 2 } r && r.From.Contains(CardLocation.Equipment(0)) && r.From.Contains(CardLocation.Hand(0)) &&
            r.Cards.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.anniversary:yang-biao.discard") == 1) && s.V(victim).Hp == 0,
            "The current Jinjie pays real HE materials once before the native movement child and original victim recovery.");
        s.Cold(); s.Continue(); s.Play();
        s.Need(s.V(victim).Hp == 1 && s.V(0).HandCount == beforeHand - 1 && s.E<RecoveryAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == victim && e.Amount == 1) == 1 &&
            s.E<AnniversaryYangBiaoPaymentIssuedEvent>().Count(e => e.FrameId == paidId) == 1 && s.E<AnniversaryYangBiaoDrawReturnedEvent>().Count(e => e.FrameId == paidId && e.Actual == 2) == 1,
            "Cold returns through the real Dying recovery without drawing or paying the original HE cost twice.");
        s.Use("stock", []); s.Play(); var endingTurn = s.G.State.TurnNumber; s.End(); s.Offer(Juee); s.Activate(); s.Menu(AnniversaryYangBiaoStage.Recipient); s.Answer(c => c.Targets.SequenceEqual([endingTarget]));
        s.Reach(() => s.E<TurnEndedEvent>().Any(e => e.TurnNumber == endingTurn));
        var budget = s.E<AnniversaryYangBiaoBudgetEvent>().Single(); var uses = s.E<AnniversaryYangBiaoNativeIssuedEvent>().Where(e => e.FrameId == budget.FrameId).ToArray();
        s.Need(budget.Target == endingTarget && budget.DiscardCount >= 12 && budget.Maximum == 12 && budget.Budget == 12 && uses.Length == 12 && uses.Select(e => e.Ordinal).SequenceEqual(Enumerable.Range(0, 12)) &&
            uses.All(e => e.Target == endingTarget && s.E<CardUseDeclaredEvent>().Count(d => d.ResolutionId == e.UseId && d.CardId == 0 && d.CardKind == e.Kind && d.SourceSeat == 0) == 1 &&
                s.E<CardUseFinishedEvent>().Count(d => d.ResolutionId == e.UseId && d.CardId == 0 && d.CardKind == e.Kind) == 1 && s.E<AnniversaryYangBiaoNativeReturnedEvent>().Count(d => d.UseId == e.UseId) == 1),
            "The ending freezes the actual discard count capped by the chosen peer maximum, and each ordered native Use returns exactly once before issuing the next.");
        s.Need(new[] { CardKind.Slash, CardKind.Dismantlement, CardKind.FiveGrains }.All(k => uses.Any(e => e.Kind == k)) &&
            uses.Where(e => e.Kind == CardKind.FiveGrains).All(e => s.E<TargetsConfirmedEvent>().Single(d => d.ResolutionId == e.UseId).TargetSeats.SequenceEqual([endingTarget])) &&
            s.G.CardMovements.Any(m => m.From == CardLocation.Hand(endingTarget) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Dismantlement &&
                s.G.CardMovements.Any(done => done.CardId == m.CardId && done.From == CardLocation.Processing && done.To == CardLocation.DiscardPile && done.Reason == CardMoveReasons.DismantlementFinished)),
            "The fixed real short flow exercises a genuine Slash response/damage flow, true target-card dismantlement, and one-recipient native Five Grains rather than a global imitation.");
        s.Offer(Driver); var extraTurn = s.G.State.TurnNumber; var extraHand = s.V(0).HandCount; s.Activate();
        s.Offer(Zhaohan); s.Activate(); s.Menu(AnniversaryYangBiaoStage.ZhaohanBranch);
        s.Need(s.V(0).HandCount == extraHand + 4, "The inserted native Draw phase receives its own real +2 before paying its own obligation.");
        s.DiscardObligation(); s.Offer(Zhaohan); var normalHand = s.V(0).HandCount; s.Activate(); s.Menu(AnniversaryYangBiaoStage.ZhaohanBranch);
        s.Need(s.G.State.TurnNumber == extraTurn && s.V(0).HandCount == normalHand + 4, "The subsequent ordinary Draw in the same actual turn can independently choose another real +2.");
        s.DiscardObligation(); s.Play();
        var arms = s.E<AnniversaryYangBiaoDrawArmedEvent>().Where(e => e.Turn == extraTurn).ToArray();
        s.Need(arms.Length == 2 && arms.Select(e => e.DrawWindowId).Distinct().Count() == 2 && arms.Select(e => e.DrawPhaseId).Distinct().Count() == 2 && arms.Count(e => e.DrawPhaseId == 0) == 1 &&
            arms.All(arm => s.E<AnniversaryYangBiaoStartedEvent>().Count(e => e.ArmId == arm.FrameId) == 1) &&
            s.E<AnniversaryYangBiaoPaymentIssuedEvent>().Count(e => s.E<AnniversaryYangBiaoStartedEvent>().Any(start => start.FrameId == e.FrameId && start.Turn == extraTurn && start.Source.SkillId == Zhaohan) && e.Branch == "discard" && e.Count == 2) == 2 &&
            s.E<ProgramPhaseScheduledEvent>().Count(e => e.SkillId == Driver && e.Phase == TurnPhase.Draw && e.Started) == 1 &&
            s.E<ProgramPhaseScheduledEvent>().Count(e => e.SkillId == Driver && e.Phase == TurnPhase.Draw && !e.Started) == 1,
            "One inserted phase and the normal phase consume their two distinct original obligations once each, without a duplicate phase-ended settlement.");
        s.Replay();
    }
    private static string? Option(PromptChoice c) => c.Parameters.GetValueOrDefault("option");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        private ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryYangBiao is not null);
        public void Offer(string skill) => Reach(() => P?.SkillPrompt?.SkillId == skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Menu(AnniversaryYangBiaoStage stage) => Reach(() => Root?.AnniversaryYangBiao?.Stage == stage && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-yang-biao") == true);
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Use(string id, int[] targets) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets, G.Revision, P!.PromptId));
        public void Native(int id)
        {
            var a = G.GetHumanLegalActions().First(a => a.CardId == id && a.ConversionSource is null && a.TargetSeats.Count == 0);
            Accept(new PlayCardCommand(0, id, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void DiscardObligation()
        {
            Answer(c => Option(c) == "discard"); Menu(AnniversaryYangBiaoStage.Materials); Answer(c => c.Cards.Count == 1);
            Menu(AnniversaryYangBiaoStage.Materials); Answer(c => c.Cards.Count == 1);
        }
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(c is not null, "Missing exact original Yang Biao command.");
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision));
        }
        public void Reach(Func<bool> ready)
        {
            for (var step = 0; step < 220; step++)
            {
                if (ready()) return;
                if (P is { } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p is { Kind: DecisionKind.SelectTargetCard, PlayerSeat: 0, SourceSeat: 0 } &&
                        Root?.AnniversaryYangBiao is { Stage: AnniversaryYangBiaoStage.NativeUse, Plan.Kind: CardKind.Dismantlement } r &&
                        p.TargetSeat == r.Target && p.Choices.All(c => c.Cards.Count == 0 && c.Targets.SequenceEqual([r.Target]) && c.Parameters.GetValueOrDefault("action") == "target-card-slot"))
                    { Answer(c => c.Parameters.GetValueOrDefault("slot-index") == "0"); continue; }
                    if (p.PlayerSeat == 0 && p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) { Answer(c => c.Cards.Count == 0); continue; }
                    if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Yang Biao native boundary: " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The fixed native main flow must retain its living target."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Yang Biao missed the bounded exact real native continuation.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var state = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(state == State(), "Cold preserves the exact original Dying, draw recipient, real paid HE material and once-only native child return."); }
        public void Replay() { var state = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(state == State(), "The normal draw obligation, original Dying payment and random whole Use sequence replay identically."); }
        public void Accept(GameCommand c) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected exact Yang Biao command."); }
        public void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + " " + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Receipt = Root?.AnniversaryYangBiao, Budget = E<AnniversaryYangBiaoBudgetEvent>(), Uses = E<AnniversaryYangBiaoNativeIssuedEvent>(), Last = G.Events.TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var opening = new[] { new ShanheStartingCard(CardKind.Crossbow, Suit.Spade, 1), new ShanheStartingCard(CardKind.Slash, Suit.Club, 2), new ShanheStartingCard(CardKind.Slash, Suit.Heart, 3), new ShanheStartingCard(CardKind.Slash, Suit.Diamond, 4), new ShanheStartingCard(CardKind.Dodge, Suit.Club, 5), new ShanheStartingCard(CardKind.Peach, Suit.Heart, 6) };
            var setup = new ShanheBattleSetup(47, 1, "anniversary-yang-biao", Hero, 3, 0, 0, 0, 0, [Driver, Watch], opening, [],
                [new(Peer, Hp: 12) { StartingHand = [] }, new(Peer, Hp: 12) { StartingHand = [] }], [new(Peer, Hp: 12) { StartingHand = [new(CardKind.Slash, Suit.Club, 4)] }], new string('b', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var s = new Scene(game, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-yang-biao", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var c = SkillProgramCatalog.Load(Rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            {
                [Driver] = new { name = "原生杨彪驱动", description = "真实伤害与摸牌。" }, [Quiet] = new { name = "安静同桌", description = "真实跳过出牌。" },
                [Watch] = new { name = "弃装原生子窗", description = "原生支付后继续。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            } }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] });
            b.AddGeneral(new(Peer, "真实同桌", "", Quiet, "wei", 12));
            b.AddDeck(new(Deck, "固定真实牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 144).Select(i => new ContentDeckPhysicalCard("standard:slash", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "杨彪原生小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
        public static string Rules => $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
          {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
          {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-he","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:yang-biao.discard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid-he","options":[{"id":"continue"}]}]}]},
          {"id":"{{Driver}}","revision":1,"triggers":[{"id":"one-extra-draw","window":"turnStartBeforeNormalFlow","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"insertPhase","target":"owner","phase":"draw","phaseContinuation":"beforeNormalPreparation"}]}],"activations":[
            {"id":"dying","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":12}]},
            {"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":12}]}
          ]}
        ]}
        """;
    }
}
