using System.Text.Json.Serialization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_ding_yuan_ol_ProgramDingYuan
{
    internal static string AdoptedChildGrantId(int seat) => $"marker:adopted-child:{seat}";
}
