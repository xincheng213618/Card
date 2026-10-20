using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed partial record RecoveryPaidCardUseContinuation
{
    private readonly IReadOnlyList<CardUsePaymentMovementReceipt>? _nativeOriginalEquipmentMovements;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardUsePaymentMovementReceipt>? NativeOriginalEquipmentMovements
    {
        get => _nativeOriginalEquipmentMovements;
        init => _nativeOriginalEquipmentMovements = value is null ? null : Array.AsReadOnly(value.Select(receipt =>
            receipt with { Batch = receipt.Batch }).ToArray());
    }
}

public sealed partial class GameEngine
{
    private bool IsNativePublicPileEquipmentOwner(CardUseFrame use, RecoveryPaidCardUseContinuation continuation) =>
        continuation.Kind == RecoveryPaidCardUseKind.EquipmentUse && continuation.SourceSeat == use.SourceSeat &&
        continuation.CardId == use.CardId && continuation.EquipmentSlot is { } slot &&
        CardCatalog.Get(use.CardKind).CategoryName == "装备牌" && EquipmentCatalog.Get(use.CardKind).Slot == slot &&
        use.NativePublicPilePayment is { SequenceAfter: > 0 } payment && payment.OwnerFrameId == use.Id &&
        use.Action is { Type: CardActionType.Use, PhysicalCards: [var cost] } action &&
        payment.ActionId == action.ActionId && cost.CardId == use.CardId &&
        cost.CardKind == use.CardKind && payment.Card.Id == cost.CardId &&
        IsNativePublicPileOriginalActionCost(action, cost);

    [ProgramCardUsePayment(ProgramCardUsePaymentStage.LeadingTail, 10)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.HpObserverTail, 10)]
    private bool HasNativePublicPileEquipmentTail(CardUseFrame use, RecoveryPaidCardUseContinuation continuation) =>
        continuation.NativeOriginalEquipmentMovements is not null && IsNativePublicPileEquipmentOwner(use, continuation);

    private RecoveryPaidCardUseContinuation FreezeNativePublicPileEquipmentChildren(CardUseFrame use,
        RecoveryPaidCardUseContinuation continuation)
    {
        if (!IsNativePublicPileEquipmentOwner(use, continuation)) return continuation;
        if (continuation.NativeOriginalEquipmentMovements is not null)
            throw new InvalidOperationException("An original equipment use cannot issue its replacement children twice.");
        var candidates = _pendingCardsMovedBatches.Where(batch => batch.ParentFrameId == use.Id &&
            batch.AwaitingProgramFrameId is null && batch.NativeDrawInvocation is null &&
            batch.NativeBuZhiDiscardInvocation is null && batch.Movements.Count > 0).ToArray();
        var batches = candidates.Where(batch => NativePublicPileEquipmentMovementsMatch(use, continuation, batch, candidates))
            .OrderBy(batch => batch.Id).ToArray();
        return continuation with
        {
            NativeOriginalEquipmentMovements = batches.Select(batch => new CardUsePaymentMovementReceipt(use.Id,
                use.Action!.ActionId, batch.Movements[0].Sequence - 1, batch.Movements[^1].Sequence) { Batch = batch }).ToArray()
        };
    }

    private bool NativePublicPileEquipmentMovementsMatch(CardUseFrame use, RecoveryPaidCardUseContinuation continuation,
        CardMovementBatchContext batch, IReadOnlyList<CardMovementBatchContext> issued)
    {
        if (!IsNativePublicPileEquipmentOwner(use, continuation) || batch.ParentFrameId != use.Id ||
            batch.AwaitingProgramFrameId is not null || batch.NativeDrawInvocation is not null ||
            batch.NativeBuZhiDiscardInvocation is not null || batch.Movements.Count == 0 ||
            batch.TurnNumber != use.NativePublicPilePayment!.ActualTurn) return false;
        var payment = use.NativePublicPilePayment!;
        bool Replacement(CardMovementRecord move) => continuation.ReplacedCardId == move.CardId &&
            move.From == CardLocation.Equipment(use.SourceSeat) && move.Reason == CardMoveReasons.EquipmentReplace &&
            move.Sequence > payment.SequenceAfter &&
            (move.To == CardLocation.DiscardPile || move.To == CardLocation.OutsideGame) &&
            EquipmentCatalog.Get(move.CardKind).Slot == continuation.EquipmentSlot;
        return batch.Movements.All(move => move.Sequence > 0 && move.Sequence <= _cardMovements.Count &&
            _cardMovements[move.Sequence - 1] == move && move.TurnNumber == batch.TurnNumber &&
            (move.Sequence == payment.SequenceAfter && move.CardId == use.CardId &&
             move.CardKind == use.CardKind && move.From == payment.Pile.Location &&
             move.To == CardLocation.Processing && move.Reason == CardMoveReasons.EquipmentUse && batch.ParentBatchId is null ||
             Replacement(move) && batch.ParentBatchId is null ||
             move.From == CardLocation.WoodenOxGrain(use.SourceSeat) && move.To == CardLocation.DiscardPile &&
             move.Reason == CardMoveReasons.WoodenOxGrainDiscard && move.Sequence > payment.SequenceAfter &&
             batch.ParentBatchId is { } parent && issued.Any(removal => removal.Id == parent &&
                 removal.ParentFrameId == use.Id && removal.ParentBatchId is null &&
                 removal.Movements is [var old] && old.CardKind == CardKind.WoodenOx && Replacement(old))));
    }

    [ProgramCardUsePayment(ProgramCardUsePaymentStage.MatchBatch, 10)]
    private bool IsNativePublicPileEquipmentBatch(CardUseFrame use, CardMovementBatchContext batch)
    {
        if (use.RecoveryPaidContinuation is not { NativeOriginalEquipmentMovements: { } receipts } continuation ||
            !HasNativePublicPileEquipmentTail(use, continuation)) return false;
        var issued = receipts.SingleOrDefault(receipt => receipt.Batch.Id == batch.Id);
        return issued is not null && CardUsePaymentBatchMatchesReceipt(use, batch, issued) &&
            NativePublicPileEquipmentMovementsMatch(use, continuation, batch, receipts.Select(receipt => receipt.Batch).ToArray());
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 27589)]
    private void AssertNativePublicPileEquipmentChildren()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>())
        {
            if (use.RecoveryPaidContinuation is not { NativeOriginalEquipmentMovements: { } receipts } continuation) continue;
            if (!HasNativePublicPileEquipmentTail(use, continuation) ||
                receipts is not System.Collections.IList { IsReadOnly: true } ||
                receipts.Select(receipt => receipt.Batch.Id).Distinct().Count() != receipts.Count ||
                receipts.Any(receipt => !IsNativePublicPileEquipmentBatch(use, receipt.Batch) ||
                    receipt.Batch.Movements is not System.Collections.IList { IsReadOnly: true } ||
                    receipt.Batch.SourceCounts is not System.Collections.IList { IsReadOnly: true } ||
                    receipt.Batch.DestinationCounts is { } counts && counts is not System.Collections.IList { IsReadOnly: true }))
                throw new InvalidOperationException("An original equipment use lost its frozen native replacement children.");
        }
    }
}
