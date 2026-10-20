namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_IssuedPlayUsePolicies
{
    internal static bool IsEligibleIssuedNoResponseCard(CardKind kind) => CardCatalog.Get(kind).CategoryName == "基本牌" || CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsOrdinaryTrick(kind);
}
