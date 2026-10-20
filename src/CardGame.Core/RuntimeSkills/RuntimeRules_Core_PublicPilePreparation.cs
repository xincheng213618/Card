using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_PublicPilePreparation
{
    internal static string PreparationReason(ProgramSkillFrame f, string step) => $"skill-program.{f.SkillId}.public-pile-preparation.{step}";
}
