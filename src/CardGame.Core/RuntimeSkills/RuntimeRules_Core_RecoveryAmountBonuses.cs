using System.Text.Json.Serialization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_RecoveryAmountBonuses
{
    // A recipient's prefix is anchored to the real TurnStarted/TurnEnded segment.
    // It counts every positive recovery, including recovery before acquiring a policy.
    internal static int? RecoveryOrdinalAt(IReadOnlyList<IGameEvent> history, int endExclusive,
        int targetSeat, int turnNumber, int turnOwnerSeat)
    {
        if (turnNumber < 1) return null;
        var start = endExclusive - 1;
        while (start >= 0 && history[start] is not TurnStartedEvent) start--;
        if (start < 0) return null;
        var started = (TurnStartedEvent)history[start];
        for (var i = start + 1; i < endExclusive; i++)
            if (history[i] is TurnEndedEvent ended && ended.TurnNumber == started.TurnNumber && ended.ActorSeat == started.ActorSeat)
                return null;
        if (started.TurnNumber != turnNumber || started.ActorSeat != turnOwnerSeat)
            throw new InvalidOperationException("A recovery ordinal lost its actual turn boundary.");
        var ids = new HashSet<long>();
        var ordinal = 1;
        for (var i = start + 1; i < endExclusive; i++)
        {
            if (history[i] is not ActualTurnRecoveryRecordedEvent prior || prior.TargetSeat != targetSeat) continue;
            if (prior.TurnNumber != turnNumber || prior.TurnOwnerSeat != turnOwnerSeat || prior.Ordinal != ordinal ||
                prior.ActualAmount <= 0 || prior.ActualAmount != prior.HpAfter - prior.HpBefore ||
                prior.ActualAmount > prior.RequestedAmount || !ids.Add(prior.RecoveryFrameId))
                throw new InvalidOperationException("An actual recovery prefix changed or repeated its paid invoice.");
            ordinal = checked(ordinal + 1);
        }
        return ordinal;
    }

    internal static bool RecoveryAmountBonusEquals(RecoveryAmountBonusReceipt a, RecoveryAmountBonusReceipt b) =>
        a.SourceSeat == b.SourceSeat && a.TargetSeat == b.TargetSeat && a.BaseRequestedAmount == b.BaseRequestedAmount &&
        a.ModifiedRequestedAmount == b.ModifiedRequestedAmount && a.HpBefore == b.HpBefore && a.HpAfter == b.HpAfter &&
        a.MaxHpAtApplication == b.MaxHpAtApplication &&
        a.AppliedRecoveryFrameId == b.AppliedRecoveryFrameId &&
        a.ActualTurnNumber == b.ActualTurnNumber && a.ActualTurnOwnerSeat == b.ActualTurnOwnerSeat &&
        a.ActualTurnRecoveryOrdinal == b.ActualTurnRecoveryOrdinal && a.Sources.SequenceEqual(b.Sources);
}
