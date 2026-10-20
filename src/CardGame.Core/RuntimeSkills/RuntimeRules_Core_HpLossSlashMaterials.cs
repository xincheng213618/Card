namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_HpLossSlashMaterials
{
    internal static SkillProgramEffect? HpLossSlashProgramProducer(ProgramExecutionPlan plan) =>
        ConvertedCardSelectionSource.HpLossProducer(plan);
}
