namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_du_xi_ol_DuXiBixiong
{
    internal static int DuXiSuitBit(Suit suit) => suit switch { Suit.Spade => 1, Suit.Heart => 2, Suit.Club => 4, Suit.Diamond => 8, _ => 0 };
}
