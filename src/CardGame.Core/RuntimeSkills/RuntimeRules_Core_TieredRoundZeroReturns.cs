namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_TieredRoundZeroReturns
{
    // CardActionContext is a class; independent JSON fields reconstruct distinct
    // objects. Only this new receipt's return proof compares the complete frozen
    // value. Existing default/reference semantics remain unchanged.
    internal static bool TieredRoundActionsStructurallyMatch(CardActionContext? left, CardActionContext right) =>
        left is not null && left.ActionId == right.ActionId && left.ParentActionId == right.ParentActionId &&
        left.Type == right.Type && left.ActorSeat == right.ActorSeat && left.ProviderSeat == right.ProviderSeat &&
        left.RequesterSeat == right.RequesterSeat && left.ResponderSeat == right.ResponderSeat && left.OpponentSeat == right.OpponentSeat &&
        left.EffectiveKind == right.EffectiveKind && left.EffectiveSuit == right.EffectiveSuit && left.EffectiveRank == right.EffectiveRank &&
        left.EffectiveIsRed == right.EffectiveIsRed && left.FactionOrigin == right.FactionOrigin &&
        left.TargetSeats.SequenceEqual(right.TargetSeats) &&
        (left.DesignatedTargetSeats is null ? right.DesignatedTargetSeats is null :
            right.DesignatedTargetSeats is not null && left.DesignatedTargetSeats.SequenceEqual(right.DesignatedTargetSeats)) &&
        left.PhysicalCards.SequenceEqual(right.PhysicalCards) && left.ConversionChain.SequenceEqual(right.ConversionChain);
}
