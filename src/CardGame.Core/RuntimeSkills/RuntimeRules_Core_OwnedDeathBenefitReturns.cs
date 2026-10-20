namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_OwnedDeathBenefitReturns
{
    internal static string OwnedDeathBenefitReturnHash(ProgramOwnedDeathBenefitReturn receipt) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(receipt))));
}
