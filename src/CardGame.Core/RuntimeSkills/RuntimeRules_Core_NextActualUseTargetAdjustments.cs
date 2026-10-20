namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_NextActualUseTargetAdjustments
{
    internal static SkillProgramEffect? NextActualUseSelectedProducer(ProgramExecutionPlan plan) =>
        AdjustedUseSelection.Producer(plan);
}
