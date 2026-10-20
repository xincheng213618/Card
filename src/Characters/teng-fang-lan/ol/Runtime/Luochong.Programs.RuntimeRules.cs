namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_teng_fang_lan_ol_Luochong_Programs
{
    internal static string LuochongOptionDisplayName(string optionId) => optionId switch
    {
        "recover" => "回复1点体力",
        "loseHp" => "失去1点体力",
        "discardTwo" => "弃置两张牌",
        "drawTwo" => "摸两张牌",
        _ => throw new InvalidOperationException($"Unknown Luochong option '{optionId}'.")
    };
}
