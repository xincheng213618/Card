using System.Globalization;
using System.Numerics;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhang_fei_wumiao_WuZhangFeiXianlue
{
    internal static int WuZhangFeiRankCount(int mask) => BitOperations.PopCount((uint)mask);
}
