namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_CardDamageMaximumHpReplacement
{
    internal static string MaximumHpReplacementUsage(string policyId) => "card-maximum-hp-replacement:" + policyId;
}
