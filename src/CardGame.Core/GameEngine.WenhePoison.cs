using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>Visibility of this departure, explicitly supplied by its native producer.</summary>
public enum CardMovementDisclosure { Unspecified, FaceUp, FaceDown }

public sealed partial record CardMovementRecord
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public CardMovementDisclosure Disclosure { get; init; }
}

public sealed record WenhePoisonDeparture(int RoundNumber, int MovementSequence, int OwnerSeat, int CardId);
public enum WenhePoisonLossStage { Ready, HealthChildren, Dying, Completed }
public sealed record WenhePoisonLossReceipt(int Cursor, WenhePoisonLossStage Stage,
    int HpBefore = 0, int HpAfter = 0, long? DyingFrameId = null);

public sealed partial record CardMovementBatchContext
{
    private readonly IReadOnlyList<WenhePoisonDeparture>? _wenhePoisonDepartures;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<WenhePoisonDeparture>? WenhePoisonDepartures
    {
        get => _wenhePoisonDepartures;
        init => _wenhePoisonDepartures = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}
public sealed partial record CardsMovedTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WenhePoisonLossReceipt? WenhePoisonLoss { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WenhePoisonFireAttackReturn? WenhePoisonFireAttackReturn { get; init; }
}

public sealed record WenhePoisonFireAttackPayment(int CostCardId, int TargetSeat, int RevealedCardId, Suit RevealedSuit,
    int SequenceBefore, int SequenceAfter, IReadOnlyList<long> BatchIds, int BatchCursor = 0, long? ActiveChildId = null)
{
    private readonly IReadOnlyList<long> _batchIds = Array.AsReadOnly(BatchIds.ToArray());
    public IReadOnlyList<long> BatchIds { get => _batchIds; init => _batchIds = Array.AsReadOnly(value.ToArray()); }
}
public sealed record WenhePoisonFireAttackReturn(long ParentFrameId, int CostCardId, long BatchId);
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WenhePoisonFireAttackPayment? WenhePoisonFireAttackPayment { get; init; }
}
public sealed record WenhePoisonFireAttackPaidEvent(long CardUseFrameId, int CostCardId, int TargetSeat,
    int RevealedCardId, Suit RevealedSuit, int SequenceBefore, int SequenceAfter) : IGameEvent;

public sealed record WenhePoisonLossAppliedEvent(long WindowFrameId, long BatchId, int RoundNumber,
    int MovementSequence, int OwnerSeat, int CardId, int HpBefore, int HpAfter) : IGameEvent;
public sealed record WenhePoisonDepartureSettledEvent(long WindowFrameId, long BatchId,
    int MovementSequence, int OwnerSeat, int CardId) : IGameEvent;

public sealed partial class GameEngine
{
    private bool TryBeginWenhePoisonFireAttackPayment(CardUseFrame use, Card revealed, Card cost, int sequenceBefore)
    {
        if (_modeDefinition.WenheActivity is null || !_pendingCardsMovedBatches.Any(batch =>
                batch.ParentFrameId == use.Id && batch.WenhePoisonDepartures?.Any(d => d.CardId == cost.Id && d.MovementSequence > sequenceBefore) == true))
            return false;
        if (_resolutionStack.LastOrDefault()?.Id != use.Id || use.WenhePoisonFireAttackPayment is not null ||
            use.CardKind != CardKind.FireAttack || _pendingDecision is not null)
            throw new InvalidOperationException("Poison Fire Attack requires its exact newly paid discard boundary.");
        var after = _movementSequence;
        var batches = _pendingCardsMovedBatches.Where(batch => batch.ParentFrameId == use.Id && batch.Movements.Count > 0 &&
            batch.Movements.All(move => move.CardId == cost.Id && move.Sequence > sequenceBefore && move.Sequence <= after))
            .OrderBy(batch => batch.Id).Select(batch => batch.Id).ToArray();
        var payment = new WenhePoisonFireAttackPayment(cost.Id, GetFireAttackTargetSeat(use), revealed.Id, revealed.Suit,
            sequenceBefore, after, batches);
        ReplaceRuntimeTop(use with { WenhePoisonFireAttackPayment = payment, Step = ResolutionFrameStep.ResolvingEffect });
        AdvanceEventRulesAndQueueFact(new WenhePoisonFireAttackPaidEvent(use.Id, cost.Id, payment.TargetSeat,
            revealed.Id, revealed.Suit, sequenceBefore, after));
        ContinueWenhePoisonFireAttackPayment(use.Id);
        return true;
    }

