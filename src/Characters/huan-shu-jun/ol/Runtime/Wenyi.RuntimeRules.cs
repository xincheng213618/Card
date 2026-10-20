namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_huan_shu_jun_ol_Wenyi
{
    internal static bool SameHuanShuJunAction(CardActionContext a, CardActionContext b) => System.Text.Json.JsonSerializer.Serialize(a) == System.Text.Json.JsonSerializer.Serialize(b);
}
