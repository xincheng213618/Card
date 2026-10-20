namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_SequentialDeckKindGrowth
{
    internal static CardConversionSource SequentialDeckKindGrowthSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);

    internal static string SequentialDeckKindGrowthReason(long frameId, int step) =>
        $"skill-program.sequential-deck-kind-growth.{frameId}.{step}.gain";

    internal static bool SameSequentialDeckKindGrowthStep(SequentialDeckKindGrowthStepReceipt left, SequentialDeckKindGrowthStepReceipt right) =>
        left.Index == right.Index && left.CardId == right.CardId && left.BatchId == right.BatchId &&
        left.SequenceBefore == right.SequenceBefore && left.SequenceAfter == right.SequenceAfter &&
        left.ActualObtained == right.ActualObtained && left.Movements.SequenceEqual(right.Movements);
}
