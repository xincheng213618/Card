using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_sun_yuan_ol_SunYuanQueries
{
    internal static CardConversionSource SunYuanSource(ProgramSkillFrame f)=>new(f.SkillId,CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);

    internal static string SunYuanHash<T>(T value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
