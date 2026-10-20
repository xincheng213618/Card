namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhou_qun_ol_ZhouQunWeather
{
    internal static (string Id, string Name) ZhouQunWeather(Suit suit) => suit switch
    { Suit.Heart => ("ol:lieshu", "烈暑"), Suit.Diamond => ("ol:ningwu", "凝雾"), Suit.Spade => ("ol:zhouyu", "骤雨"), Suit.Club => ("ol:yanshuang", "严霜"), _ => ("", "无天气") };
}