    private void ContinueWenhePoisonFireAttackPayment(long useId)
    {
        if (_resolutionStack.LastOrDefault() is not CardUseFrame use || use.Id != useId ||
            use.WenhePoisonFireAttackPayment is not { ActiveChildId: null } payment)
            throw new InvalidOperationException("Poison Fire Attack lost its frozen paid owner.");
        AssertWenhePoisonFireAttackPayment(use);
        while (payment.BatchCursor < payment.BatchIds.Count)
        {
            var batchId = payment.BatchIds[payment.BatchCursor];
            var batch = _pendingCardsMovedBatches.SingleOrDefault(batch => batch.Id == batchId) ??
                throw new InvalidOperationException("Poison Fire Attack lost an unpaid movement child.");
            _pendingCardsMovedBatches.Remove(batch);
            var child = CreateCardsMovedProgramWindow(batch);
            if (child is null)
            {
                payment = payment with { BatchCursor = payment.BatchCursor + 1 };
                ReplaceRuntimeTop(use = use with { WenhePoisonFireAttackPayment = payment });
                continue;
            }
            ReplaceRuntimeTop(use with { WenhePoisonFireAttackPayment = payment with { ActiveChildId = child.Id } });
            PushRuntimeFrame(child with { WenhePoisonFireAttackReturn = new(use.Id, payment.CostCardId, batch.Id) });
            AdvanceRuntimeTop<CardsMovedTriggerWindowFrame>();
            return;
        }
        var canDamage = _winner == Winner.None && _players[use.SourceSeat].IsAlive && _players[payment.TargetSeat].IsAlive;
        AdvanceEventRulesAndQueueFact(new FireAttackResolvedEvent(use.Id, use.SourceSeat, payment.TargetSeat,
            payment.RevealedCardId, payment.RevealedSuit, payment.CostCardId, canDamage));
        var card = GetFireAttackEffectCard(use);
        ReplaceRuntimeTop(use with { FireAttackSelection = null, WenhePoisonFireAttackPayment = null });
        if (!canDamage)
        {
            MoveFinishedTrickCard(use.Id, card);
            FinishCardUse(use.Id, card, use.CardKind);
            return;
        }
        var attack = new CardAttackHandle(this, use.Id, use.SourceSeat, payment.TargetSeat,
            (IsTieredRoundZeroFireAttackUse(use.Id) || IsMouZhijueZeroUse(use.Id) || IsQinLangXianyingUse(use.Id)) ? null : card,
            damageAmount: 1, playedCardKind: CardKind.FireAttack, physicalCards: GetCardUsePhysicalCards(use.Id));
        ActiveCardAttack = attack;
        if (!ApplyAttackDamage(attack)) CompleteAttack(attack);
    }

    private bool ReturnWenhePoisonFireAttackMovement(CardsMovedTriggerWindowFrame child)
    {
        if (child.WenhePoisonFireAttackReturn is not { } returned) return false;
        if (_resolutionStack.LastOrDefault() is not CardUseFrame parent || parent.Id != returned.ParentFrameId ||
            !IsWenhePoisonFireAttackMovement(parent, child))
            throw new InvalidOperationException("Poison Fire Attack movement returned to a different paid owner.");
        var payment = parent.WenhePoisonFireAttackPayment!;
        ReplaceRuntimeTop(parent with { WenhePoisonFireAttackPayment = payment with
            { BatchCursor = payment.BatchCursor + 1, ActiveChildId = null } });
        ContinueWenhePoisonFireAttackPayment(parent.Id);
        return true;
    }

