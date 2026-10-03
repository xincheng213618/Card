using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

// Real-command phase substitution, payment and ending-history behavior checks.
internal static class BoundaryZhangHeChecks
{
    private const string Skill = "boundary:qiaobian";
    private const string Driver = "fixture:zh-driver";
    private const string Cost = "fixture:zh-cost";
    private const string Gain = "fixture:zh-gain";
    private const string Recovery = "fixture:zh-hp";
    private const string DrawEnded = "fixture:zh-draw-ended";
    private const string JudgeStarting = "fixture:zh-judge-starting";
    private const string Mode = "identity:classic-boundary-zhang-he-fixture";
    private static PlayerMarkerKind Marker => Enum.Parse<PlayerMarkerKind>("Bian");

    public static void JudgmentAndDrawSubstitutionPayAtTheirActualBoundaries()
    {
        AssertNewOperationContracts();
        for (var targetCount = 0; targetCount <= 2; targetCount++)
        {
            var (game, registry) = Create(CardKind.Lightning);
            ReachBinding(game, "skip-judgment");
            Require(Markers(game) == 2 && Enumerable.Range(0, 4).All(viewer =>
                game.CreateSnapshot(viewer).Players[0].Markers!.Single(m => m.Kind == Marker).Count == 2),
                "The actual game-start source grants two public change markers to all four views.");
            Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            Reach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == JudgeStarting);
            Cold(game, registry); Continue(game);
            ReachBinding(game, "skip-draw-and-take-hands");
            Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            Reach(game, p => p.PlayerSeat == 0 && p.Choices.Any(c =>
                c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == "classic:tuxi"));
            Cold(game, registry); Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            ReachPlay(game);
            Require(game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Any(e => e.OwnerSeat == 0 && e.SkillId == JudgeStarting),
                "Refusing the unpaid new substitution retains the real judgment-start benefit and the real Tuxi draw-start choice.");
            var lightning = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Lightning);
            Accept(game, new PlayCardCommand(0, lightning.CardId!.Value, lightning.TargetSeats, game.Revision, Prompt(game)!.PromptId));
            ReachPlay(game);
            var delayed = game.CreateSnapshot(0).Players[0].Judgment.Single(c => c.Kind == CardKind.Lightning).Id;
            EndPlay(game); ReachBinding(game, "skip-judgment");
            var turn = game.State.TurnNumber;
            var judgmentBoundary = game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Last();
            var judgmentProbeCount = game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Count(e => e.OwnerSeat == 0 && e.SkillId == JudgeStarting);
            Require(judgmentBoundary.Window == SkillProgramTriggerWindow.JudgmentPhaseStarting &&
                game.CreateSnapshot(0).Players[0].Judgment.Any(c => c.Id == delayed),
                "The real delayed entity remains in judgment until the actual judgment-start choice.");
            Activate(game, "skip-judgment"); ReachPayment(game); Private(game); Cold(game, registry);
            var cost = Prompt(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "alternative-phase-cost-card").Cards.Single();
            var markerBefore = Markers(game); Reject(game);
            Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "alternative-phase-cost-card" && c.Cards.SequenceEqual([cost]));
            ReachBinding(game, "skip-draw-and-take-hands");
            Require(Markers(game) == markerBefore && game.CardMovements.Count(m => m.CardId == cost &&
                m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.boundary:qiaobian.PayOwnedCardOrMarker") == 1 &&
                game.CreateSnapshot(0).Players[0].Judgment.Any(c => c.Id == delayed) &&
                game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Count(e => e.OwnerSeat == 0 && e.SkillId == JudgeStarting) == judgmentProbeCount &&
                Skips(game).Last() is { Window: SkillProgramTriggerWindow.JudgmentPhaseStarting, SkippedPhase: SkillProgramTurnPhase.Judgment },
                "A real hand payment skips the actual judgment and leaves its delayed card untouched without consuming a marker.");
            Activate(game, "skip-draw-and-take-hands"); ReachPayment(game);
            var drawBoundary = game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Last();
            Require(drawBoundary.Window == SkillProgramTriggerWindow.DrawPhaseStarting,
                "Draw substitution is offered by its own real phase-start frame after judgment returns.");
            var counts = game.State.Players.Select(p => p.HandCount).ToArray(); var gainedBefore = TakeFacts(game).Count;
            Cold(game, registry); PayMarker(game); ReachTargets(game);
            Require(Markers(game) == markerBefore - 1 && game.State.Players[0].HandCount == counts[0] &&
                Skips(game).Last() is { Window: SkillProgramTriggerWindow.DrawPhaseStarting, SkippedPhase: SkillProgramTurnPhase.Draw },
                "The marker pays once and skips normal draw before any optional blind hand-card gain.");
            var selected = Prompt(game)!.Choices.First(c => c.Targets.Count == targetCount).Targets.ToArray();
            Cold(game, registry); Answer(game, c => c.Targets.SequenceEqual(selected));
            if (targetCount > 0)
            {
                Reach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Gain);
                var owner = Qiaobian(game);
                var receipt = owner.BlindHandTake ?? throw new InvalidOperationException("The gain observer lost its frozen producer receipt.");
                Require(receipt.TargetSeats.SequenceEqual(selected) &&
                    receipt.CardIds.Count == targetCount && receipt.SourceLocations.SequenceEqual(selected.Select(CardLocation.Hand)) &&
                    game.State.Players[0].HandCount == counts[0] + targetCount &&
                    selected.All(seat => game.State.Players[seat].HandCount == counts[seat] - 1),
                    "The mixed-source batch gains exactly one frozen actual entity from each selected hand before its child observer.");
                MovementReturn(game, owner, receipt.CardIds, sourceHand: true); Private(game); Cold(game, registry); Continue(game);
            }
            ReachPlay(game);
            Require(TakeFacts(game).Count == gainedBefore + 1 && TakeFacts(game).Last().CardCount == targetCount &&
                TakeFacts(game).Last().TargetSeats.SequenceEqual(selected) && game.State.Players[0].HandCount == counts[0] + targetCount &&
                !game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Any(e => e.OwnerSeat == 0 &&
                    e.SkillId == DrawEnded && e.FrameId > drawBoundary.Id) && game.State.TurnNumber == turn,
                "Zero, one or two targets complete one paid skip; a skipped draw does not run the true draw-ended probe.");
            var canceledDrawChoices = game.Events.Select(e => e.Payload).OfType<ProgramBindingResolvedEvent>().Where(e =>
                e.FrameId == drawBoundary.Id && e.OwnerSeat == 0 && e.SkillId == "classic:tuxi").ToArray();
            Require(canceledDrawChoices.Length == 1 && !canceledDrawChoices[0].Activated && !canceledDrawChoices[0].Completed &&
                !game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Any(e => e.OwnerSeat == 0 && e.SkillId == "classic:tuxi"),
                "The exact confirmed paid draw-start parent cancels its remaining real Tuxi candidate, without taking extra cards or replacing an unskipped window.");
            Require(game.Events.Select(e => e.Payload).OfType<ProgramAlternativePhaseCostPaidEvent>().Count(e =>
                e.OwnerSeat == 0 && e.BindingId == "skip-draw-and-take-hands") == 1,
                "The child-return chain cannot remove another marker or repay the earlier card.");
            Cold(game, registry);
        }
    }

    public static void EquippedCardCostDrainsHpAndMovementBeforeSkippingDraw()
    {
        var (game, registry) = Create(CardKind.SilverLion, observers: true); ReachPlay(game);
        var equip = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
        Accept(game, new PlayCardCommand(0, equip.CardId!.Value, equip.TargetSeats, game.Revision, Prompt(game)!.PromptId)); ReachPlay(game);
        var armor = game.CreateSnapshot(0).Players[0].Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
        Require(game.State.Players[0].Hp == game.State.Players[0].MaxHp - 1,
            "The actual Lord bonus gives this initial 3/4 fixture a wounded 4/5 equipped owner.");
        EndPlay(game); ReachBinding(game, "skip-draw-and-take-hands"); Activate(game, "skip-draw-and-take-hands"); ReachPayment(game);
        var markers = Markers(game); var hand = game.State.Players[0].HandCount; var skipCount = Skips(game).Count;
        Private(game); Cold(game, registry); Reject(game);
        Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "alternative-phase-cost-card" && c.Cards.SequenceEqual([armor]));
        Reach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Recovery);
        var paid = Qiaobian(game);
        Require(paid.AlternativePhaseCost is { Kind: ProgramAlternativePhaseCostKind.DiscardCard, CardId: var id, SourceLocation: var from } &&
            id == armor && from == CardLocation.Equipment(0) && game.State.Players[0].Hp == game.State.Players[0].MaxHp &&
            game.State.Players[0].HandCount == hand && Markers(game) == markers && Skips(game).Count == skipCount &&
            game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h => h.ResumeFrameId == paid.Id && h.Continuation == PostEventContinuation.Program),
            "The paid equipment receipt owns the actual HP observer while skip, draw and optional gain remain untouched.");
        Cold(game, registry); Continue(game); Reach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Cost);
        paid = Qiaobian(game); MovementReturn(game, paid, [armor], sourceHand: false);
        Require(Skips(game).Count == skipCount && game.State.Players[0].HandCount == hand,
            "The completed HP child returns to its producer, and cost movement still precedes the skip.");
        Cold(game, registry); Continue(game); ReachTargets(game); Answer(game, c => c.Targets.Count == 0); ReachPlay(game);
        Require(Skips(game).Count == skipCount + 1 && Markers(game) == markers && game.State.Players[0].HandCount == hand &&
            game.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile) == 1 &&
            game.Events.Select(e => e.Payload).OfType<SilverLionRemovedRecoveryEvent>().Count(e => e.PlayerSeat == 0 && e.RecoveredAmount == 1) == 1,
            "Cold continuation performs one real loss, one recovery and one draw skip without repaying or awarding an extra normal draw.");
        Cold(game, registry);
    }

    public static void FieldMoveAndDiscardSkipRetainActualPaidStageHistory()
    {
        var (game, registry) = Create(CardKind.SilverLion, observers: true); ReachPlay(game);
        var equip = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
        Accept(game, new PlayCardCommand(0, equip.CardId!.Value, equip.TargetSeats, game.Revision, Prompt(game)!.PromptId)); ReachPlay(game);
        var armor = game.CreateSnapshot(0).Players[0].Equipment.Single().Id;
        EndPlay(game); ReachBinding(game, "skip-play-and-move-field-card");
        Activate(game, "skip-play-and-move-field-card"); ReachPayment(game); var markers = Markers(game); PayMarker(game); ReachTargets(game);
        Require(game.CreateSnapshot(0).Players[1].Equipment.Count == 0,
            "The quiet fixture prohibits actual AI equipment use, leaving a real empty destination.");
        Answer(game, c => c.Targets.SequenceEqual([0, 1]));
        Reach(game, p => p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"));
        Cold(game, registry); Answer(game, c => c.Cards.SequenceEqual([armor]));
        Reach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Recovery);
        var paidMove = Qiaobian(game); var play = Skips(game).Last();
        Require(play.SkippedPhase == SkillProgramTurnPhase.Play && play.Window == SkillProgramTriggerWindow.AfterNormalDraw &&
            play.ActualTurnNumber == game.State.TurnNumber && play.SkillInstanceId == paidMove.SkillInstanceId &&
            Markers(game) == markers - 1 && game.CreateSnapshot(0).Players[1].Equipment.Any(c => c.Id == armor) &&
            !game.CreateSnapshot(0).Players[0].Equipment.Any(c => c.Id == armor) &&
            !game.Events.Select(e => e.Payload).OfType<PhaseChangedEvent>().Any(e => e.ActorSeat == 0 && e.Phase == TurnPhase.Discard &&
                game.Events.First(item => ReferenceEquals(item.Payload, e)).Sequence > game.Events.Last(item => ReferenceEquals(item.Payload, play)).Sequence),
            "The public skip fact retains actual turn/window/source; real field movement and recovery pause before Discard.");
        Cold(game, registry); Continue(game); Reach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Cost);
        MovementReturn(game, Qiaobian(game), [armor], sourceHand: false, to: CardLocation.Equipment(1));
        Cold(game, registry); Continue(game); ReachBinding(game, "skip-discard"); Activate(game, "skip-discard"); ReachPayment(game);
        var hand = game.State.Players[0].HandCount;
        var card = Prompt(game)!.Choices.First(c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).Cards.Single();
        Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "alternative-phase-cost-card" && c.Cards.SequenceEqual([card]));
        Reach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Cost); Cold(game, registry); var before = HistoryFacts(game).Count; Continue(game);
        ReachUntil(game, () => HistoryFacts(game).Count > before);
        var recorded = HistoryFacts(game).Last();
        Require(recorded.HandCount == hand - 1 && recorded.SkillInstanceId == play.SkillInstanceId &&
            Skips(game).Last().SkippedPhase == SkillProgramTurnPhase.Discard &&
            !game.CardMovements.Any(m => m.From == CardLocation.Hand(0) && m.Reason == CardMoveReasons.HandLimitDiscard &&
                m.Sequence > game.CardMovements.Single(m => m.CardId == card && m.To == CardLocation.DiscardPile).Sequence),
            "A real discard-start cost skips the excess-hand discard and records the resulting actual ending count under the same source.");
        Require(game.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Equipment(1)) == 1,
            "The field-card observer return never repeats the actual equipment transfer."); Cold(game, registry);
    }

    public static void EndingHandCountHistoryComparesBeforeInsertAcrossExtraTurnsAndNativeAi()
    {
        var (game, registry) = Create(CardKind.Slash); ReachPlay(game); var initialMarkers = Markers(game);
        EndPlay(game); ReachUntil(game, () => HistoryFacts(game).Count == 1);
        var first = HistoryFacts(game).Single();
        Require(first.IsNew && Markers(game) == initialMarkers + 1 && first.HandCount == 5,
            "The first actual ending compares against an empty source history and grants exactly one marker."); Cold(game, registry);
        ReachPlay(game); EndPlay(game); ReachUntil(game, () => HistoryFacts(game).Count == 2);
        var second = HistoryFacts(game).Last();
        Require(!second.IsNew && second.HandCount == first.HandCount && second.SkillInstanceId == first.SkillInstanceId &&
            Markers(game) == initialMarkers + 1, "A repeated actual ending count is inserted without granting another marker."); Cold(game, registry);
        ReachPlay(game);
        var discarded = game.CreateSnapshot(0).Players[0].Hand.Take(3).Select(c => c.Id).ToArray();
        Use(game, "discard-three", discarded); ReachPlay(game); Use(game, "extra-turn"); ReachPlay(game); EndPlay(game);
        ReachUntil(game, () => HistoryFacts(game).Count == 3);
        var third = HistoryFacts(game).Last();
        Require(third.IsNew && third.HandCount == 4 && Markers(game) == initialMarkers + 2,
            "Real command-paid hand losses create a different ending population and a second distinct marker grant."); Cold(game, registry);
        ReachPlay(game); Require(game.State.TurnNumber == third.ActualTurnNumber + 1,
            "The formally queued extra turn runs immediately and has a distinct actual-turn identity.");
        EndPlay(game); ReachUntil(game, () => HistoryFacts(game).Count == 4);
        var fourth = HistoryFacts(game).Last(); var history = game.GetProgramEndingHandCountHistoryDiagnostics().Single(h => h.OwnerSeat == 0 && h.SkillId == Skill);
        Require(!fourth.IsNew && fourth.HandCount == 5 && fourth.SkillInstanceId == first.SkillInstanceId &&
            history.Counts.SequenceEqual([4, 5]) && history.LastActualTurnNumber == fourth.ActualTurnNumber &&
            Markers(game) == initialMarkers + 2,
            "Extra-turn endings share the exact source set, compare before inserting, and do not confuse Round with ActualTurn.");
        if (history.Counts is IList<int> list) { var blocked = false; try { list[0] = 99; } catch (NotSupportedException) { blocked = true; } Require(blocked, "Trusted diagnostic history collections are frozen."); }
        Cold(game, registry);
        var records = HistoryFacts(game).Count; ReachPlay(game); Use(game, "down"); ReachPlay(game); EndPlay(game);
        ReachUntil(game, () => HistoryFacts(game).Count == records + 1); var beforeSkippedTurn = HistoryFacts(game).Count;
        var turnEndedBefore = game.Events.Select(e => e.Payload).OfType<TurnEndedEvent>().Count(e => e.ActorSeat == 0);
        ReachUntil(game, () => game.Events.Select(e => e.Payload).OfType<TurnEndedEvent>().Count(e => e.ActorSeat == 0) > turnEndedBefore);
        Require(HistoryFacts(game).Count == beforeSkippedTurn, "An actual face-down skipped whole turn never enters the ending-count observer."); Cold(game, registry);
        var (native, nativeRegistry) = Create(CardKind.Slash, native: true); ReachPlay(native); EndPlay(native);
        ReachUntil(native, () => native.Events.Select(e => e.Payload).OfType<ProgramEndingHandCountRecordedEvent>().Any(e => e.OwnerSeat == 1 && e.SkillId == Skill));
        var nativeCost = native.Events.Select(e => e.Payload).OfType<ProgramAlternativePhaseCostPaidEvent>().FirstOrDefault(e => e.OwnerSeat == 1);
        Require(nativeCost is { Kind: ProgramAlternativePhaseCostKind.RemoveMarker } &&
            native.Events.Select(e => e.Payload).OfType<ProgramTurnPhaseSubstitutedEvent>().Any(e => e.OwnerSeat == 1 && e.SkillInstanceId == nativeCost.SkillInstanceId),
            "Native AI pays a real public token cost and retains its own source identity without a scripted AnswerPrompt for the AI."); Cold(native, nativeRegistry);
    }

    private static IReadOnlyList<ProgramTurnPhaseSubstitutedEvent> Skips(GameEngine g) => g.Events.Select(e => e.Payload).OfType<ProgramTurnPhaseSubstitutedEvent>().Where(e => e.OwnerSeat == 0).ToArray();
    private static void AssertNewOperationContracts()
    {
        void Load(string window, string phase, bool history = false, bool paymentFirst = true)
        {
            var payment = new { op = "payOwnedCardOrMarker", target = "owner", zones = new[] { "hand", "equipment" }, marker = "bian" };
            var skip = new { op = "skipTurnPhases", target = "owner", phases = new[] { phase } };
            object[] effects = history ? [new { op = "recordEndHandCountAndGrantMarker", target = "owner", stateId = "end-counts", marker = "bian", amount = 1 }]
                : paymentFirst ? [payment, skip] : [skip, payment];
            SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion,
                skills = new[] { new { id = "fixture:zh-contract", revision = 1, triggers = new[] { new { id = "boundary", window, subject = "owner", optional = !history, effects } } } } }),
                JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object> { ["fixture:zh-contract"] = new { name = "阶段合同", description = "精确通用能力入口" } } }));
        }
        Load("judgmentPhaseStarting", "judgment"); Load("drawPhaseStarting", "draw"); Load("turnEnding", "discard", history: true);
        foreach (var item in new[] { ("judgmentPhaseStarting", "draw", false, true), ("drawPhaseStarting", "judgment", false, true),
            ("playEnding", "play", false, true), ("afterDamageApplied", "draw", false, true),
            ("afterNormalDraw", "play", false, false), ("drawPhaseStarting", "draw", true, true) })
        {
            var rejected = false; try { Load(item.Item1, item.Item2, item.Item3, item.Item4); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Alternative payment and history cannot escape their exact phase/window/order contracts.");
        }
    }
    private static IReadOnlyList<ProgramRandomHandCardsTakenEvent> TakeFacts(GameEngine g) => g.Events.Select(e => e.Payload).OfType<ProgramRandomHandCardsTakenEvent>().Where(e => e.OwnerSeat == 0 && e.SkillId == Skill).ToArray();
    private static IReadOnlyList<ProgramEndingHandCountRecordedEvent> HistoryFacts(GameEngine g) => g.Events.Select(e => e.Payload).OfType<ProgramEndingHandCountRecordedEvent>().Where(e => e.OwnerSeat == 0 && e.SkillId == Skill).ToArray();
    private static int Markers(GameEngine g) => g.State.Players[0].Markers?.SingleOrDefault(m => m.Kind == Marker)?.Count ?? 0;
    private static ProgramSkillFrame Qiaobian(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.OwnerSeat == 0 && f.SkillId == Skill);
    private static void MovementReturn(GameEngine g, ProgramSkillFrame owner, IReadOnlyList<int> ids, bool sourceHand, CardLocation? to = null)
    {
        var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(m =>
            m.Batch.ParentFrameId == owner.Id && (owner.PendingMovementContinuation is not null
                ? m.Batch.AwaitingProgramFrameId == owner.Id && m.ResumeProgramFrameId is null
                : m.Batch.AwaitingProgramFrameId is null && m.ResumeProgramFrameId == owner.Id));
        Require(movement.Batch.ParentFrameId == owner.Id && movement.Batch.OriginSkillId == Skill && movement.Batch.OriginSkillInstanceId == owner.SkillInstanceId &&
            movement.Batch.Movements.Select(m => m.CardId).Order().SequenceEqual(ids.Order()) &&
            movement.Batch.Movements.All(m => m.From.Zone == (sourceHand ? CardZoneKind.Hand : CardZoneKind.Equipment) &&
                m.To == (to ?? (owner.BlindHandTake is not null ? CardLocation.Hand(0) : CardLocation.DiscardPile))),
            "The actual movement batch and typed child return retain exactly the original producer and physical materials.");
    }
    private static void Private(GameEngine g)
    {
        Require(Prompt(g) is { PlayerSeat: 0, IsPrivate: true }, "The owning chooser receives the private program prompt.");
        for (var viewer = 1; viewer < 4; viewer++) Require(g.CreateSnapshot(viewer).PendingDecision is null && g.CreateSnapshot(viewer).Players[0].Hand.Count == 0,
            "Another viewer receives neither choices nor the owner's private hand identities.");
    }
    private static (GameEngine, ContentRegistry) Create(CardKind deckKind, bool observers = false, bool native = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(deckKind, observers, native));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:zh-owner", game.Revision, Prompt(game)!.PromptId)); return (game, registry);
    }
    private static PendingDecision? Prompt(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Binding(PendingDecision p, string id) => p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == Skill && c.Parameters.GetValueOrDefault("binding-id") == id);
    private static void ReachBinding(GameEngine g, string id) => Reach(g, p => Binding(p, id));
    private static void ReachPlay(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void ReachPayment(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action")?.StartsWith("alternative-phase-cost-", StringComparison.Ordinal) == true));
    private static void ReachTargets(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-targets"));
    private static void Activate(GameEngine g, string id) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("binding-id") == id);
    private static void PayMarker(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "alternative-phase-cost-marker");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void EndPlay(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, Prompt(g)!.PromptId));
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? cards = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], [], g.Revision, Prompt(g)!.PromptId));
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) => ReachUntil(g, () => Prompt(g) is { } p && predicate(p));
    private static void ReachUntil(GameEngine g, Func<bool> predicate)
    {
        for (var step = 0; step < 240; step++) { if (predicate()) return; Advance(g); }
        throw new InvalidOperationException("Fixed staged Zhang He fixture missed its real boundary: " + JsonSerializer.Serialize(Prompt(g)));
    }
    private static void Advance(GameEngine g)
    {
        var p = Prompt(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId is Cost or Gain or Recovery or DrawEnded or JudgeStarting) Continue(g);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = Prompt(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static void Reject(GameEngine g)
    { var before = State(g); var p = Prompt(g)!; Require(!g.Submit(new AnswerPromptCommand(0, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before, "A stale/unpublished alternative cost cannot alter the command prefix, RNG, private cards or payment."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics(), History = g.GetProgramEndingHandCountHistoryDiagnostics() });
    private static void Cold(GameEngine g, ContentRegistry registry) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry)),
        "The real command journal cold-restores all four private views, exact typed producer receipts, history, movement and public phase facts.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(CardKind kind, bool observers, bool native) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-zhang-he", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"discard-three","minCards":3,"maxCards":3,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":3}]},
                {"id":"extra-turn","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
                {"id":"down","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setFaceState","target":"owner","faceDown":true}]}]},
                {"id":"fixture:zh-quiet","revision":1,"triggers":[{"id":"quiet","window":"turnStartBeforeNormalFlow","subject":"owner","priority":50,"optional":false,"effects":[{"op":"grantTurnCardActionProhibition","target":"owner","cardKinds":["slash","silverLion","lightning"],"actionTypes":["use"]}]}]},
                {"id":"{{JudgeStarting}}","revision":1,"triggers":[{"id":"actual-judge-start","window":"judgmentPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                {"id":"{{Cost}}","revision":1,"triggers":[{"id":"cost","window":"cardsMoved","subject":"owner","optional":false,"sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["skill-program.boundary:qiaobian.PayOwnedCardOrMarker","skill-program.boundary:qiaobian.SelectAndMoveOwnedCard"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                {"id":"{{Gain}}","revision":1,"triggers":[{"id":"gained","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:qiaobian.TakeRandomHandCardFromSelectedTargets"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                {"id":"{{Recovery}}","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                {"id":"{{DrawEnded}}","revision":1,"triggers":[{"id":"actual-draw-end","window":"drawPhaseEnded","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实历史准备", description = "命令支付三牌、额外回合及正式翻面" },
                    ["fixture:zh-quiet"] = new { name = "安静回合", description = "禁止固定牌堆的实际用牌" },
                    [Cost] = Label("费用移动观察"), [Gain] = Label("真实获牌观察"), [Recovery] = Label("银狮实际回复"), [DrawEnded] = Label("实际摸牌结束"), [JudgeStarting] = Label("实际判定开始") } }));
            foreach (var id in catalog.Programs.Keys) builder.AddSkill(new(id, id, "正式程序夹具") { Program = catalog.Programs[id] });
            builder.AddSkill(new("fixture:zh-selection", "固定选将", "无运行程序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddGeneral(new("fixture:zh-owner", "巧变真实程序", "supporter", Skill, "wei", 4,
                observers ? [Driver, Cost, Gain, Recovery, DrawEnded, JudgeStarting, "classic:tuxi"] : [Driver, Gain, DrawEnded, JudgeStarting, "classic:tuxi"]) { InitialHp = kind == CardKind.SilverLion ? 3 : 4 });
            for (var i = 1; i < 4; i++) builder.AddGeneral(new($"fixture:zh-target-{i}", "固定目标", "supporter", "fixture:zh-selection", "shu", 6,
                native ? ["fixture:zh-quiet", Skill] : ["fixture:zh-quiet"]) { InitialHp = 6 });
            var card = kind switch { CardKind.SilverLion => "classic:silver-lion", CardKind.Lightning => "standard:lightning", _ => "standard:slash" };
            builder.AddDeck(new("fixture:zh-deck", "固定真实实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 120).Select(i => new ContentDeckPhysicalCard(card, Suit.Heart, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "真实巧变", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:zh-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:zh-owner", "fixture:zh-target-1", "fixture:zh-target-2", "fixture:zh-target-3"]));
        }
        private static object Label(string name) => new { name, description = "真实规则子窗口暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
