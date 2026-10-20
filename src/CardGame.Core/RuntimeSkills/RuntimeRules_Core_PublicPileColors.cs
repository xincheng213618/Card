namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_PublicPileColors
{
    internal static bool? SuitColor(Suit? suit)=>suit is Suit.Heart or Suit.Diamond ? true : suit is Suit.Club or Suit.Spade ? false : null;
}
