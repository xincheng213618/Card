using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_DrawDiscardCategoryRefund
{
    internal static string DrawDiscardCategoryReason(ProgramSkillFrame f, string part) => $"skill-program.{f.SkillId}.draw-discard-category.{part}";
}
