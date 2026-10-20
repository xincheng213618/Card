namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_bian_fu_ren_ol_BianFuRenMovements
{
    internal static bool IsBianFuRenOwnedZone(CardLocation from) => from.OwnerSeat is not null &&
        from.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain;

    internal static bool BianFuRenNativeUseFinish(CardMovementRecord movement) =>
        movement.From == CardLocation.Processing && movement.To == CardLocation.DiscardPile &&
        (movement.Reason == CardMoveReasons.UseFinished || movement.Reason == CardMoveReasons.IronChainFinished);

    internal static bool BianFuRenNativeUsePayment(CardMovementRecord movement) =>
        // Eligibility comes from the typed producer captured on its owning use,
        // never from a later guess based on the reason string or current source.
        movement.To == CardLocation.Processing;
}
