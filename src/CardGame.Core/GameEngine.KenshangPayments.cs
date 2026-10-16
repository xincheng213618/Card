namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsKenshangRemovedMaterial(long useId, int cardId)
    {
        if (LifecycleCardUse(useId) is not { Kenshang: { } receipt, Action: { } action,
                PaymentMovementReceipt: { } payment } use ||
            !KenshangUseProof(use) ||
            action.PhysicalCards.SingleOrDefault(cost => cost.CardId == cardId) is not
                { CardKind: CardKind.GeneralWeapon } material ||
            material.From != CardLocation.Equipment(receipt.ProviderSeat) ||
            !_cardZones.Contains(CardLocation.OutsideGame, cardId) ||
            !IsCardUsePaymentBatch(use, payment.Batch)) return false;

        // General weapons leave the game when their equipped entity is paid.
        // Only this Use's frozen native payment batch can authorize that zone.
        return payment.Batch.Movements.Count(move => move.CardId == cardId &&
            move.CardKind == material.CardKind && move.From == material.From &&
            move.To == CardLocation.OutsideGame && move.Reason == CardMoveReasons.Use) == 1;
    }
}
