namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsOrderedSelectedActorDuelProgram(ProgramSkillFrame frame) =>
        frame.TriggerId is null && frame.WindowContext is null &&
        ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).Instructions is
            [{ Op: SkillProgramEffectOp.DiscardSelected }, var duel] && OrderedSelectedActorDuelContract.IsOptIn(duel);

    private bool TryPayOrderedSelectedActorDuel(ProgramSkillFrame frame, IReadOnlyList<int> ids, CardMoveReason reason)
    {
        if (!IsOrderedSelectedActorDuelProgram(frame)) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        OrderedSelectedActorDuelContract.ValidateActivation(frame.SkillId, plan.Activation!);
        if (frame.InstructionIndex != 1 || frame.OrderedSelectedActorDuel is not null || ids.Count != 1 ||
            !ids.SequenceEqual(frame.SelectedCardIds) || frame.SelectedTargetSeats is not [var actor, var target] ||
            actor == target || actor == frame.OwnerSeat || target == frame.OwnerSeat ||
            frame.SelectedCardPayment is not { InstructionIndex: 1, Operation: SkillProgramEffectOp.DiscardSelected, MovementCommitted: false } ||
            reason != new CardMoveReason($"skill-program.{frame.SkillId}.{SkillProgramEffectOp.DiscardSelected}"))
            throw new InvalidOperationException("Ordered Duel payment requires its exact original one-card activation.");
        MoveProgramCardsFromMultipleSources(ids, CardLocation.DiscardPile, reason, (batchId, moves) =>
        {
            if (moves is not [var move] || move.CardId != ids[0] || move.From.OwnerSeat != frame.OwnerSeat ||
                move.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))
                throw new InvalidOperationException("Ordered Duel must pay its owner's selected original entity once.");
            var paid = new OrderedSelectedActorDuelPayment(frame.Id, frame.OwnerSeat, actor, target,
                frame.SkillId, frame.ActivationId, frame.SkillInstanceId, frame.GameplayHash, _turnNumber,
                batchId, move.Sequence, move.CardId, move.CardKind, move.From, move.To, move.Reason.Value);
            var active = GetActiveProgramFrame(frame.Id);
            ReplaceRuntimeTop(active with { OrderedSelectedActorDuel = new(paid) });
            AdvanceEventRulesAndQueueFact(new OrderedSelectedActorDuelPaidEvent(paid));
        }, disclosure: CardMovementDisclosure.FaceUp);
        return true;
    }

    private bool HasExactOrderedSelectedActorDuelPayment(ProgramSkillFrame frame)
    {
        if (frame.OrderedSelectedActorDuel is not { Payment: { } paid } state || !IsOrderedSelectedActorDuelProgram(frame) ||
            frame.InstructionIndex is < 1 or > 2 || frame.SelectedCardIds is not [var id] || id != paid.CardId ||
            frame.SelectedTargetSeats is not [var actor, var target] || actor != paid.ActorSeat || target != paid.TargetSeat ||
            actor == target || actor == frame.OwnerSeat || target == frame.OwnerSeat || !IsValidPlayerSeat(actor) || !IsValidPlayerSeat(target) ||
            paid.ProgramFrameId != frame.Id || paid.OwnerSeat != frame.OwnerSeat || paid.SkillId != frame.SkillId ||
            paid.ActivationId != frame.ActivationId || paid.SkillInstanceId != frame.SkillInstanceId || paid.GameplayHash != frame.GameplayHash ||
            paid.TurnNumber != _turnNumber || paid.MovementBatchId <= 0 || paid.MovementSequence <= 0 ||
            paid.From.OwnerSeat != frame.OwnerSeat || paid.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || paid.From == paid.To ||
            paid.Reason != $"skill-program.{frame.SkillId}.{SkillProgramEffectOp.DiscardSelected}" ||
            frame.SelectedCardPayment is not { InstructionIndex: 1, Operation: SkillProgramEffectOp.DiscardSelected } payment ||
            !payment.CardIds.SequenceEqual([id]) ||
            _cardMovements.Count(move => move.Sequence == paid.MovementSequence && move.CardId == paid.CardId &&
                move.CardKind == paid.PrintedKind && move.From == paid.From && move.To == paid.To && move.Reason.Value == paid.Reason) != 1 ||
            CompleteProgramEventHistory().OfType<OrderedSelectedActorDuelPaidEvent>().Count(fact => fact.Payment == paid) != 1)
            return false;
        return state.IssuedCardUseFrameId is null ? !state.Completed :
            state.IssuedCardUseFrameId > 0 && CompleteProgramEventHistory().OfType<OrderedSelectedActorDuelIssuedEvent>()
                .Count(fact => fact.CardUseFrameId == state.IssuedCardUseFrameId && fact.Payment == paid) == 1;
    }

    [ProgramContinuation(ProgramContinuationKind.Movement, 2402)]
    private bool ReturnOrderedSelectedActorDuelPayment(ProgramSkillFrame frame)
    {
        if (frame.OrderedSelectedActorDuel is null || frame.SelectedCardPaymentResult is not null) return false;
        if (!HasExactOrderedSelectedActorDuelPayment(frame) || frame.InstructionIndex != 1 ||
            frame.PendingMovementContinuation is not null ||
            frame.SelectedCardPayment is not { MovementCommitted: true, ActiveChildFrameId: null } payment)
            throw new InvalidOperationException("Ordered Duel cannot return before its exact paid discard children finish.");
        ReplaceRuntimeTop(frame with { SelectedCardPaymentResult = new(payment.InstructionIndex,
            payment.Operation, payment.LastCompletedChildFrameId, true) });
        AdvanceRuntimeProgram(frame.Id);
        return true;
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 2402)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 2402)]
    private bool CanContinuePaidOrderedSelectedActorDuel(ProgramSkillFrame frame) =>
        HasExactOrderedSelectedActorDuelPayment(frame);

    private SkillProgramStepOutcome BeginOrderedSelectedActorDuel(ProgramSkillFrame frame)
    {
        if (!HasExactOrderedSelectedActorDuelPayment(frame) || frame.InstructionIndex != 2 ||
            frame.OrderedSelectedActorDuel is not { IssuedCardUseFrameId: null, Completed: false } state ||
            frame.SelectedCardPaymentResult is not { InstructionIndex: 1, Operation: SkillProgramEffectOp.DiscardSelected, Completed: true } ||
            ActiveCardAttack is not null || ActiveDuel is not null)
            throw new InvalidOperationException("Ordered Duel issuance requires its returned exact discard, not a fresh cost.");
        var paid = state.Payment;
        if (_winner != Winner.None || !CanIssueSelectedActorDuel(paid.TargetSeat, paid.ActorSeat))
            return SkillProgramStepOutcome.Continue;
        var origin = new ProgramSelectedActorDuelOrigin(frame.Id, 2, frame.OwnerSeat, paid.ActorSeat,
            frame.SkillId, frame.ActivationId, frame.SkillInstanceId, frame.GameplayHash, _turnNumber)
            { OrderedPairPayment = paid };
        var card = new Card(0, CardKind.Duel, Suit.None, 0);
        var id = BeginCardUse(card, paid.ActorSeat, [paid.TargetSeat], CardKind.Duel,
            physicalCardIds: [], selectedActorDuelOrigin: origin);
        var suspended = _resolutionStack.OfType<ProgramSkillFrame>().Single(parent => parent.Id == frame.Id);
        ReplaceRuntimeFrame(frame.Id, suspended with
            { OrderedSelectedActorDuel = state with { IssuedCardUseFrameId = id } });
        AdvanceEventRulesAndQueueFact(new OrderedSelectedActorDuelIssuedEvent(id, paid));
        BeginJizhiOrNullificationWindow(id, card, paid.ActorSeat, [paid.TargetSeat], LegalActionKind.Duel, playedCardKind: CardKind.Duel);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool MatchesOrderedSelectedActorDuel(ProgramSkillFrame frame, ProgramSelectedActorDuelOrigin origin, long useId) =>
        HasExactOrderedSelectedActorDuelPayment(frame) && frame.InstructionIndex == 2 &&
        frame.OrderedSelectedActorDuel is { Completed: false } state && state.IssuedCardUseFrameId == useId &&
        origin.OrderedPairPayment == state.Payment && origin.ParentProgramFrameId == frame.Id && origin.InstructionIndex == 2 &&
        origin.OwnerSeat == frame.OwnerSeat && origin.InitialActorSeat == state.Payment.ActorSeat &&
        origin.SkillId == frame.SkillId && origin.ActivationId == frame.ActivationId && origin.SkillInstanceId == frame.SkillInstanceId &&
        origin.GameplayHash == frame.GameplayHash && origin.TurnNumber == _turnNumber;

    private bool IsOrderedSelectedActorDuelCannotNullify(long useId)
    {
        if (LifecycleCardUse(useId)?.SelectedActorDuelOrigin is not { OrderedPairPayment: not null } origin) return false;
        if (_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(frame => frame.Id == origin.ParentProgramFrameId) is not { } parent ||
            !MatchesOrderedSelectedActorDuel(parent, origin, useId))
            throw new InvalidOperationException("Unnullifiable ordered Duel lost its exact paid native origin.");
        return true;
    }

    private void CompleteOrderedSelectedActorDuel(ProgramSkillFrame frame, ProgramSelectedActorDuelOrigin origin, long useId)
    {
        if (!MatchesOrderedSelectedActorDuel(frame, origin, useId) || _resolutionStack.LastOrDefault()?.Id != frame.Id)
            throw new InvalidOperationException("Ordered Duel lost its exact typed return and original discard owner.");
        ReplaceRuntimeTop(frame with { OrderedSelectedActorDuel = frame.OrderedSelectedActorDuel! with { Completed = true } });
        AdvanceRuntimeProgram(frame.Id);
    }

    [NativeAttackReturn(NativeAttackReturnPhase.BeforeParentResume, 2402)]
    private bool ReturnOrderedSelectedActorDuelAttack(AttackCompletionReceipt completion)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { OrderedSelectedActorDuel: { } state } frame ||
            state.IssuedCardUseFrameId != completion.ResolutionId) return false;
        if (state.Completed || completion.ProgramFrameId != frame.Id || !completion.RequestedSlashChild ||
            !HasExactOrderedSelectedActorDuelPayment(frame) ||
            CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(fact =>
                fact.ResolutionId == completion.ResolutionId && fact.CardId == 0 && fact.CardKind == CardKind.Duel) != 1)
            throw new InvalidOperationException("Ordered Duel completion cannot borrow another program's paid return.");
        // The completed CardUse has been popped; its exact issued fact and owning
        // scalar payment remain, while response cards keep their native owners.
        ReplaceRuntimeTop(frame with { OrderedSelectedActorDuel = state with { Completed = true } });
        AdvanceRuntimeProgram(frame.Id);
        return true;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 2402)]
    private void AssertOrderedSelectedActorDuelPayment(ProgramSkillFrame frame)
    {
        if (frame.OrderedSelectedActorDuel is not null && !HasExactOrderedSelectedActorDuelPayment(frame))
            throw new InvalidOperationException("Ordered Duel payment lost its original command, physical movement or source identity.");
    }

    private void AssertOrderedSelectedActorDuelUse(CardUseFrame use, ProgramSkillFrame parent, ProgramSelectedActorDuelOrigin origin)
    {
        if (!MatchesOrderedSelectedActorDuel(parent, origin, use.Id) || use.CardId != 0 || use.CardKind != CardKind.Duel ||
            use.PhysicalCardIds?.Count != 0 ||
            use.Action is not { Type: CardActionType.Use, EffectiveKind: CardKind.Duel, PhysicalCards.Count: 0,
                ConversionChain.Count: 0, EffectiveSuit: Suit.None, EffectiveIsRed: false })
            throw new InvalidOperationException("Ordered Duel must retain its independent zero-entity Use and exact paid parent.");
    }
}