    private bool IsWenhePoisonFireAttackMovement(CardUseFrame parent, CardsMovedTriggerWindowFrame child) =>
        parent.WenhePoisonFireAttackPayment is { } payment && child.WenhePoisonFireAttackReturn is { } returned &&
        returned.ParentFrameId == parent.Id && returned.CostCardId == payment.CostCardId && returned.BatchId == child.Batch.Id &&
        child.Id == child.Batch.Id && payment.ActiveChildId == child.Id && payment.BatchCursor < payment.BatchIds.Count &&
        payment.BatchIds[payment.BatchCursor] == child.Batch.Id && child.Batch.ParentFrameId == parent.Id &&
        child.Batch.Movements.Count > 0 && child.Batch.Movements.All(move => move.CardId == payment.CostCardId &&
            move.Sequence > payment.SequenceBefore && move.Sequence <= payment.SequenceAfter) && child.ResumeProgramFrameId is null;

    private bool HasWenhePoisonFireAttackPrompt() => _resolutionStack.OfType<CardUseFrame>().Any(use =>
        use.WenhePoisonFireAttackPayment is { ActiveChildId: { } id } &&
        _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(child => child.Id == id && IsWenhePoisonFireAttackMovement(use, child)));

    private void AssertWenhePoisonFireAttackPayment(CardUseFrame use)
    {
        var p = use.WenhePoisonFireAttackPayment!;
        var moves = _cardMovements.Where(move => move.Sequence > p.SequenceBefore && move.Sequence <= p.SequenceAfter).ToArray();
        if (_modeDefinition.WenheActivity is null || use.CardKind != CardKind.FireAttack ||
            use.FireAttackSelection?.RevealedCardId != p.RevealedCardId || GetFireAttackTargetSeat(use) != p.TargetSeat ||
            p.BatchIds is not System.Collections.IList { IsReadOnly: true } || p.BatchIds.Count != 2 || p.BatchIds.Distinct().Count() != 2 ||
            p.BatchCursor < 0 || p.BatchCursor > p.BatchIds.Count || p.ActiveChildId is not null && p.BatchCursor >= p.BatchIds.Count ||
            moves.Length != 2 || moves.Any(move => move.CardId != p.CostCardId) ||
            moves[0].From != CardLocation.Hand(use.SourceSeat) || moves[0].To != CardLocation.Processing ||
            moves[0].Reason != CardMoveReasons.FireAttackDiscard || moves[0].Disclosure != CardMovementDisclosure.FaceUp ||
            moves[1].From != CardLocation.Processing || moves[1].To != CardLocation.DiscardPile || moves[1].Reason != CardMoveReasons.FireAttackDiscardFinished ||
            GetAttackCard(p.CostCardId).Kind != CardKind.Peach ||
            ProgramEventHistory<WenhePoisonFireAttackPaidEvent>().Count(e => e == new WenhePoisonFireAttackPaidEvent(use.Id,
                p.CostCardId, p.TargetSeat, p.RevealedCardId, p.RevealedSuit, p.SequenceBefore, p.SequenceAfter)) != 1)
            throw new InvalidOperationException("Poison Fire Attack changed its exact paid discard or native effect continuation.");
    }
    private int? CaptureWenhePoisonRound() => _modeDefinition.WenheActivity is not null &&
        _setupComplete && _winner == Winner.None && CurrentWenheRule == WenheRuleKind.Poison ? _roundNumber : null;

    private bool IsWenhePhysicalPoison(CharacterState owner, Card card) =>
        _modeDefinition.WenheActivity is not null && CurrentWenheRule == WenheRuleKind.Poison &&
        card.Kind == CardKind.Peach && card.Id > 0 && owner.IsAlive &&
        _cardZones.GetLocation(card.Id) == CardLocation.Hand(owner.Seat);

