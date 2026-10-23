using System.Text.Json.Serialization;

namespace CardGame.Core;

// Trusted physical-movement evidence. No private material is added to public events.
public sealed record HappyWeiYanGainDestinationPlan(
    int CardId, CardKind PrintedKind, CardLocation From, CardLocation RequestedTo,
    CardLocation ActualTo, CardMoveReason Reason, EquipmentSlot Slot,
    bool RecipientEquipmentAreaAbolished, int RecipientSlotCapacity,
    long IssueFrameId, CardConversionSource Source, string GameplayHash,
    int IssueEventIndex, int EventCountBefore, long SequenceBefore);

// A request can be replaced by obtaining the issuer's own hand card. This is a
// producer receipt, not a physical movement and not an actual hand gain.
public sealed record HappyWeiYanRetainedGainSettlement(
    long ProducerFrameId, ResolutionFrameKind ProducerKind, int? InstructionIndex,
    string? ProducerSkillId, string? ProducerBindingId, string? ProducerSkillInstanceId, string? ProducerGameplayHash,
    int Ordinal, HappyWeiYanGainDestinationPlan Plan);

public abstract partial record ResolutionFrame
{
    private readonly IReadOnlyList<HappyWeiYanRetainedGainSettlement>? _happyWeiYanRetainedGains;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<HappyWeiYanRetainedGainSettlement>? HappyWeiYanRetainedGains
    {
        get => _happyWeiYanRetainedGains;
        init => _happyWeiYanRetainedGains = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}

/// <summary>The actual outcome of one original gain request, including retained entities.</summary>
public sealed record HappyWeiYanGainRequestSettlement(long ProducerFrameId, long? MovementBatchId,
    long SequenceBefore, CardMoveReason Reason)
{
    private readonly IReadOnlyList<CardMovementRecord> _movements = Array.AsReadOnly(Array.Empty<CardMovementRecord>());
    private readonly IReadOnlyList<HappyWeiYanRetainedGainSettlement> _retainedGains =
        Array.AsReadOnly(Array.Empty<HappyWeiYanRetainedGainSettlement>());
    public IReadOnlyList<CardMovementRecord> Movements
    { get => _movements; init => _movements = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<HappyWeiYanRetainedGainSettlement> RetainedGains
    { get => _retainedGains; init => _retainedGains = Array.AsReadOnly(value.ToArray()); }
}

public sealed partial record CardMovementRecord
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HappyWeiYanGainDestinationPlan? HappyWeiYanGainDestination { get; init; }
}

public sealed partial class GameEngine
{
    private HappyWeiYanGainDestinationPlan? PlanHappyWeiYanGainDestination(
        Card card, CardLocation from, CardLocation requestedTo, CardMoveReason reason)
    {
        if (!_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveHappyWeiYanGujing)) return null;
        var actualTo = NormalizeHappyWeiYanGainDestination(card, from, requestedTo, reason);
        if (actualTo == requestedTo) return null;
        if (requestedTo is not { Zone: CardZoneKind.Hand, OwnerSeat: { } recipient } ||
            actualTo is not { Zone: CardZoneKind.Hand, OwnerSeat: { } issuer } ||
            !IsValidPlayerSeat(recipient) || !IsValidPlayerSeat(issuer) ||
            !EquipmentCatalog.IsEquipment(card.Kind) || !_players[issuer].IsAlive ||
            _players[issuer].HappyWeiYanGujing is not { } issue)
            throw new InvalidOperationException("A gain replacement lost its original requested and actual hands.");
        var history = CompleteProgramEventHistory().ToArray();
        var fact = new HappyWeiYanGujingIssuedEvent(issue.FrameId, issue.Source, issue.GameplayHash);
        var matches = history.Select((payload, index) => (payload, index)).Where(item => Equals(item.payload, fact)).ToArray();
        var slot = EquipmentCatalog.Get(card.Kind).Slot;
        var capacity = _players[recipient].EquipmentSlotCapacity(slot);
        if (matches is not [var issued] || issue.Source.OwnerSeat != issuer ||
            issue.Source.SkillId != HappyWeiYanComposition.Gujing ||
            _contentRegistry.GetSkill(issue.Source.SkillId).Program?.GameplayHash != issue.GameplayHash ||
            !_players[recipient].EquipmentAreaAbolished && capacity > 0)
            throw new InvalidOperationException("A gain replacement has no unique prior limited issue or abolished receiving slot.");
        return new(card.Id, card.Kind, from, requestedTo, actualTo, reason, slot,
            _players[recipient].EquipmentAreaAbolished, capacity, issue.FrameId, issue.Source,
            issue.GameplayHash, issued.index, history.Length, _movementSequence);
    }

    private void ValidateHappyWeiYanGainDestinationRecord(CardMovementRecord movement)
    {
        if (movement.HappyWeiYanGainDestination is not null &&
            !IsHappyWeiYanRedirectedGain(movement, movement.HappyWeiYanGainDestination.RequestedTo))
            throw new InvalidOperationException("A redirected gain lost its frozen original destination and historical issuer.");
    }

