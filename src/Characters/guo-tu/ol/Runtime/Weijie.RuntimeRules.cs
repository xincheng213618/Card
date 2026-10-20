using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_guo_tu_ol_Weijie
{
    internal static string WeijieDecisionHash(PendingDecision p)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(p))));

    internal static bool SameWeijieNeed(WeijieNeed a,WeijieNeed b)=>JsonSerializer.Serialize(a)==JsonSerializer.Serialize(b);
}
