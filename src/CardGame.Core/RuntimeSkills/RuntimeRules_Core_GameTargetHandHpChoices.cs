using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_GameTargetHandHpChoices
{
    internal static string GameHandHpUsage(string state, int target) => $"game-hand-hp:{state}:target:{target}";

    internal static string GameHandHpReason(ProgramSkillFrame f) => $"skill-program.{f.SkillId}.{SkillProgramEffectOp.ResolveGameTargetHandHpChoice}";
}
