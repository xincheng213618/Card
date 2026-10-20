using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_DamageTargetBudget
{
    internal static bool DamageBudgetCard(CardActionContext a) => CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsSlashCard(a.EffectiveKind) ||
        CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsOrdinaryTrick(a.EffectiveKind) && a.EffectiveIsRed == false;
}
