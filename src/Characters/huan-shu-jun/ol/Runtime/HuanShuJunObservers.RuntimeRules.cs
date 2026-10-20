namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_huan_shu_jun_ol_HuanShuJunObservers
{
    internal static string HuanShuJunDecisionHash(PendingDecision p) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(p)))).ToLowerInvariant();
}
