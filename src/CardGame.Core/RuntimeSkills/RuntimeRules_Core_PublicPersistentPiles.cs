namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_PublicPersistentPiles
{
    internal static string PublicPileCardLabel(Card card) => card.DisplayName + " " + CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.GetSuitDisplayName(card.Suit) + card.RankText;
}
