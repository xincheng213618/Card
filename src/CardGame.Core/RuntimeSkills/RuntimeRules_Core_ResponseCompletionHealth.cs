namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ResponseCompletionHealth
{
    internal static bool IsResponseCompletionSilverLionAttempt(IReadOnlyList<CardMovementRecord> costs, RecoveryAttempt a) =>
        a.Completion.Producer == RecoveryAttemptProducer.SilverLion && a.Amount == 1 && a.SourceSeat == a.TargetSeat &&
        costs.Any(m => m.CardKind == CardKind.SilverLion && m.From == CardLocation.Equipment(a.TargetSeat) &&
            m.Reason == a.Completion.MoveReason);

    internal static bool IsResponseCompletionSilverLionHealth(IReadOnlyList<CardMovementRecord> costs, HpChangeContext h) =>
        h.Kind == HpChangeKind.Recovery && h.Amount == 1 && h.SourceSeat == h.TargetSeat && h.HpAfter == h.HpBefore + 1 &&
        costs.Any(m => m.CardKind == CardKind.SilverLion && m.From == CardLocation.Equipment(h.TargetSeat));

    internal static bool SameResponseCompletionRecovery(RecoveryAttempt a, RecoveryAttempt b) =>
        a.Id == b.Id && a.SourceSeat == b.SourceSeat && a.TargetSeat == b.TargetSeat && a.Amount == b.Amount && a.HpBefore == b.HpBefore &&
        a.ActualTurnNumber == b.ActualTurnNumber && a.ActualTurnOwnerSeat == b.ActualTurnOwnerSeat && a.Completion == b.Completion &&
        a.Candidates.SequenceEqual(b.Candidates);
}
