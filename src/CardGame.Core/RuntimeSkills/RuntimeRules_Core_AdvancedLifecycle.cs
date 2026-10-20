namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_AdvancedLifecycle
{
    internal static bool AdvancedIsOrdinaryTrick(CardKind kind) => CardCatalog.Get(kind).CategoryName == "锦囊牌" && !CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsDelayedCard(kind);
}