    private bool IsHappyWeiYanRedirectedGain(CardMovementRecord movement, CardLocation expectedTo)
    {
        if (movement.HappyWeiYanGainDestination is not { } plan || plan.RequestedTo != expectedTo ||
            !IsHappyWeiYanGainDestinationPlan(plan) || movement.From == movement.To ||
            movement.CardId != plan.CardId || movement.CardKind != plan.PrintedKind || movement.From != plan.From ||
            movement.To != plan.ActualTo || movement.Reason != plan.Reason ||
            movement.Sequence <= plan.SequenceBefore ||
            _cardMovements.Count(actual => actual.Sequence == movement.Sequence && actual == movement) != 1)
            return false;
        return true;
    }

    private bool IsHappyWeiYanGainDestinationPlan(HappyWeiYanGainDestinationPlan plan)
    {
        if (plan.RequestedTo is not { Zone: CardZoneKind.Hand, OwnerSeat: { } recipient } ||
            plan.ActualTo is not { Zone: CardZoneKind.Hand, OwnerSeat: { } issuer } ||
            !IsValidPlayerSeat(recipient) || !IsValidPlayerSeat(issuer) || recipient == issuer ||
            plan.SequenceBefore < 0 || plan.SequenceBefore > _movementSequence ||
            !EquipmentCatalog.IsEquipment(plan.PrintedKind) || EquipmentCatalog.Get(plan.PrintedKind).Slot != plan.Slot ||
            !plan.RecipientEquipmentAreaAbolished && plan.RecipientSlotCapacity > 0 ||
            plan.IssueFrameId <= 0 || plan.Source.OwnerSeat != issuer || plan.Source.SkillId != HappyWeiYanComposition.Gujing ||
            _contentRegistry.GetSkill(plan.Source.SkillId).Program?.GameplayHash != plan.GameplayHash)
            return false;
        var history = CompleteProgramEventHistory().ToArray();
        var issued = new HappyWeiYanGujingIssuedEvent(plan.IssueFrameId, plan.Source, plan.GameplayHash);
        return plan.IssueEventIndex >= 0 && plan.IssueEventIndex < plan.EventCountBefore &&
            plan.EventCountBefore <= history.Length && Equals(history[plan.IssueEventIndex], issued) &&
            history.Count(payload => Equals(payload, issued)) == 1;
    }

    private bool TrySettleHappyWeiYanRetainedGain(Card card, CardLocation from, CardLocation requestedTo,
        CardMoveReason reason, long? producerFrameId = null)
    {
        var plan = PlanHappyWeiYanGainDestination(card, from, requestedTo, reason);
        return plan is not null && TrySettleHappyWeiYanRetainedGain(plan, producerFrameId);
    }

    private bool TrySettleHappyWeiYanRetainedGain(HappyWeiYanGainDestinationPlan plan,
        long? producerFrameId = null)
    {
        if (plan.ActualTo != plan.From) return false;
        SettleHappyWeiYanRetainedGain(plan, producerFrameId);
        return true;
    }

    private HappyWeiYanRetainedGainSettlement SettleHappyWeiYanRetainedGain(
        HappyWeiYanGainDestinationPlan plan, long? producerFrameId = null)
    {
        var producer = producerFrameId is { } id
            ? _resolutionStack.SingleOrDefault(frame => frame.Id == id) : _resolutionStack.LastOrDefault();
        if (producer is null || producer.Step == ResolutionFrameStep.Completed ||
            plan.From != plan.ActualTo || plan.From.Zone != CardZoneKind.Hand ||
            plan.SequenceBefore != _movementSequence || !IsHappyWeiYanGainDestinationPlan(plan) ||
            !_cardZones.CardsAt(plan.From).Any(card => card.Id == plan.CardId && card.Kind == plan.PrintedKind))
            throw new InvalidOperationException("A retained gain requires its original live producer and unchanged physical hand entity.");
        var program = producer as ProgramSkillFrame;
        var previous = producer.HappyWeiYanRetainedGains ?? [];
        var settlement = new HappyWeiYanRetainedGainSettlement(producer.Id, producer.Kind,
            program?.InstructionIndex, program?.SkillId, program?.ActivationId, program?.SkillInstanceId, program?.GameplayHash,
            previous.Count, plan);
        if (previous.Any(item => item.InstructionIndex == settlement.InstructionIndex && item.Plan == plan))
            throw new InvalidOperationException("The original gain request was already settled without a movement.");
        ReplaceRuntimeFrame(producer.Id, producer with { HappyWeiYanRetainedGains = previous.Append(settlement).ToArray() });
        return settlement;
    }