    // Match conversion input names against the current held identity without
    // rewriting the physical entity recorded by native material payments.
    private CardKind EffectiveConversionInputKind(CharacterState owner, Card card) =>
        IsWenhePhysicalPoison(owner, card) ? CardKind.Poison : card.Kind;

    private CardSnapshot ProjectWenhePoisonHandCard(CharacterState owner, Card card, CardSnapshot snapshot) =>
        IsWenhePhysicalPoison(owner, card) ? snapshot with
        { IdentityKind = CardKind.Poison, IdentitySkillName = "宴安鸩毒" } : snapshot;

    private CardMovementBatchContext CaptureWenhePoisonDepartures(CardMovementBatchContext batch, int? poisonRound)
    {
        if (poisonRound is not > 0 || _modeDefinition.WenheActivity is null) return batch;
        var departures = batch.Movements.Where(move => GetAttackCard(move.CardId).Kind == CardKind.Peach &&
            move.Disclosure == CardMovementDisclosure.FaceUp && move.From is { Zone: CardZoneKind.Hand, OwnerSeat: { } owner } &&
            IsValidPlayerSeat(owner) && _players[owner].IsAlive && move.To != move.From)
            .OrderBy(move => move.Sequence)
            .Select(move => new WenhePoisonDeparture(poisonRound.Value, move.Sequence, move.From.OwnerSeat!.Value, move.CardId)).ToArray();
        return departures.Length == 0 ? batch : batch with { WenhePoisonDepartures = departures };
    }

    private bool HasWenhePoisonDepartures(CardMovementBatchContext batch) => batch.WenhePoisonDepartures is { Count: > 0 };

    private bool IsWenhePoisonUsePaymentBatch(CardUseFrame use, CardMovementBatchContext batch) =>
        HasWenhePoisonDepartures(batch) && batch.ParentFrameId == use.Id && batch.ParentBatchId is null &&
        IsWenhePoisonUsePaymentOrigin(use, batch) && use.Action is { Type: CardActionType.Use } action &&
        batch.Movements.Count == action.PhysicalCards.Count && batch.Movements.Count > 0 &&
        batch.Movements.Select(move => move.CardId).Distinct().Count() == batch.Movements.Count &&
        batch.Movements.All(move => move.Reason == CardMoveReasons.Use && move.Disclosure == CardMovementDisclosure.FaceUp &&
            move.To == CardLocation.Processing && action.PhysicalCards.Count(cost => cost.CardId == move.CardId &&
                cost.From == move.From && cost.CardKind == move.CardKind) == 1 &&
            move.Sequence > 0 && move.Sequence <= _cardMovements.Count && _cardMovements[move.Sequence - 1] == move);

    private bool IsWenhePoisonUsePaymentOrigin(CardUseFrame use, CardMovementBatchContext batch)
    {
        if (batch.AwaitingProgramFrameId is null) return true;
        // A real rescue Use can sit above a suspended discard program. That
        // ancestor owns its original payment, not this newly issued rescue cost.
        var useIndex = _resolutionStack.FindIndex(frame => frame.Id == use.Id);
        if (use.DyingResponse is not { } response || useIndex < 1 ||
            _resolutionStack[useIndex - 1] is not DyingFrame dying || response.ResolutionId != dying.Id ||
            response.ResponderSeat != use.SourceSeat || dying.ResponderSeat != use.SourceSeat ||
            !(use.CardKind == CardKind.Peach && response.UsedPeach || use.CardKind == CardKind.Alcohol && response.UsedAlcohol)) return false;
        var programIndex = _resolutionStack.FindIndex(frame => frame.Id == batch.AwaitingProgramFrameId);
        return programIndex >= 0 && programIndex < useIndex - 1 &&
            _resolutionStack[programIndex] is ProgramSkillFrame program && IsAwaitingProgramMovement(program) &&
            batch.OriginOwnerSeat == program.OwnerSeat && batch.OriginSkillId == program.SkillId &&
            batch.OriginSkillInstanceId == program.SkillInstanceId;
    }

