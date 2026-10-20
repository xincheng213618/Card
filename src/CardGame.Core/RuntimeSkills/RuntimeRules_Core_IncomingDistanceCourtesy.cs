using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_IncomingDistanceCourtesy
{
    internal static string IncomingDistanceCourtesyReason(ProgramSkillFrame f, int step) => $"skill-program.{f.SkillId}.incoming-distance-courtesy.{step}";
}
