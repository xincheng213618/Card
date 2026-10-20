namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_DamagePreventionPrograms
{
    internal static bool UsesDamageNatureCondition(SkillProgramTriggerCondition condition) =>
        condition.Kind == SkillProgramTriggerConditionKind.DamageNatureIs ||
        condition.Children.Any(CardGame.Core.RuntimeSkills.RuntimeRules_Core_DamagePreventionPrograms.UsesDamageNatureCondition);
}
