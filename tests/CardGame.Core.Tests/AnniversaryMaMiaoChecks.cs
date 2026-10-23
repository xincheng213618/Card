using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class AnniversaryMaMiaoChecks
{
    private const string Hero = "anniversary:ma-miao", Zhangguan = "anniversary:zhangguan", Congfeng = "anniversary:congfeng", Peer = "fixture:ma-miao-peer",
        Driver = "fixture:ma-miao-driver", NoDraw = "fixture:ma-miao-no-draw", Quiet = "fixture:ma-miao-quiet", Watch = "fixture:ma-miao-paid-gain", DrawHold = "fixture:ma-miao-native-draw", Echo = "fixture:ma-miao-independent-gift";
    public static void EveryTurnAdjustmentRecoveryAndPairedNativeRandomGainsCold()
    {
        var s = Scene.Start(); s.Offer(); s.Skip(); s.Play();
        var gifts = s.V().Hand!.Take(2).Select(c => c.Id).ToArray();
        s.Gift(gifts, 1); s.Play();
        s.Need(gifts.All(id => s.Zone(id) == CardLocation.Hand(1)) && gifts.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1)) == 1),
            "The two selected originals really transfer once from Ma Miao to the selected recipient.");
        s.Need(s.E<AnniversaryMaMiaoGainQualifiedEvent>() is [{ Recipient: 1, Holder: 0 }],
            "The original two-card batch issues one paired Congfeng qualification, and the skill's own direct extra never issues a recursive qualification.");
        s.Need(s.E<AnniversaryMaMiaoDecisionEvent>() is [{ Chooser: 1, Accepted: true }],
            "The other actual recipient must enter the original Congfeng child and choose the extra.");
        s.Need(s.E<AnniversaryMaMiaoRandomEvent>() is [{ Recipient: 1, Holder: 0 }],
            "Accepting the extra must issue exactly one native random selection from Ma Miao's remaining owned cards.");
        var firstExtra = s.E<AnniversaryMaMiaoRandomEvent>().Single();
        s.Need(s.V().HandCount == 4 && s.V(1).HandCount == 7 && s.Zone(firstExtra.CardId) == CardLocation.Hand(1) &&
            s.G.CardMovements.Count(m => m.CardId == firstExtra.CardId && m.From == firstExtra.From && m.To == CardLocation.Hand(1) && m.Reason.Value == "skill-program.anniversary:congfeng.extra") == 1 &&
            s.E<AnniversaryMaMiaoMovementEvent>().Count(e => e.FrameId == firstExtra.FrameId && e.Mode == "gain" && e.Count == 1) == 1,
            "The accepted extra pays one real native movement, leaving both originals and the one extra with the actual recipient.");
        s.End(); s.Offer();
        s.Need(s.G.State.CurrentSeat == 1 && s.G.State.TurnNumber == 2, "Zhangguan is offered at another role's real turn start, not only Ma Miao's own preparation.");
        s.Activate(); s.Stage("discard"); var turnRoot = s.Root!.Id;
        s.Need(s.Root.AnniversaryMaMiao is { InitialHand: 4, AdjustmentHp: 3, Count: 1 }, "Adjustment uses current HP3 rather than MaxHp4.");
        s.Answer(c => c.Cards.Count == 1); s.Stage("obtain-choice");
        s.Need(s.V().Hp == 4 && s.V().HandCount == 3 && s.E<AnniversaryMaMiaoRecoveryIssuedEvent>().Count(e => e.FrameId == turnRoot) == 1 &&
            s.E<RecoveryAppliedEvent>().Any(e => e.TargetSeat == 0 && e.Amount == 1) &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == turnRoot && e.ExecutorSeat == 0 && e.ActualCount == 1) == 1 &&
            s.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == "Hand").All(c => c.Cards.Count == 0),
            "A real decrease first pays its native discard and native recovery, then still obtains a card under the current rule, using opaque foreign Hand slots.");
        s.Answer(c => c.Parameters.GetValueOrDefault("source-zone") == "Hand"); s.Stage("extra-choice");
        var extraRoot = s.Root!.Id; var randoms = s.E<AnniversaryMaMiaoRandomEvent>().Length;
        s.Need(s.P!.PlayerSeat == 0 && s.Root.AnniversaryMaMiao is { Recipient: 0, Holder: 1 }, "The reciprocal direction is decided by the actual gaining Ma Miao.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "accept"); s.HoldGain();
        var paid = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == extraRoot).AnniversaryMaMiao!;
        var selected = s.E<AnniversaryMaMiaoRandomEvent>().Single(e => e.FrameId == extraRoot);
        s.Need(paid.Stage == "gain-children" && paid.Gain is { Movements.Count: 1 } && paid.RandomPool.Count > 0 && selected.Index >= 0 &&
            paid.RandomPool[selected.Index].CardId == selected.CardId && s.Zone(selected.CardId) == CardLocation.Hand(0) && s.E<AnniversaryMaMiaoRandomEvent>().Length == randoms + 1,
            "The selected original entity has paid a real typed gain before its independent native observer pauses; the random receipt remains on the producing frame.");
        s.Cold(); s.Continue(); s.EchoTarget(); s.Answer(c => c.Targets.SequenceEqual([2])); s.EchoCard();
        var echoRoot = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Echo).Id;
        var echoed = s.P!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        s.Answer(c => c.Cards.Count == 1);
        s.Reach(() => s.E<AnniversaryMaMiaoCompletedEvent>().Any(e => e.FrameId == turnRoot));
        var gains = s.E<AnniversaryMaMiaoGainQualifiedEvent>(); var takes = s.E<AnniversaryMaMiaoRandomEvent>();
        s.Need(takes.Count(e => e.FrameId == extraRoot) == 1 && s.E<AnniversaryMaMiaoMovementEvent>().Count(e => e.FrameId == extraRoot && e.Mode == "gain") == 1 &&
            s.G.CardMovements.Count(m => m.CardId == selected.CardId && m.From == selected.From && m.Reason.Value == "skill-program.anniversary:congfeng.extra") == 1,
            "Cold never redraws the original accepted randomness or repays its already completed native extra.");
        var echoMoves = s.G.CardMovements.Where(m => m.CardId == echoed && m.Reason.Value == $"skill-program.{Echo}.SelectAndMoveOwnedCard").ToArray();
        s.Need(echoMoves is [{ From: var echoFrom, To: var echoProcessing }, { From: var echoReturn, To: var echoTo }] &&
            echoFrom == CardLocation.Hand(0) && echoProcessing == CardLocation.Processing && echoReturn == CardLocation.Processing && echoTo == CardLocation.Hand(2) &&
            s.E<AnniversaryMaMiaoTransferProducerEvent>().Any(e => e.ProducerId == echoRoot && !e.NativeSnatch && e.Recipient == 2 && e.EntrySequence == echoMoves[0].Sequence),
            "The genuinely different skill pays its own complete native owned-Hand to Processing to other-Hand transfer, retaining its exact producer proof.");
        s.Need(gains.Length == 3 && gains.Any(e => e.Recipient == 2 && e.Holder == 0 && e.Sequence == echoMoves[1].Sequence) &&
            takes.Select(e => e.Recipient).SequenceEqual([1, 0, 2]) && s.E<AnniversaryMaMiaoDecisionEvent>().Where(e => e.Accepted).Select(e => e.Chooser).SequenceEqual([1, 0, 2]),
            "The independent skill's real terminal batch creates its own reciprocal Congfeng for the actual recipient; each direct Congfeng extra remains excluded.");
        s.Need(s.E<AnniversaryMaMiaoAdjustedEvent>().Single(e => e.FrameId == turnRoot) is { InitialHand: 4, HandAfter: 3, Hp: 3 },
            "The original every-turn adjustment keeps its real HP-based decrease after all genuine nested acquisition children return.");
    }
    public static void NativeRefillThenUnchangedHandRecoversAndCanDeclineExtra()
    {
        var s = Scene.Start(shortHand: true); s.Offer(); s.Activate(); s.HoldDraw();
        var first = s.Root!.Id;
        s.Need(s.Root.AnniversaryMaMiao is { Stage: "drawing", InitialHand: 1, AdjustmentHp: 3, Count: 2 } &&
            s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any(f => f.Context.ParentFrameId == first && f.Context.RequestedCount == 2),
            "A deficient Hand issues the original native two-card adjustment request at the true own turn start.");
        s.Cold(); s.Continue(); s.Play();
        s.Need(s.V().Hp == 3 && s.V().HandCount == 3 && s.E<AnniversaryMaMiaoAdjustedEvent>().Single(e => e.FrameId == first) is { InitialHand: 1, HandAfter: 3 } &&
            !s.E<AnniversaryMaMiaoRecoveryIssuedEvent>().Any(e => e.FrameId == first) && !s.E<AnniversaryMaMiaoRandomEvent>().Any(),
            "A genuine increase stops the skill after the single restored native draw, without recovery or taking another role's card.");
        s.End(); s.Offer(); s.Activate(); s.Stage("obtain-choice"); var second = s.Root!.Id;
        s.Need(s.V().Hp == 4 && s.E<AnniversaryMaMiaoAdjustedEvent>().Single(e => e.FrameId == second) is { InitialHand: 3, HandAfter: 3 } &&
            s.E<AnniversaryMaMiaoRecoveryIssuedEvent>().Count(e => e.FrameId == second) == 1,
            "An unchanged Hand also recovers then proceeds to real acquisition, instead of following the obsolete split rule.");
        s.Answer(c => c.Parameters.GetValueOrDefault("source-zone") == "Hand"); s.Stage("extra-choice");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "decline");
        s.Reach(() => s.E<AnniversaryMaMiaoCompletedEvent>().Any(e => e.FrameId == second));
        s.Need(s.E<AnniversaryMaMiaoDecisionEvent>() is [{ Chooser: 0, Accepted: false }] && !s.E<AnniversaryMaMiaoRandomEvent>().Any() && s.V().HandCount == 4,
            "The real recipient may refuse the extra; the original acquisition remains paid once without issuing a random selection.");
        s.Play(); s.Snatch(1); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectTargetCard });
        var snatchRoot = s.G.ResolutionStack.OfType<CardUseFrame>().Last().Id;
        s.Answer(_ => true); s.Stage("extra-choice");
        var transfer = s.E<AnniversaryMaMiaoTransferProducerEvent>().Single(e => e.NativeSnatch && e.ProducerId == snatchRoot);
        var snatchGain = s.E<AnniversaryMaMiaoGainQualifiedEvent>().Last();
        var entry = s.G.CardMovements.Single(m => m.Sequence == transfer.EntrySequence);
        var terminal = s.G.CardMovements.Single(m => m.Sequence == snatchGain.Sequence);
        s.Need(transfer.Recipient == 0 && snatchGain.Recipient == 0 && snatchGain.Holder == 1 && entry.CardId == terminal.CardId &&
            entry.From == CardLocation.Hand(1) && entry.To == CardLocation.Processing && entry.Reason == CardMoveReasons.Snatch &&
            terminal.From == CardLocation.Processing && terminal.To == CardLocation.Hand(0) && terminal.Reason == CardMoveReasons.SnatchFinished &&
            s.E<TargetCardTakenEvent>().Any(e => e.ResolutionId == snatchRoot && e.SourceSeat == 0 && e.TargetSeat == 1 && e.FromZone == CardZoneKind.Hand),
            "A real native Snatch also qualifies its original owned-Hand transfer across Processing using its exact card-use and taken-card producer proof.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "decline"); s.Play();
        s.Need(s.E<AnniversaryMaMiaoGainQualifiedEvent>().Length == 2 && s.E<AnniversaryMaMiaoDecisionEvent>().Length == 2 &&
            s.E<AnniversaryMaMiaoDecisionEvent>().All(e => e.Chooser == 0 && !e.Accepted) && !s.E<AnniversaryMaMiaoRandomEvent>().Any(),
            "Both original acquisitions remain real and paid, while declining either extra never advances Congfeng randomness.");
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryMaMiao is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Offer() => Reach(() => P?.SkillPrompt?.SkillId == Zhangguan && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") != "activate");
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Stage(string stage) => Reach(() => Root?.AnniversaryMaMiao?.Stage == stage && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-ma-miao") == true);
        public void HoldGain() => Reach(() => P?.SkillPrompt?.SkillId == Watch);
        public void HoldDraw() => Reach(() => P?.SkillPrompt?.SkillId == DrawHold);
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void EchoTarget() => Reach(() => P?.SkillPrompt?.SkillId == Echo && P.Choices.Any(c => c.Targets.SequenceEqual([2])));
        public void EchoCard() => Reach(() => P?.SkillPrompt?.SkillId == Echo && P.Choices.Any(c => c.Cards.Count == 1));
        public void Gift(IReadOnlyList<int> ids, int peer) => Accept(new UseProgramSkillCommand(0, Driver, "gift-batch", ids, [peer], G.Revision, P!.PromptId));
        public void Snatch(int target)
        {
            var action = G.GetHumanLegalActions().FirstOrDefault(a => a.Kind == LegalActionKind.Snatch && a.TargetSeats.SequenceEqual([target]) && a.TargetCardId is null);
            Need(action?.CardId is not null, "The fixed native fixture retains a real legal Snatch for the owned-card transfer path.");
            Accept(new PlayCardCommand(0, action!.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId)
                { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        }
        public void Answer(Func<PromptChoice, bool> match)
        { var p = P!; var choice = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && choice is not null, "Only the real current human can answer the original canonical choice."); Accept(new AnswerPromptCommand(0, p.PromptId, choice!.Id, G.Revision)); }
        public void Reach(Func<bool> ready, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 180; i++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.SkillPrompt?.SkillId == Zhangguan && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate")) { Skip(); continue; }
                    if (p.SkillPrompt?.SkillId is Watch or DrawHold) { Continue(); continue; }
                    throw new InvalidOperationException("Unexpected original Ma Miao prompt at " + line + Dump());
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Bounded Ma Miao native flow did not reach " + line + Dump());
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();
            var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) && frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))),
                "Cold preserves the real turn/gain parent, private payment, frozen random pool and native paid observer subtree.");
        }
        public void Accept(GameCommand c)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real Ma Miao command."); }
        public void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, P, Root = Root?.AnniversaryMaMiao,
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }), Gains = E<AnniversaryMaMiaoGainQualifiedEvent>(),
            Producers = E<AnniversaryMaMiaoTransferProducerEvent>(),
            Started = E<AnniversaryMaMiaoStartedEvent>(), Decisions = E<AnniversaryMaMiaoDecisionEvent>(), Random = E<AnniversaryMaMiaoRandomEvent>(),
            Movement = E<AnniversaryMaMiaoMovementEvent>(), Bindings = E<ProgramBindingResolvedEvent>().Where(e => e.SkillId == Congfeng), Adjust = E<AnniversaryMaMiaoAdjustedEvent>() });
        public static Scene Start(bool shortHand = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            IReadOnlyList<ShanheStartingCard> hand = Enumerable.Range(0, shortHand ? 1 : 7).Select(i => new ShanheStartingCard(shortHand ? CardKind.Snatch : CardKind.Dodge, Suit.Heart, i + 1)).ToArray();
            var peers = Enumerable.Range(0, 4).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Club, i + 8)).ToArray();
            var setup = new ShanheBattleSetup(33, 1, "ma-miao-native-gain", Hero, 3, 0, 0, 0, 0, [NoDraw, Driver, Watch, DrawHold, Echo], hand, [],
                [new(Peer, Hp: 8) { StartingHand = peers }, new(Peer, Hp: 8) { StartingHand = peers }], [new(Peer, Hp: 8) { StartingHand = peers }], new string('b', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-ma-miao", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"none","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"hold-originals","query":"handLimit","operation":"add","value":20,"priority":0}],"activations":[{"id":"gift-batch","minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget","amount":2}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-extra","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:congfeng.extra"],"priority":1000,"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"native-gain","options":[{"id":"continue"}]}]}]},
              {"id":"{{DrawHold}}","revision":1,"triggers":[{"id":"native-adjustment","window":"nativeDrawStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"native-draw","options":[{"id":"continue"}]}]}]},
              {"id":"{{Echo}}","revision":1,"triggers":[{"id":"different-skill-gift","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:congfeng.extra"],"usageScope":"game","usageLimit":1,"optional":false,"isSkillInvocation":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"count":1,"destination":"selectedTargetHand","targetRef":{"kind":"selectedTarget"},"awaitMovementTriggers":true}]}]}
            ]}
            """;
            var names = new Dictionary<string, object>();
            foreach (var id in new[] { NoDraw, Quiet, Driver, Echo }) names[id] = new { name = "马邈真实原生输入", description = "固定短局及真实原子给牌" };
            foreach (var id in new[] { Watch, DrawHold }) names[id] = new { name = "马邈原生暂停", description = "真实原生子树暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "固定同桌", "supporter", NoDraw, "wei", 8, AdditionalSkillIds: [Quiet]));
            const string deck = "fixture:ma-miao-deck";
            b.AddDeck(new(deck, "固定原生闪", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "马邈真实回合与随机获取", 4, 4, new Dictionary<string, int>(), deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
