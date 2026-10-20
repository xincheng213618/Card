using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_SameTypeActualUseAid
{
    // 2018 official-site community tutorial is auxiliary classification evidence.
    // This is an opt-in engineering interpretation, not a new global card category.
    internal static bool IsSameTypeAidEffectiveCard(CardKind kind) => CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsSlashCard(kind) ||
        kind is CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage;

    internal static bool IsSameTypeAidCard(CardKind paid, CardKind effective) =>
        CardCatalog.Get(paid).CategoryName == CardCatalog.Get(effective).CategoryName &&
        CardGame.Core.RuntimeSkills.RuntimeRules_Core_AlternativeViewAsCosts.ProgramBasicCardName(paid) != CardGame.Core.RuntimeSkills.RuntimeRules_Core_AlternativeViewAsCosts.ProgramBasicCardName(effective);
}
