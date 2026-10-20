namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_PaidColorConversions
{
    internal static bool HasActualRedOrBlackSuit(Suit suit) => suit is Suit.Spade or Suit.Club or Suit.Heart or Suit.Diamond;
}