    private void CaptureWenhePoisonUsePayment(CardMovementBatchContext batch)
    {
        if (_resolutionStack.LastOrDefault() is not CardUseFrame use || use.PaymentMovementReceipt is not null ||
            !IsWenhePoisonUsePaymentBatch(use, batch)) return;
        ReplaceRuntimeTop(use with { PaymentMovementReceipt = new(use.Id, use.Action!.ActionId,
            batch.Movements[0].Sequence - 1, batch.Movements[^1].Sequence) { Batch = batch } });
    }

    private bool HasWenhePoisonUsePaymentTail(CardUseFrame use, RecoveryPaidCardUseContinuation continuation) =>
        continuation.Kind is RecoveryPaidCardUseKind.Trick or RecoveryPaidCardUseKind.Simple or RecoveryPaidCardUseKind.EquipmentUse &&
        use.PaymentMovementReceipt is { } receipt && receipt.OwnerFrameId == use.Id &&
        IsWenhePoisonUsePaymentBatch(use, receipt.Batch) && CardUsePaymentBatchMatchesReceipt(use, receipt.Batch);
    private bool IsPendingWenhePoisonWindow(CardsMovedTriggerWindowFrame frame) =>
        HasWenhePoisonDepartures(frame.Batch) && frame.CandidateIndex == 0 &&
        (frame.WenhePoisonLoss is null || frame.WenhePoisonLoss is { Stage: not WenhePoisonLossStage.Completed } receipt &&
            receipt.Cursor >= 0 && receipt.Cursor < frame.Batch.WenhePoisonDepartures!.Count);

    private bool IsCurrentWenhePoisonDying() => ActiveDying is { } dying &&
        _resolutionStack.FindIndex(frame => frame.Id == dying.Id) is var index && index > 0 &&
        IsWenhePoisonDyingChild(_resolutionStack[index - 1], dying);

    private bool HasExactWenhePoisonResponseContinuation()
    {
        if (_modeDefinition.WenheActivity is null || ActiveDying is not { } dying) return false;
        var dyingIndex = _resolutionStack.FindIndex(frame => frame.Id == dying.Id);
        if (dyingIndex < 1 || _resolutionStack[dyingIndex - 1] is not CardsMovedTriggerWindowFrame movement ||
            !IsWenhePoisonDyingChild(movement, dying)) return false;
        AssertWenhePoisonMovementWindow(movement);
        var receipt = movement.WenhePoisonLoss!;
        if (receipt.HpBefore != 1 || receipt.HpAfter != 0 || receipt.Cursor >= movement.Batch.WenhePoisonDepartures!.Count) return false;
        var ownerIndex = dyingIndex - 2;
        if (ownerIndex < 0) return movement.Batch.ParentFrameId is null ||
            ProgramEventHistory<CardUseFinishedEvent>().Any(e => e.ResolutionId == movement.Batch.ParentFrameId);
        var owner = _resolutionStack[ownerIndex];
        if (owner is ProgramSkillFrame program) return ProgramMovementReturnMatches(program, movement);
        if (owner is CardUseFrame use)
            return IsPaidCardUseMovementReturn(use, movement) || IsWenhePoisonFireAttackMovement(use, movement) ||
                IsWenhePoisonColorFireAttackMovement(use, movement);
        return ResponseCompletionStructuralEdge(owner, movement) || CardSupplyCompletionStructuralEdge(owner, movement);
    }

