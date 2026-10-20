namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_RequestedDeckBasicOwnership
{
    internal static PendingDecision RequestedDeckBasicNativeDecision(PendingDecision p) => CardGame.Core.RuntimeSkills.RuntimeRules_liu_zhang_ol_ZonghuNeeds.ZonghuNativeDecision(p.Choices.Any(c=>c.Parameters.ContainsKey("deck-basic-source"))
        ? p with {Choices=Array.AsReadOnly(p.Choices.Where(c=>!c.Parameters.ContainsKey("deck-basic-source")).ToArray())} : p);
}
