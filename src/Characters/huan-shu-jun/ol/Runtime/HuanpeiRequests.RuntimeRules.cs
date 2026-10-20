using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_huan_shu_jun_ol_HuanpeiRequests
{
    internal const string HuanpeiOpen = "huanpei-open";

    internal static PendingDecision HuanpeiNativeDecision(PendingDecision p) => p with { Choices = Array.AsReadOnly(p.Choices.Where(c => !c.Parameters.ContainsKey(HuanpeiOpen)).ToArray()) };
}