    private bool IsWenhePoisonColorFireAttackMovement(CardUseFrame use, CardsMovedTriggerWindowFrame movement) =>
        use.CardKind == CardKind.FireAttack && use.ColorFireAttack is { PaidCardId: { } cost, PaidFrom: { } from } p &&
        use.Action?.ActionId == p.ActionId && p.Source.OwnerSeat == use.SourceSeat && from == CardLocation.Hand(use.SourceSeat) &&
        p.PaidIsRed is not null && p.PaidIsRed == p.RevealedIsRed && p.PaymentEndSequence > p.PaymentStartSequence &&
        movement.ResumeColorFireAttackFrameId == use.Id && movement.Batch.ParentFrameId == use.Id &&
        movement.ResumeProgramFrameId is null && movement.Batch.AwaitingProgramFrameId is null &&
        movement.Batch.Movements.Count == 1 && movement.Batch.Movements[0] is { } move &&
        move.CardId == cost && move.From == from && move.To == CardLocation.DiscardPile &&
        move.Reason == CardMoveReasons.FireAttackDiscard && move.Disclosure == CardMovementDisclosure.FaceUp &&
        move.Sequence > p.PaymentStartSequence && move.Sequence <= p.PaymentEndSequence &&
        movement.Batch.WenhePoisonDepartures?.Any(d => d.CardId == cost && d.MovementSequence == move.Sequence) == true;

    private bool IsOriginalDyingSuspendedByWenhePoison(DyingFrame original)
    {
        var index = _resolutionStack.FindIndex(frame => frame.Id == original.Id);
        if (index < 0 || index + 1 >= _resolutionStack.Count) return false;
        var response = _resolutionStack[index + 1];
        var actualResponse = response is CardUseFrame { DyingResponse: { } use } && use.ResolutionId == original.Id ||
            response is ProgramSkillFrame { WindowContext: { } program } && program.ParentFrameId == original.Id &&
                program.Window is SkillProgramTriggerWindow.DyingResponse or SkillProgramTriggerWindow.SelfDyingResponse ||
            response is ProgramLifecycleTriggerWindowFrame { ResumeDyingFrameId: { } entry } && entry == original.Id;
        if (!actualResponse) return false;
        for (var at = index + 2; at < _resolutionStack.Count; at++)
        {
            if (_resolutionStack[at] is not CardsMovedTriggerWindowFrame poison || !IsPendingWenhePoisonWindow(poison) ||
                poison.Batch.ParentFrameId is not { } parent) continue;
            var parentIndex = _resolutionStack.FindIndex(frame => frame.Id == parent);
            if (parentIndex > index && parentIndex < at) return true;
        }
        return false;
    }

