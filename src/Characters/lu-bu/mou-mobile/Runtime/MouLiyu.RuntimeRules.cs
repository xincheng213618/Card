using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_lu_bu_mou_mobile_MouLiyu
{
    internal static int MouLbCategory(CardKind kind) => CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.GetProgramCardCategory(kind) switch { SkillProgramCardCategory.Basic => 1, SkillProgramCardCategory.Equipment => 4, _ => 2 };
}
