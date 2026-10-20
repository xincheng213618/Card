using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_wang_yun_ol_Jingong_Programs
{
    internal static string WangYunSamplesFingerprint(IReadOnlyList<CardKind> samples)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',',samples.Select(k=>(int)k)))));
}
