namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_NamedUseConversions
{
    internal static string NamedUseLedgerId(SkillProgramViewAs rule) =>
        $"view-as-name:{rule.NameLedgerId}:{CardGame.Core.RuntimeSkills.RuntimeRules_Core_AlternativeViewAsCosts.ProgramBasicCardName(rule.OutputKind)}";
}
