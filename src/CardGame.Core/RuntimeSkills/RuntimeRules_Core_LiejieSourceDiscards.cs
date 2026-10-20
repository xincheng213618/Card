namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_LiejieSourceDiscards
{
    internal static string LiejieDiscardReason(ProgramSkillFrame frame) => $"skill-program.{frame.SkillId}.liejie-discard";
}