    private bool IsHappyWeiYanRetainedGainSettlement(HappyWeiYanRetainedGainSettlement settlement,
        long producerFrameId, int cardId, CardLocation requestedTo, long before)
    {
        var producer = _resolutionStack.SingleOrDefault(frame => frame.Id == producerFrameId);
        if (producer is null || settlement.ProducerFrameId != producer.Id || settlement.ProducerKind != producer.Kind ||
            settlement.Ordinal < 0 || producer.HappyWeiYanRetainedGains is not { } receipts ||
            settlement.Ordinal >= receipts.Count || receipts[settlement.Ordinal] != settlement ||
            receipts.Count(item => item == settlement) != 1 ||
            settlement.Plan.CardId != cardId || settlement.Plan.RequestedTo != requestedTo ||
            settlement.Plan.SequenceBefore < before || settlement.Plan.From != settlement.Plan.ActualTo ||
            settlement.Plan.From.Zone != CardZoneKind.Hand || !IsHappyWeiYanGainDestinationPlan(settlement.Plan))
            return false;
        return producer is ProgramSkillFrame program
            ? settlement.InstructionIndex is { } instruction && instruction >= 0 && instruction <= program.InstructionIndex &&
                settlement.ProducerSkillId == program.SkillId && settlement.ProducerBindingId == program.ActivationId &&
                settlement.ProducerSkillInstanceId == program.SkillInstanceId &&
                settlement.ProducerGameplayHash == program.GameplayHash
            : settlement.InstructionIndex is null && settlement.ProducerSkillId is null && settlement.ProducerBindingId is null &&
                settlement.ProducerSkillInstanceId is null && settlement.ProducerGameplayHash is null;
    }

    private void ValidateHappyWeiYanGainRequestSettlement(HappyWeiYanGainRequestSettlement settlement,
        long producerFrameId, IReadOnlyList<int> intendedIds, CardLocation requestedTo, long before)
    {
        var actual = settlement.Movements;
        var retained = settlement.RetainedGains;
        if (settlement.ProducerFrameId != producerFrameId || settlement.SequenceBefore != before ||
            before < 0 || before > _movementSequence || intendedIds.Count == 0 ||
            intendedIds.Distinct().Count() != intendedIds.Count ||
            (actual.Count == 0 ? settlement.MovementBatchId is not null : settlement.MovementBatchId is not > 0) ||
            actual.Select(move => move.CardId).Concat(retained.Select(receipt => receipt.Plan.CardId))
                .Order().SequenceEqual(intendedIds.Order()) is false ||
            actual.Any(move => move.Sequence <= before || move.Reason != settlement.Reason ||
                move.From == move.To || !_cardMovements.Contains(move) ||
                move.To != requestedTo && move.To != CardLocation.OutsideGame && !IsHappyWeiYanRedirectedGain(move, requestedTo)) ||
            retained.Any(receipt => receipt.Plan.SequenceBefore != before || receipt.Plan.Reason != settlement.Reason ||
                !IsHappyWeiYanRetainedGainSettlement(receipt, producerFrameId, receipt.Plan.CardId, requestedTo, before)))
            throw new InvalidOperationException("A gain request lost its complete actual-movement and retained-entity settlement.");
    }

    private void MoveNativeGivenCards(long producerFrameId, int giver, int recipient,
        IReadOnlyList<int> cardIds, CardMoveReason reason,
        Action<HappyWeiYanGainRequestSettlement>? afterRequestSettled = null,
        CardMovementDisclosure disclosure = CardMovementDisclosure.Unspecified)
    {
        var before = _movementSequence;
        MoveProgramCardsFromMultipleSources(cardIds, CardLocation.Hand(recipient), reason,
            disclosure: disclosure, afterRequestSettled: NativeGiveRequestCompletion(
                producerFrameId, giver, recipient, cardIds, before, afterRequestSettled),
            producerFrameId: producerFrameId);
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 42306)]
    private void AssertHappyWeiYanRetainedProgramGains(ProgramSkillFrame frame)
    {
        AssertHappyWeiYanRetainedGainSettlements(frame);
        if (frame.SelectedCardPayment is { GainRequestSettlement: { } settlement } payment)
        {
            if (payment.Operation != SkillProgramEffectOp.GiveSelected ||
                settlement.RetainedGains.Any(receipt => receipt.InstructionIndex != payment.InstructionIndex))
                throw new InvalidOperationException("A selected-card gain settlement lost its exact paid Give instruction.");
            ValidateHappyWeiYanGainRequestSettlement(settlement, frame.Id, payment.CardIds,
                CardLocation.Hand(payment.RecipientSeat), settlement.SequenceBefore);
        }
    }

    private void AssertHappyWeiYanRetainedGainSettlements(ResolutionFrame frame)
    {
        if (frame.HappyWeiYanRetainedGains is not { } receipts) return;
        if (receipts.Count == 0 || receipts.Where((receipt, ordinal) => receipt.Ordinal != ordinal ||
            !IsHappyWeiYanRetainedGainSettlement(receipt, frame.Id, receipt.Plan.CardId,
                receipt.Plan.RequestedTo, 0)).Any())
            throw new InvalidOperationException("A retained gain lost its original producer or frozen historical destination replacement.");
    }
}
