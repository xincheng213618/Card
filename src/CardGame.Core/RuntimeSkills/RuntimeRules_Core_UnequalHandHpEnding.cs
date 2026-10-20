using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_UnequalHandHpEnding
{
    internal static string UnequalHandHpReason(ProgramSkillFrame f, string step) => $"skill-program.{f.SkillId}.unequal-hand-hp.{step}";
}
