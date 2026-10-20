namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_OverflowTargetCancellationObservers
{
    internal static bool SameOverflowAction(CardActionContext a, CardActionContext b) =>
        a.ActionId == b.ActionId && a.ParentActionId == b.ParentActionId && a.Type == b.Type &&
        a.ActorSeat == b.ActorSeat && a.ProviderSeat == b.ProviderSeat && a.RequesterSeat == b.RequesterSeat &&
        a.ResponderSeat == b.ResponderSeat && a.OpponentSeat == b.OpponentSeat && a.EffectiveKind == b.EffectiveKind &&
        a.EffectiveSuit == b.EffectiveSuit && a.EffectiveRank == b.EffectiveRank && a.EffectiveIsRed == b.EffectiveIsRed &&
        a.FactionOrigin == b.FactionOrigin && a.TargetSeats.SequenceEqual(b.TargetSeats) &&
        a.EffectiveDesignatedTargetSeats.SequenceEqual(b.EffectiveDesignatedTargetSeats) &&
        a.PhysicalCards.SequenceEqual(b.PhysicalCards) && a.ConversionChain.SequenceEqual(b.ConversionChain);

    internal static bool SameOverflowIssuance(OverflowTargetCancellationReceipt a, OverflowTargetCancellationReceipt b) =>
        a.InstructionIndex == b.InstructionIndex && a.Source == b.Source && a.GameplayHash == b.GameplayHash &&
        a.WindowFrameId == b.WindowFrameId && a.CardUseFrameId == b.CardUseFrameId && a.ActionId == b.ActionId &&
        a.ProviderSeat == b.ProviderSeat && a.EffectiveKind == b.EffectiveKind && CardGame.Core.RuntimeSkills.RuntimeRules_Core_OverflowTargetCancellationObservers.SameOverflowAction(a.ActionBeforeCancellation, b.ActionBeforeCancellation) &&
        a.HandCount == b.HandCount && a.HandLimit == b.HandLimit && a.RecipientSeat == b.RecipientSeat && a.SequenceBefore == b.SequenceBefore &&
        a.OriginalTargetSeats.SequenceEqual(b.OriginalTargetSeats) && a.BeforeTargetSeats.SequenceEqual(b.BeforeTargetSeats) &&
        a.CanceledPrimaryTargetSeats.SequenceEqual(b.CanceledPrimaryTargetSeats) && a.ResultTargetSeats.SequenceEqual(b.ResultTargetSeats) &&
        a.MaterialCardIds.SequenceEqual(b.MaterialCardIds);
}
