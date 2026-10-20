namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_yang_wan_ol_YangWanPrograms
{
    internal static int SuitMask(Suit suit) => suit switch
    {
        Suit.Spade => 1, Suit.Heart => 2, Suit.Club => 4, Suit.Diamond => 8, _ => 0
    };
}
