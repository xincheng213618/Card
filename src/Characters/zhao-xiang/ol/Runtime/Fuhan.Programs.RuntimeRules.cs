using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhao_xiang_ol_Fuhan_Programs
{
    internal static string ZhaoXiangPoolFingerprint(IReadOnlyList<string> ids)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',ids))));
}
