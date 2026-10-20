namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_tang_ji_ol_TangJiPrograms
{
    internal static string SuitName(Suit suit) => suit switch
    {
        Suit.Spade => "黑桃", Suit.Heart => "红桃", Suit.Club => "梅花", _ => "方块"
    };
}
