using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ChainedStateBasicConversions
{
    internal const string ChainedStateBasicMarker = "chained-state-basic";

    internal static bool IsChainedStateBasicAnswer(PromptChoice choice) => choice.Parameters.ContainsKey(ChainedStateBasicMarker);

    internal static PendingDecision ChainedStateBasicNativeDecision(PendingDecision prompt) => CardGame.Core.RuntimeSkills.RuntimeRules_liu_zhang_ol_ZonghuNeeds.ZonghuNativeDecision(prompt with
    { Choices = Array.AsReadOnly(prompt.Choices.Where(c => !CardGame.Core.RuntimeSkills.RuntimeRules_Core_ChainedStateBasicConversions.IsChainedStateBasicAnswer(c)).ToArray()) });
}