    private bool TryAdvanceWenhePoisonLoss(CardsMovedTriggerWindowFrame supplied)
    {
        if (!HasWenhePoisonDepartures(supplied.Batch)) return false;
        if (_resolutionStack.LastOrDefault() is not CardsMovedTriggerWindowFrame frame || frame.Id != supplied.Id ||
            _pendingDecision is not null) throw new InvalidOperationException("Poison requires its exact active movement window.");
        AssertWenhePoisonMovementWindow(frame);
        if (frame.WenhePoisonLoss is null)
            ReplaceRuntimeTop(frame = frame with { WenhePoisonLoss = new(0, WenhePoisonLossStage.Ready) });
        while (frame.WenhePoisonLoss!.Cursor < frame.Batch.WenhePoisonDepartures!.Count)
        {
            var receipt = frame.WenhePoisonLoss;
            var departure = frame.Batch.WenhePoisonDepartures[receipt.Cursor];
            if (receipt.Stage == WenhePoisonLossStage.Dying)
                throw new InvalidOperationException("Poison cannot resume before its precise dying child returns.");
            if (receipt.Stage == WenhePoisonLossStage.Ready)
            {
                var owner = _players[departure.OwnerSeat];
                var before = owner.Hp;
                var after = owner.IsAlive ? Math.Max(0, before - 1) : before;
                ReplaceRuntimeTop(frame = frame with { WenhePoisonLoss = receipt with
                { Stage = WenhePoisonLossStage.HealthChildren, HpBefore = before, HpAfter = after } });
                owner.Hp = after;
                RecordHpChange(frame.Id, null, owner.Seat, before, after, HpChangeKind.Loss);
                AdvanceEventRulesAndQueueFact(new WenhePoisonLossAppliedEvent(frame.Id, frame.Batch.Id,
                    departure.RoundNumber, departure.MovementSequence, owner.Seat, departure.CardId, before, after));
                if (owner.IsAlive && before > 0 && after == 0)
                {
                    BeginWenhePoisonDying(frame, departure);
                    return true;
                }
            }
            frame = (CardsMovedTriggerWindowFrame)_resolutionStack.Last();
            if (TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.WenhePoison)) return true;
            receipt = frame.WenhePoisonLoss!;
            AdvanceEventRulesAndQueueFact(new WenhePoisonDepartureSettledEvent(frame.Id, frame.Batch.Id,
                departure.MovementSequence, departure.OwnerSeat, departure.CardId));
            var cursor = receipt.Cursor + 1;
            ReplaceRuntimeTop(frame = frame with { WenhePoisonLoss = new(cursor,
                cursor == frame.Batch.WenhePoisonDepartures!.Count ? WenhePoisonLossStage.Completed : WenhePoisonLossStage.Ready) });
        }
        return false;
    }

    private void BeginWenhePoisonDying(CardsMovedTriggerWindowFrame frame, WenhePoisonDeparture departure)
    {
        var id = ++_resolutionSequence;
        ReplaceRuntimeTop(frame with { WenhePoisonLoss = frame.WenhePoisonLoss! with
        { Stage = WenhePoisonLossStage.Dying, DyingFrameId = id } });
        PushRuntimeFrame(new DyingFrame(id, frame.Id, departure.OwnerSeat, null,
            Array.AsReadOnly(BuildDyingResponderSeats(departure.OwnerSeat).ToArray()), 0, DyingContinuationKind.WenhePoison));
        AdvanceEventRulesAndQueueFact(new PlayerDyingEvent(id, departure.OwnerSeat, null));
        _status = EngineStatus.Running;
        if (!TryBeginMandatorySelfDyingProgram(ActiveDying!) && !TryBeginDyingEntryProgramWindow(ActiveDying!))
            ExposeHumanDyingPrompt();
    }

    private void ReturnWenhePoisonDying(DyingCompletionReceipt returned)
    {
        if (_resolutionStack.LastOrDefault() is not CardsMovedTriggerWindowFrame frame ||
            frame.Id != returned.ParentFrameId || returned.Continuation != DyingContinuationKind.WenhePoison ||
            frame.WenhePoisonLoss is not { Stage: WenhePoisonLossStage.Dying } receipt ||
            receipt.DyingFrameId != returned.FrameId || frame.Batch.WenhePoisonDepartures![receipt.Cursor].OwnerSeat != returned.VictimSeat)
            throw new InvalidOperationException("Poison dying returned to a different paid departure.");
        ReplaceRuntimeTop(frame with { WenhePoisonLoss = receipt with
        { Stage = WenhePoisonLossStage.HealthChildren, DyingFrameId = null } });
        AdvanceRuntimeFrame(frame.Id);
    }

    private void AssertWenhePoisonMovementWindow(CardsMovedTriggerWindowFrame frame)
    {
        if (frame.Batch.WenhePoisonDepartures is not { } departures)
        {
            if (frame.WenhePoisonLoss is not null) throw new InvalidOperationException("Poison lost its departure list.");
            return;
        }
        if (_modeDefinition.WenheActivity is null || departures.Count == 0 ||
            departures is not System.Collections.IList { IsReadOnly: true } ||
            departures.Select(d => d.MovementSequence).Distinct().Count() != departures.Count ||
            !departures.Select(d => d.MovementSequence).SequenceEqual(departures.Select(d => d.MovementSequence).Order()) ||
            departures.Any(d => d.RoundNumber < 1 || !IsValidPlayerSeat(d.OwnerSeat) ||
                frame.Batch.Movements.Count(move => move.Sequence == d.MovementSequence && move.CardId == d.CardId &&
                    GetAttackCard(move.CardId).Kind == CardKind.Peach && move.Disclosure == CardMovementDisclosure.FaceUp &&
                    move.From == CardLocation.Hand(d.OwnerSeat) && move.To != move.From) != 1 ||
                !ProgramEventHistory<WenheRoundRuleChangedEvent>().Any(rule => rule.RoundNumber == d.RoundNumber && rule.Rule == WenheRuleKind.Poison)))
            throw new InvalidOperationException("Poison changed its frozen public physical departure proof.");
        if (frame.WenhePoisonLoss is not { } receipt) return;
        if (!Enum.IsDefined(receipt.Stage) || receipt.Cursor < 0 || receipt.Cursor > departures.Count ||
            (receipt.Stage == WenhePoisonLossStage.Completed) != (receipt.Cursor == departures.Count) ||
            (receipt.Stage == WenhePoisonLossStage.Dying) != (receipt.DyingFrameId is not null))
            throw new InvalidOperationException("Poison changed its serial departure cursor or native child.");
        var applied = ProgramEventHistory<WenhePoisonLossAppliedEvent>().Where(e => e.WindowFrameId == frame.Id).ToArray();
        var settled = ProgramEventHistory<WenhePoisonDepartureSettledEvent>().Where(e => e.WindowFrameId == frame.Id).ToArray();
        var activeIssued = receipt.Stage is WenhePoisonLossStage.HealthChildren or WenhePoisonLossStage.Dying ? 1 : 0;
        if (applied.Length != receipt.Cursor + activeIssued || settled.Length != receipt.Cursor ||
            applied.Where((e, i) => e.BatchId != frame.Batch.Id || e.RoundNumber != departures[i].RoundNumber ||
                e.MovementSequence != departures[i].MovementSequence || e.OwnerSeat != departures[i].OwnerSeat || e.CardId != departures[i].CardId ||
                e.HpBefore < 0 || e.HpAfter < 0 || e.HpBefore - e.HpAfter is < 0 or > 1).Any() ||
            settled.Where((e, i) => e.BatchId != frame.Batch.Id || e.MovementSequence != departures[i].MovementSequence ||
                e.OwnerSeat != departures[i].OwnerSeat || e.CardId != departures[i].CardId).Any() ||
            activeIssued != 0 && (applied[^1].HpBefore != receipt.HpBefore || applied[^1].HpAfter != receipt.HpAfter))
            throw new InvalidOperationException("Poison lost an exact once-issued HP loss or departure return.");
    }

    private bool IsWenhePoisonHealthChild(ResolutionFrame? parent, HpChangedTriggerWindowFrame child) =>
        parent is CardsMovedTriggerWindowFrame { WenhePoisonLoss: { Stage: WenhePoisonLossStage.HealthChildren } receipt } frame &&
        frame.Id == child.ResumeFrameId && child.Continuation == PostEventContinuation.WenhePoison &&
        child.Change.ParentFrameId == frame.Id && child.Change.TargetSeat == frame.Batch.WenhePoisonDepartures![receipt.Cursor].OwnerSeat &&
        child.Change.Kind == HpChangeKind.Loss && child.Change.HpBefore == receipt.HpBefore && child.Change.HpAfter == receipt.HpAfter;

    private bool IsWenhePoisonDyingChild(ResolutionFrame? parent, DyingFrame child) =>
        parent is CardsMovedTriggerWindowFrame { WenhePoisonLoss: { Stage: WenhePoisonLossStage.Dying } receipt } frame &&
        child.ParentFrameId == frame.Id && child.Id == receipt.DyingFrameId && child.KillerSeat is null &&
        child.Continuation == DyingContinuationKind.WenhePoison &&
        child.VictimSeat == frame.Batch.WenhePoisonDepartures![receipt.Cursor].OwnerSeat && receipt.HpAfter == 0;
}
