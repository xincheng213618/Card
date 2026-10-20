using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_li_yi_ol_LiYiQueries
{
    internal static string ChanshuangHash<T>(T value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    internal static int ChanshuangItemCount(ChanshuangReceipt r)=>r.Operation==SkillProgramEffectOp.ResolveChanshuangEnding?System.Numerics.BitOperations.PopCount((uint)r.FrozenMask):2;

    internal static ChanshuangVote ChanshuangItem(ProgramSkillFrame f)
    {var r=f.Chanshuang!;return r.Operation==SkillProgramEffectOp.ResolveChanshuangEnding?new(f.OwnerSeat,(ChanshuangOption)(r.Cursor+1)):r.Votes[r.Cursor];}
}
