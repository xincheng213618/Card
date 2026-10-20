namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_CardUsePayments
{
    // This producer already owns payment children on its exact Program return
    // after the whole Use retires. A generic payment tail must not take them.
    internal static bool OwnsGenericSlashPaymentTail(CardUseFrame use) =>
        use.ForeignSelectedCardSlashReturn is null;

    internal static bool CardUsePaymentBatchMatchesReceipt(CardUseFrame use, CardMovementBatchContext batch,
        CardUsePaymentMovementReceipt? issuedReceipt = null)
    {
        if (use.Action is not { } action || (issuedReceipt ?? use.PaymentMovementReceipt) is not { } receipt ||
            receipt.OwnerFrameId != use.Id || receipt.ActionId != action.ActionId || receipt.SequenceBefore < 0 ||
            receipt.SequenceAfter <= receipt.SequenceBefore || batch.Movements.Count == 0 ||
            receipt.SequenceBefore != batch.Movements[0].Sequence - 1 ||
            receipt.SequenceAfter != batch.Movements[^1].Sequence || receipt.Batch is not { } issued) return false;
        return batch.Id == issued.Id && batch.ParentFrameId == issued.ParentFrameId && batch.ParentBatchId == issued.ParentBatchId &&
            batch.TurnNumber == issued.TurnNumber && batch.AwaitingProgramFrameId == issued.AwaitingProgramFrameId &&
            batch.OriginSkillId == issued.OriginSkillId && batch.OriginSkillInstanceId == issued.OriginSkillInstanceId &&
            batch.OriginOwnerSeat == issued.OriginOwnerSeat && batch.MovementTiming == issued.MovementTiming &&
            batch.DiscardRecoveryPhase == issued.DiscardRecoveryPhase && batch.NativeDrawInvocation == issued.NativeDrawInvocation &&
            batch.NativeBuZhiDiscardInvocation == issued.NativeBuZhiDiscardInvocation &&
            batch.Movements.SequenceEqual(issued.Movements) && batch.SourceCounts.SequenceEqual(issued.SourceCounts) &&
            ((batch.DestinationCounts is null && issued.DestinationCounts is null) ||
             batch.DestinationCounts is { } destinations && issued.DestinationCounts is { } issuedDestinations &&
             destinations.SequenceEqual(issuedDestinations));
    }
}
