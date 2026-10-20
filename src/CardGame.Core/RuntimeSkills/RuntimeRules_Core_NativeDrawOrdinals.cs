namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_NativeDrawOrdinals
{
    internal static bool UsesNativeDrawOrdinal(SkillProgramTrigger trigger) => ProgramInstructionResolver.Default
        .Features(trigger).UsesCondition(SkillProgramTriggerConditionKind.NativeDrawActualTurnOrdinalIs);

    // The event prefix belongs to this invoice, not to whichever turn or draw
    // happens to be current when an older gain child finally returns.
    internal static int? NativeDrawOrdinalAt(IReadOnlyList<IGameEvent> history, int endExclusive,
        int owner, int turn, int turnOwner)
    {
        if (turn < 1) return null;
        var startIndex = endExclusive - 1;
        while (startIndex >= 0 && history[startIndex] is not TurnStartedEvent) startIndex--;
        if (startIndex < 0) return null;
        var start = (TurnStartedEvent)history[startIndex];
        for (var index = startIndex + 1; index < endExclusive; index++)
            if (history[index] is TurnEndedEvent ended && ended.TurnNumber == start.TurnNumber && ended.ActorSeat == start.ActorSeat)
                return null;
        if (start.TurnNumber != turn || start.ActorSeat != turnOwner)
            throw new InvalidOperationException("A native draw ordinal lost its original actual-turn boundary.");
        var seen = new HashSet<long>();
        var ordinal = 1;
        for (var index = startIndex + 1; index < endExclusive; index++)
        {
            if (history[index] is not NativeDrawInvocationRecordedEvent draw || draw.OwnerSeat != owner) continue;
            if (draw.InvocationId <= 0 || draw.ActualCount < 1 || draw.Requested < draw.ActualCount ||
                draw.TurnNumber != turn || draw.ActualTurnOwnerSeat != turnOwner || draw.Reason == CardMoveReasons.InitialDeal ||
                !seen.Add(draw.InvocationId))
                throw new InvalidOperationException("An actual-turn draw prefix repeated or changed an issued native invoice.");
            ordinal = checked(ordinal + 1);
        }
        return ordinal;
    }
}
