using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_qian_zhao_ol_WeifuUses
{
    internal static bool WeifuCanAddTargets(CardUseFrame use) => CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.GetProgramCardCategory(use.CardKind) == SkillProgramCardCategory.Basic ||
        CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsOrdinaryTrick(use.CardKind);
}
