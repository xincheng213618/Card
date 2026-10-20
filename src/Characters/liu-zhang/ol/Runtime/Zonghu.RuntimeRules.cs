using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_liu_zhang_ol_Zonghu
{
    internal static string ZonghuDecisionHash(PendingDecision p)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(p))));

    internal static bool SameZonghuNeed(ZonghuNeed a,ZonghuNeed b)=>JsonSerializer.Serialize(a)==JsonSerializer.Serialize(b);
}
