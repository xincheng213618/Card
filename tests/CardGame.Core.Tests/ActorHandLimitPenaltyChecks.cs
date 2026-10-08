using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ActorHandLimitPenaltyChecks
{
    private const string Mode = "identity:classic-actor-hand-penalty";
    private const string Owner = "fixture:actor-penalty-owner";
    private const string Driver = "fixture:actor-penalty-driver";
    private const string Penalty = "fixture:actor-penalty";
    private const string Complete = "fixture:actor-penalty-complete";
    private const string Gain = "fixture:actor-penalty-gain";
    private const string Ended = "fixture:actor-penalty-ended";
    private const string Add = "fixture:actor-penalty-add";
    private const string Redirect = "fixture:actor-penalty-redirect";
    private const string Initial = "fixture:actor-penalty-initial";
    private const string PrintedLoss = "fixture:actor-penalty-printed-loss";
    private const string Reacquire = "fixture:actor-penalty-reacquire";
    private const string Late = "fixture:actor-penalty-late";
    private const string DrawReason = "skill-program." + Penalty + ".Draw";
    private enum Scenario { Stack, Color, Foreign, Tail, Borrowed, SourceLoss, SourceSwitch, Late }

    public static void RealUsesStackAndChangeActualDiscard()
    {
        ActorHandLimitPenaltyContractChecks.RejectsMalformedContracts();
        var (g, registry) = Start(Scenario.Stack, CardKind.Slash, Suit.Heart);
        var printedInstance = V(g, 1).SkillRuntimeStates!.Single(s => s.SkillId == Penalty).BooleanStates!
            .Single(s => s.StateId == "source-identity").SkillInstanceId;
        Require(!E<ProgramTurnSkillsGrantedEvent>(g).Any(), "The target initially has its real printed source; a second source has not been fabricated at setup.");
        var initial = V(g, 0).HandCount;
        ProgramTurnSkillsGrantedEvent? secondary = null;
        var paid = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            var a = Action(g, LegalActionKind.Slash, [1]); paid.Add(a.CardId!.Value);
            Play(g, a); Reach(g, IsComplete);
            var use = UseFor(g, a.CardId.Value);
            secondary ??= E<ProgramTurnSkillsGrantedEvent>(g).Single();
            Require(E<ProgramTurnSkillsGrantedEvent>(g) is [var turnGrant] && SameTurnGrant(turnGrant, secondary) &&
                turnGrant.SkillId == Initial && turnGrant.BindingId == "second-source" && turnGrant.OwnerSeat == 1 &&
                turnGrant.RecipientSeat == 1 && turnGrant.GrantedSkillIds.SequenceEqual([Penalty]) &&
                !string.IsNullOrWhiteSpace(turnGrant.GrantSourceId) && turnGrant.TurnExpiry is { } expiry &&
                expiry.TurnNumber == g.State.TurnNumber && expiry.TurnOwnerSeat == 0 &&
                V(g, 1).SkillRuntimeStates!.Single(s => s.SkillId == Penalty).BooleanStates! is [var identity] &&
                identity.StateId == "source-identity" && identity.SkillInstanceId == printedInstance,
                "The first real finalized target window issues an independent temporary source to the off-turn recipient, sharing its original effective printed instance throughout all three uses: " +
                JsonSerializer.Serialize(new { Iteration = i, Secondary = secondary, Actual = E<ProgramTurnSkillsGrantedEvent>(g),
                    PrintedInstance = printedInstance, PublicStates = V(g, 1).SkillRuntimeStates!.Single(s => s.SkillId == Penalty).BooleanStates }));
            Frozen(secondary.GrantedSkillIds);
            var grants = Grants(g).Where(p => p.CardUseFrameId == use.Id).ToArray();
            Require(grants is [var one] && one.ActorSeat == 0 && one.TargetSeat == 1 && one.Amount == 1 &&
                one.ActionId == use.Action!.ActionId && one.Source.SkillId == Penalty &&
                one.Source.BindingId == "slash" && one.Source.OwnerSeat == 1 && one.EffectIndex == 0 &&
                one.GameplayHash == registry.GetSkill(Penalty).Program!.GameplayHash &&
                E<CardActionAcceptedEvent>(g).Count(e => e.Action.ActionId == one.ActionId && e.Action.Type == CardActionType.Use) == 1,
                "Every real red Slash issues exactly one positive scalar penalty against its actual actor, despite two live sources of the same target skill.");
            Require(use.ActorHandLimitAnnouncedTargets!.SequenceEqual([1]), "The actual primary target is frozen on its owning Use.");
            Frozen(use.ActorHandLimitAnnouncedTargets!); Private(g); g = Cold(g, registry);
            Continue(g); Reach(g, IsPlay); AssertPhysicalUse(g, a.CardId.Value, CardKind.Slash, use.Id);
        }
        Require(Grants(g).Length == 3 && Grants(g).Select(p => p.ActionId).Distinct().Count() == 3 &&
            Grants(g).Select(p => p.GrantSequence).Distinct().Count() == 3 && V(g, 0).HandCount == initial - 3 &&
            !Expired(g).Any(), "Three distinct paid uses stack three contributions without paying extra material or expiring early.");
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, p => p is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 });
        var discard = P(g)!;
        Require(V(g, 0).Hp == 3 && discard.RequiredCardCount == V(g, 0).HandCount && discard.RequiredCardCount > 0,
            "Three penalties reduce the real three-HP actor's hand limit to zero: the native discard command requires every remaining hand entity.");
        g = Cold(g, registry); discard = P(g)!;
        var cards = discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray();
        Accept(g, new DiscardCardsCommand(0, cards, discard.PromptId, g.Revision));
        Reach(g, p => IsEnded(p) && E<TurnEndedEvent>(g).LastOrDefault()?.ActorSeat == 0);
        Require(V(g, 0).HandCount == 0 && cards.All(id => g.CardMovements.Count(m => m.CardId == id &&
                m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.HandLimitDiscard) == 1) &&
            Expired(g).Length == 3 && Expired(g).All(e => e.EndedActorSeat == 0 && e.Reason == "actor-turn-ended"),
            "Actual hand-limit payment and the actor's exact TurnEnded each settle once, before the native after-ended observer.");
        Require(E<ProgramTurnSkillGrantExpiredEvent>(g).Count(e => e.RecipientSeat == 1 && e.SkillId == Penalty &&
                e.SourceId == secondary!.GrantSourceId && e.SkillInstanceId == printedInstance && e.TurnExpiry == secondary.TurnExpiry) == 1 &&
            V(g, 1).Skills!.Any(s => s.Id == Penalty) && V(g, 1).SkillRuntimeStates!.Single(s => s.SkillId == Penalty)
                .BooleanStates! is [var permanent] && permanent.SkillInstanceId == printedInstance,
            "Exactly the independent temporary source expires at its frozen actual turn end; the original printed source and its shared effective identity survive.");
        Private(g); _ = Cold(g, registry);
    }

    public static void BlackTrickAndForeignActorOwnTurnExpiry()
    {
        foreach (var (kind, suit, expected) in new[] { (CardKind.IronChain, Suit.Spade, 1),
            (CardKind.IronChain, Suit.Heart, 0), (CardKind.Indulgence, Suit.Spade, 0) })
        {
            var (g, registry) = Start(Scenario.Color, kind, suit);
            var a = Action(g, kind == CardKind.IronChain ? LegalActionKind.IronChain : LegalActionKind.Indulgence, [1]);
            Play(g, a); Reach(g, IsComplete); var use = UseFor(g, a.CardId!.Value);
            Require(Grants(g).Length == expected && Grants(g).All(p => p.CardUseFrameId == use.Id && p.ActorSeat == 0 && p.TargetSeat == 1),
                "Only an actually black ordinary trick qualifies; red ordinary tricks and black delayed tricks do not.");
            Private(g); g = Cold(g, registry); Continue(g); Reach(g, IsPlay);
            Require(E<CardUseDeclaredEvent>(g).Count(e => e.ResolutionId == use.Id && e.CardId == a.CardId && e.CardKind == kind) == 1 &&
                g.CardMovements.Count(m => m.CardId == a.CardId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1,
                "Each positive or negative color branch executes one real physical native use.");
        }
        var (foreign, r) = Start(Scenario.Foreign, CardKind.Slash, Suit.Heart);
        Activate(foreign, "request", [1]);
        Reach(foreign, IsComplete);
        var penalty = Grants(foreign).Single();
        var original = foreign.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == penalty.CardUseFrameId);
        Require(penalty.ActorSeat == 1 && penalty.TargetSeat == 0 && original.Action is { ActorSeat: 1, ProviderSeat: 1 } &&
            original.Action.PhysicalCards is [var cost] && cost.From == CardLocation.Hand(1) &&
            foreign.State.CurrentSeat == 0 && penalty.IssuedActualTurnNumber == foreign.State.TurnNumber,
            "A native AI really accepts a requested physical Slash outside its own turn; the contribution belongs to actor one, not the requester or current turn owner.");
        foreign = Cold(foreign, r); Reach(foreign, IsPlay);
        var before = Grants(foreign).Length;
        Activate(foreign, "foreign-duel", [1]);
        Reach(foreign, p => p is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 });
        var response = P(foreign)!; var choice = response.Choices.First(c => c.Cards.Count == 1);
        var responseCard = choice.Cards.Single();
        foreign = Cold(foreign, r); Answer(foreign, c => c.Cards.SequenceEqual([responseCard]));
        Reach(foreign, IsPlay);
        Require(E<CardActionAcceptedEvent>(foreign).Any(e => e.Action.Type == CardActionType.Response &&
                e.Action.ActorSeat == 0 && e.Action.PhysicalCards.Any(c => c.CardId == responseCard)) &&
            Grants(foreign).Length == before,
            "A true Duel Slash response pays its physical entity but cannot create a designated-Use penalty; the zero-material colorless Duel cannot masquerade as a black trick.");
        Accept(foreign, new EndPlayPhaseCommand(0, foreign.Revision, P(foreign)!.PromptId));
        Reach(foreign, p => IsEnded(p) && E<TurnEndedEvent>(foreign).LastOrDefault()?.ActorSeat == 0);
        Require(!Expired(foreign).Any(e => e.Penalty == penalty), "Ending the other actual turn does not expire the actor's off-turn contribution.");
        foreign = Cold(foreign, r); Continue(foreign);
        Reach(foreign, p => IsEnded(p) && E<TurnEndedEvent>(foreign).LastOrDefault()?.ActorSeat == 1);
        var ended = E<TurnEndedEvent>(foreign).Last();
        Require(Expired(foreign).Count(e => e.Penalty == penalty && e.EndedActorSeat == 1 &&
                e.EndedTurnNumber == ended.TurnNumber && e.Reason == "actor-turn-ended") == 1,
            "The real actor's next actual TurnEnded, after its native preparation/draw/skipped Play, expires the old contribution once.");
        _ = Cold(foreign, r);
    }

    public static void AddedRedirectedAndBorrowedSwordTargets()
    {
        var (g, registry) = Start(Scenario.Tail, CardKind.Slash, Suit.Spade);
        var tailSeat = g.CreateSnapshot(0).Players.Single(p => p.GeneralId == "fixture:actor-penalty-peer-2").Seat;
        var newSeat = new[] { 2, 3 }.Single(s => s != tailSeat);
        Require(tailSeat == 3 && newSeat == 2, "Ordinary selection weights put the real tail redirector at seat three: original seat one is outside its range, so seat two is its sole native redirect candidate.");
        var a = Action(g, LegalActionKind.Slash, [1]); Play(g, a);
        Reach(g, p => p?.SkillPrompt?.SkillId == Add && p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([tailSeat]));
        Reach(g, IsComplete); var use = UseFor(g, a.CardId!.Value);
        var facts = Grants(g).Where(p => p.CardUseFrameId == use.Id).ToArray();
        var redirectedPayments = g.CardMovements.Where(m => m.From == CardLocation.Hand(tailSeat) && m.To == CardLocation.DiscardPile &&
            m.Reason.Value == $"skill-program.{Redirect}.SelectAndMoveOwnedCard").ToArray();
        Require(E<ProgramCardTriggerResolvedEvent>(g).Count(e => e.SkillId == Redirect && e.OwnerSeat == tailSeat && e.Activated) == 1 &&
            use.TargetSeats.SequenceEqual([1, newSeat]) && use.ActorHandLimitAnnouncedTargets!.SequenceEqual([1, tailSeat, newSeat]) &&
            facts.Select(p => p.TargetSeat).Order().SequenceEqual([1, 2, 3]) && facts.All(p => p.ActionId == use.Action!.ActionId && p.ActorSeat == 0) &&
            redirectedPayments.Length == 1 && g.CardMovements.Count(m => m.CardId == redirectedPayments.Single().CardId && m.From == CardLocation.Hand(tailSeat) && m.To == CardLocation.DiscardPile) == 1,
            "The original target and appended target are announced once before effects; an accepted Slash tail's real paid Liuli redirect announces the new identity without retracting the old contribution.");
        Frozen(use.ActorHandLimitAnnouncedTargets!); Private(g); g = Cold(g, registry); Continue(g); Reach(g, IsPlay);
        AssertPhysicalUse(g, a.CardId.Value, CardKind.Slash, use.Id);
        Require(Grants(g).Count(p => p.ActionId == use.Action!.ActionId) == 3,
            "Cold return of both actual Slash targets cannot duplicate any old or newly redirected target identity.");

        var (b, br) = Start(Scenario.Borrowed, CardKind.BorrowedSword, Suit.Spade);
        foreach (var seat in new[] { 1, 3 }) { Activate(b, "equip", [seat]); Reach(b, IsPlay); }
        var weapons = new[] { 1, 3 }.ToDictionary(s => s, s => V(b, s).Equipment.Single(c => c.Kind == CardKind.Crossbow).Id);
        var borrow = Action(b, LegalActionKind.BorrowedSword, [1, 2]); Play(b, borrow);
        Reach(b, p => p?.SkillPrompt?.SkillId == Add && p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        b = Cold(b, br); Answer(b, c => c.Targets.SequenceEqual([3]));
        Reach(b, p => p?.SkillPrompt?.SkillId == Add && p.PlayerSeat == 0 && p.Choices.Any(c => c.Targets.SequenceEqual([2])));
        b = Cold(b, br); Answer(b, c => c.Targets.SequenceEqual([2]));
        Reach(b, IsComplete); var bu = UseFor(b, borrow.CardId!.Value);
        Require(bu.TargetSeats.SequenceEqual([1, 2, 3, 2]) && bu.ActorHandLimitAnnouncedTargets!.SequenceEqual([1, 3]) &&
            Grants(b).Where(p => p.CardUseFrameId == bu.Id).Select(p => p.TargetSeat).Order().SequenceEqual([1, 3]) &&
            !Grants(b).Any(p => p.TargetSeat == 2) && weapons.All(pair =>
                b.CardMovements.Count(m => m.CardId == pair.Value && m.From == CardLocation.Equipment(pair.Key) &&
                    m.To == CardLocation.Processing && m.Reason == CardMoveReasons.BorrowedSwordGive) == 1 &&
                b.CardMovements.Count(m => m.CardId == pair.Value && m.From == CardLocation.Processing &&
                    m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.BorrowedSwordGive) == 1),
            "Borrowed Sword uses two real ordered pairs and two real weapon payments, while only weapon-holder primary targets issue penalties; repeated victims never count.");
        Frozen(bu.ActorHandLimitAnnouncedTargets!); b = Cold(b, br); Continue(b); Reach(b, IsPlay);
        AssertPhysicalUse(b, borrow.CardId.Value, CardKind.BorrowedSword, bu.Id); _ = Cold(b, br);
    }

    public static void PaidChildrenSourceLossPrivacyAndColdReplay()
    {
        foreach (var scenario in new[] { Scenario.SourceLoss, Scenario.SourceSwitch })
        {
            var (g, registry) = Start(scenario, CardKind.IronChain, Suit.Spade);
            string? first = null, second = null;
            GameSnapshot? retained = null;
            g.EventCommitted += e => { if (e.Payload is ProgramActorHandLimitPenaltyGrantedEvent p) first = JsonSerializer.Serialize(p); };
            g.EventCommitted += e => { if (e.Payload is ProgramActorHandLimitPenaltyGrantedEvent p) second = JsonSerializer.Serialize(p); };
            g.StateChanged += s => { retained ??= s; Frozen(s.Players); };
            var a = Action(g, LegalActionKind.IronChain, [1]); Play(g, a);
            Reach(g, p => p?.SkillPrompt?.SkillId == Gain);
            var penalty = Grants(g).Single();
            var parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == penalty.ProgramFrameId);
            var child = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == Gain);
            var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Id == child.WindowContext!.ParentFrameId);
            Require(parent.InstructionIndex == 2 && movement.Batch.ParentFrameId == parent.Id &&
                movement.ResumeProgramFrameId == parent.Id && movement.Batch.Movements is [var draw] &&
                draw.From == CardLocation.DrawPile && draw.To == CardLocation.Hand(1) && draw.Reason.Value == DrawReason &&
                parent.SkillInstanceId == penalty.Source.SkillInstanceId && first == second && retained is not null && g.ObserverFailures.Count == 0,
                "An issued scalar penalty owns one real paid native Draw child and retains its exact source, parent cursor and prepared observer deliveries.");
            var retainedJson = SnapshotJson.Serialize(retained!);
            Frozen(movement.Batch.Movements); Frozen(P(g)!.Choices); Frozen(retained!.Players[0].Hand);
            Frozen(g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == penalty.CardUseFrameId).ActorHandLimitAnnouncedTargets!); Private(g);
            var p = P(g)!;
            Reject(g, new AnswerPromptCommand(0, p.PromptId, p.Choices.First().Id, g.Revision));
            Reject(g, new AnswerPromptCommand(0, p.PromptId, new ChoiceId("forged-penalty-child"), g.Revision));
            Reject(g, new AdvanceOneStepCommand(g.Revision - 1));
            g = Cold(g, registry); Step(g);
            if (scenario == Scenario.SourceSwitch)
            {
                Reach(g, q => q?.SkillPrompt?.SkillId == Reacquire);
                Require(V(g, 1).Skills!.All(s => s.Id != Penalty) && E<OrderedPrintedSkillGrantRemovedEvent>(g).Any(e =>
                    e.Grant.SkillId == Penalty && e.Grant.SkillInstanceId == penalty.Source.SkillInstanceId),
                    "A real nested damage/ordered printed loss removes the exact issued A source before native reacquisition.");
                g = Cold(g, registry); Step(g);
            }
            Reach(g, IsComplete); var use = UseFor(g, a.CardId!.Value);
            Require(Grants(g).Count(x => x.ActionId == penalty.ActionId && x.TargetSeat == 1) == 1 && !Expired(g).Any() &&
                E<OrderedPrintedSkillGrantRemovedEvent>(g).Any(e => e.Grant.SkillId == Penalty && e.Grant.SkillInstanceId == penalty.Source.SkillInstanceId) &&
                (scenario == Scenario.SourceSwitch ? E<SkillsAcquiredEvent>(g).Any(e => e.PlayerSeat == 1 && e.SourceSkillId == Reacquire && e.SkillIds.Contains(Penalty)) : V(g, 1).Skills!.All(s => s.Id != Penalty)) &&
                (scenario != Scenario.SourceSwitch || V(g, 1).SkillRuntimeStates!.Any(s => s.SkillId == Penalty && s.IsAcquired &&
                    s.BooleanStates?.Any(b => b.StateId == "source-identity" && b.SkillInstanceId != penalty.Source.SkillInstanceId) == true)) &&
                g.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(1) && m.Reason.Value == DrawReason) == 1 &&
                SnapshotJson.Serialize(retained!) == retainedJson,
                "The live Use cannot issue a second penalty when its original source is lost or a different instance takes over; the owed draw and retained private snapshot stay once and immutable.");
            Private(g); g = Cold(g, registry); Continue(g); Reach(g, IsPlay);
            AssertPhysicalUse(g, a.CardId.Value, CardKind.IronChain, use.Id); _ = Cold(g, registry);
        }
        var (late, lr) = Start(Scenario.Late, CardKind.Slash, Suit.Spade);
        var action = Action(late, LegalActionKind.Slash, [1]); Play(late, action);
        Reach(late, p => p?.SkillPrompt?.SkillId == Late);
        var live = UseFor(late, action.CardId!.Value);
        Require(V(late, 1).Skills!.Any(s => s.Id == Penalty) && Grants(late).Length == 0 &&
            live.ActorHandLimitAnnouncedTargets!.SequenceEqual([1]),
            "The original target was genuinely announced without this skill; acquiring it inside that finalized window cannot create a retroactive opportunity.");
        Frozen(live.ActorHandLimitAnnouncedTargets!); late = Cold(late, lr); Reach(late, IsPlay);
        Require(Grants(late).Length == 0, "Returning the grant/choice child cannot retroactively punish the old action.");
        var next = Action(late, LegalActionKind.Slash, [1]); Play(late, next); Reach(late, IsComplete);
        Require(Grants(late) is [var one] && one.ActionId != live.Action!.ActionId && one.TargetSeat == 1,
            "The newly acquired skill does punish a later independent real Use, so the negative was not a missing-capability fixture.");
        late = Cold(late, lr); Continue(late); Reach(late, IsPlay); _ = Cold(late, lr);
    }

    private static ActorHandLimitPenalty[] Grants(GameEngine g) => E<ProgramActorHandLimitPenaltyGrantedEvent>(g).Select(e => e.Penalty).ToArray();
    private static bool SameTurnGrant(ProgramTurnSkillsGrantedEvent a, ProgramTurnSkillsGrantedEvent b) =>
        a.FrameId == b.FrameId && a.SkillId == b.SkillId && a.BindingId == b.BindingId && a.OwnerSeat == b.OwnerSeat &&
        a.RecipientSeat == b.RecipientSeat && a.GrantSourceId == b.GrantSourceId && a.TurnExpiry == b.TurnExpiry &&
        a.GrantedSkillIds.SequenceEqual(b.GrantedSkillIds, StringComparer.Ordinal);
    private static ProgramActorHandLimitPenaltyExpiredEvent[] Expired(GameEngine g) => E<ProgramActorHandLimitPenaltyExpiredEvent>(g);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsPlay(PendingDecision? p) => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    private static bool IsComplete(PendingDecision? p) => p?.SkillPrompt?.SkillId == Complete;
    private static bool IsEnded(PendingDecision? p) => p?.SkillPrompt?.SkillId == Ended;
    private static CardUseFrame UseFor(GameEngine g, int card) => g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == card);
    private static LegalAction Action(GameEngine g, LegalActionKind kind, IReadOnlyList<int> targets) => g.GetHumanLegalActions().FirstOrDefault(a => a.Kind == kind && a.TargetSeats.SequenceEqual(targets))
        ?? throw new InvalidOperationException("Missing actual legal " + kind + ": " + Diagnostic(g));
    private static void Play(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats,
        g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    private static void Activate(GameEngine g, string id, IReadOnlyList<int> targets) => Accept(g,
        new UseProgramSkillCommand(0, Driver, id, [], targets, g.Revision, P(g)!.PromptId));
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> match)
    {
        var p = P(g)!; Require(p.PlayerSeat == 0, "Only the real human chooser submits a prompt answer.");
        var c = p.Choices.FirstOrDefault(match) ?? throw new InvalidOperationException("Missing published choice: " + Diagnostic(g));
        Accept(g, new AnswerPromptCommand(0, p.PromptId, c.Id, g.Revision));
    }
    private static void Reach(GameEngine g, Func<PendingDecision?, bool> ready)
    {
        for (var i = 0; i < 220; i++)
        {
            if (ready(P(g))) return;
            Require(!IsPlay(P(g)), "Native resolution returned to Play before its required boundary: " + Diagnostic(g));
            Step(g);
        }
        throw new InvalidOperationException("Bounded native penalty driver missed its boundary: " + Diagnostic(g));
    }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is not { PlayerSeat: 0 }) { Accept(g, new AdvanceOneStepCommand(g.Revision)); return; }
        if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) { Continue(g); return; }
        if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
        { Answer(g, c => c.Cards.Count == 0); return; }
        if (p.Kind == DecisionKind.DiscardCards)
        { Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision)); return; }
        if (p.Kind == DecisionKind.SelectTargetCard) { Answer(g, _ => true); return; }
        throw new InvalidOperationException("Unexpected real human penalty boundary: " + Diagnostic(g));
    }
    private static void AssertPhysicalUse(GameEngine g, int card, CardKind kind, long id)
    {
        var finish = kind == CardKind.IronChain ? CardMoveReasons.IronChainFinished : CardMoveReasons.UseFinished;
        Require(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == id && e.CardId == card && e.CardKind == kind) == 1 &&
            g.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            g.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == finish) == 1 &&
            !g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == id), "Original physical payment, native finish, and child return each occur once.");
    }
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "A real actor-penalty command was rejected.");
    }
    private static void Reject(GameEngine g, GameCommand c)
    { var before = State(g); Require(!g.Submit(c).Accepted && State(g) == before, "Wrong actor/forged choice/stale revision rejects atomically, preserving all paid facts and views."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), g.CardMovements,
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics()
    });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Require(State(restored) == State(g), "Serialized command-prefix cold replay retains exact use/action, contributions, frozen announcements, native children and four private views."); return restored;
    }
    private static void Private(GameEngine g)
    { foreach (var s in Enumerable.Range(0, 4)) foreach (var p in g.CreateSnapshot(s).Players.Where(p => p.Seat != s)) Require(p.Hand.Count == 0, "Target notifications and public contributions never reveal foreign Hand entities."); }
    private static void Frozen<T>(IReadOnlyList<T> list)
    {
        Require(list is IList<T> { IsReadOnly: true }, "Published nested collections have a read-only implementation.");
        var rejected = false;
        try { var writable = (IList<T>)list; if (writable.Count == 0) writable.Add(default!); else writable[0] = writable[0]; }
        catch (NotSupportedException) { rejected = true; }
        Require(rejected, "Observers cannot mutate frozen nested lists, even by replacing an equal element.");
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new
    {
        Prompt = P(g) is { } p ? new { p.Kind, p.PlayerSeat, Skill = p.SkillPrompt?.SkillId, p.RequiredCardCount, Choices = p.Choices.Select(c => new { c.Id, c.Targets, c.Cards, c.Parameters }) } : null,
        Players = g.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.GeneralId, p.Hp, p.HandCount }),
        Frames = g.ResolutionStack.Select(f => new { f.Id, Type = f.GetType().Name }),
        Penalties = Grants(g), LastFacts = g.Events.TakeLast(8).Select(e => new { Type = e.Payload.GetType().Name, Payload = JsonSerializer.Serialize(e.Payload, e.Payload.GetType()) })
    });
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Start(Scenario scenario, CardKind kind, Suit suit)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new Fixture(scenario, kind, suit));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Reach(g, IsPlay);
        Require(V(g, 1).GeneralId == "fixture:actor-penalty-peer-1", "Verified Seed31 and ordinary general-selection weights fix the first native recipient at seat one.");
        Private(g); return (g, registry);
    }

    private sealed class Fixture(Scenario scenario, CardKind kind, Suit suit) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-actor-hand-penalty", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            if (kind == CardKind.BorrowedSword) b.AddCard(StandardContentRegistry.CreateWithClassicGenerals().GetCard("classic:borrowed-sword"));
            var rules = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:actor-penalty-driver","revision":1,"modifiers":[
                {"id":"range","query":"attackRange","operation":"add","value":4,"priority":0},
                {"id":"quota","query":"slashLimit","operation":"add","value":4,"priority":0}],"activations":[
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                {"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"request"}]},
                {"id":"foreign-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]}]},
              {"id":"fixture:actor-penalty","revision":1,"states":[{"id":"source-identity","initialValue":false,"visibility":"public","resetScope":"game","reacquirePolicy":"preserveUntilGameEnd"}],"triggers":[
                {"id":"slash","window":"cardUseTargetsFinalized","ownerRelation":"target","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"singleActionInstance":true,"onlyDesignatedCardTargets":true,"effects":[{"op":"grantActorHandLimitPenalty","target":"owner","amount":1},{"op":"draw","target":"owner","amount":1}]},
                {"id":"black-trick","window":"cardUseTargetsFinalized","ownerRelation":"target","cardCategories":["instantTrick"],"condition":{"kind":"cardActionCardIsBlack"},"optional":false,"singleActionInstance":true,"onlyDesignatedCardTargets":true,"effects":[{"op":"grantActorHandLimitPenalty","target":"owner","amount":1},{"op":"draw","target":"owner","amount":1}]}]},
              {"id":"fixture:actor-penalty-complete","revision":1,"triggers":[{"id":"complete","window":"cardUseCompleted","ownerRelation":"observer","singleActionInstance":true,"cardKinds":["slash","fireSlash","thunderSlash","ironChain","indulgence","borrowedSword","duel"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:actor-penalty-ended","revision":1,"triggers":[
                {"id":"own-ended","window":"afterTurnEnded","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"own","options":[{"id":"continue"}]}]},
                {"id":"other-ended","window":"afterTurnEnded","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"other","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:actor-penalty-gain","revision":1,"triggers":[{"id":"real-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.fixture:actor-penalty.Draw"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]},{"op":"damage","target":"owner","amount":1}]}]},
              {"id":"fixture:actor-penalty-add","revision":1,"triggers":[{"id":"add","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash","borrowedSword"],"priority":100,"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"addCurrentCardUseTarget","target":"selectedTarget"}]}]},
              {"id":"fixture:actor-penalty-redirect","revision":1,"triggers":[{"id":"real-tail-redirect","window":"slashTargetRedirecting","ownerRelation":"target","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"slashRedirectable"},{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand","equipment"],"count":1,"destination":"discardPile","awaitMovementTriggers":true},{"op":"redirectCurrentAttack","target":"selectedTarget"}]}]},
              {"id":"fixture:actor-penalty-initial","revision":1,"triggers":[{"id":"second-source","window":"cardUseTargetsFinalized","ownerRelation":"target","cardKinds":["slash","fireSlash","thunderSlash"],"priority":100,"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"grantTurnSkills","target":"owner","skillIds":["fixture:actor-penalty"],"expiry":"actualTurnEnd"}]}]},
              {"id":"fixture:actor-penalty-printed-loss","revision":1,"triggers":[{"id":"loss","window":"afterDamageApplied","subject":"any","damageOccurrence":"perDamage","optional":false,"effects":[{"op":"loseFirstPrintedSkillsUntilTurnEndAndDraw","target":"owner"}]}]},
              {"id":"fixture:actor-penalty-reacquire","revision":1,"triggers":[{"id":"new-instance","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.fixture:actor-penalty-printed-loss.ordered-printed-skill-loss.draw"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]},{"op":"grantSkills","target":"owner","skillIds":["fixture:actor-penalty"]}]}]},
              {"id":"fixture:actor-penalty-late","revision":1,"triggers":[{"id":"late-source","window":"cardUseTargetsFinalized","ownerRelation":"target","cardKinds":["slash"],"priority":100,"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"grantSkills","target":"owner","skillIds":["fixture:actor-penalty"]},{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:actor-penalty-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """)!.AsObject();
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            var presentations = rules["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(), n =>
            {
                var id = n!["id"]!.GetValue<string>(); var value = new Dictionary<string, object> { ["name"] = id, ["description"] = "真实用牌与实际使用者回合结束的共享上限贡献" };
                if (id is Complete or Ended or Gain or Reacquire or Late) value["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                return (object)value;
            });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "共享指定目标与上限贡献夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:actor-penalty-first", "固定首位", "已验证Seed31原生选将权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            b.AddSkill(new("fixture:actor-penalty-peer", "固定其他角色", "普通选将权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 100d) });
            b.AddSkill(new("fixture:actor-penalty-middle", "固定中间位", "保持原生流离唯一合法目标的选将权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 5000d) });
            var ownerExtras = new List<string> { Complete, Ended };
            if (scenario == Scenario.Foreign) ownerExtras.Add(Penalty);
            if (scenario is Scenario.Tail or Scenario.Borrowed) ownerExtras.Add(Add);
            b.AddGeneral(new(Owner, "真实用牌者", "supporter", Driver, "wei", scenario == Scenario.Stack ? 2 : 12, ownerExtras));
            for (var index = 1; index < 4; index++)
            {
                var extras = new List<string> { "fixture:actor-penalty-quiet" };
                var primary = index == 1 && (scenario is Scenario.SourceLoss or Scenario.SourceSwitch) ? Penalty : index == 1 ? "fixture:actor-penalty-first" : "fixture:actor-penalty-peer";
                if (primary == Penalty) extras.Add("fixture:actor-penalty-first");
                if (scenario != Scenario.Foreign && !(scenario == Scenario.Late && index == 1) && primary != Penalty) extras.Add(Penalty);
                if (scenario == Scenario.Stack && index == 1) extras.Add(Initial);
                if (scenario == Scenario.Tail && index == 2) extras.Add(Redirect);
                if (scenario == Scenario.Tail && index == 3) extras.Add("fixture:actor-penalty-middle");
                if ((scenario is Scenario.SourceLoss or Scenario.SourceSwitch) && index == 1) { extras.Add(Gain); extras.Add(PrintedLoss); }
                if (scenario == Scenario.SourceSwitch && index == 1) extras.Add(Reacquire);
                if (scenario == Scenario.Late && index == 1) extras.Add(Late);
                b.AddGeneral(new($"fixture:actor-penalty-peer-{index}", "真实原生其他角色", "supporter", primary, "shu", 12, extras));
            }
            var definition = kind switch { CardKind.Slash => "standard:slash", CardKind.IronChain => "standard:iron_chain", CardKind.Indulgence => "standard:indulgence", CardKind.BorrowedSword => "classic:borrowed-sword", _ => throw new InvalidOperationException("Unsupported tiny fixture card.") };
            // Compound payment deliberately has neither Slash nor Nullification: both real holders transfer their weapons.
            b.AddDeck(new("fixture:actor-penalty-deck", "固定真实小牌库", scenario == Scenario.Stack ? 8 : 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(scenario == Scenario.Borrowed ? i % 3 == 0 ? "standard:crossbow" : i % 3 == 1 ? definition : "standard:dodge" : definition, suit, 7)).ToArray() });
            b.AddMode(new(Mode, "共享实际使用者手牌上限贡献", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:actor-penalty-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, "fixture:actor-penalty-peer-1", "fixture:actor-penalty-peer-2", "fixture:actor-penalty-peer-3"]));
        }
    }
}
