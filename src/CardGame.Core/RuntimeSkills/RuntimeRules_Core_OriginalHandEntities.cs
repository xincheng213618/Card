using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_OriginalHandEntities
{
    internal static bool IsOriginalHandTrigger(SkillProgramTrigger trigger, SkillProgramEffectOp op) =>
        trigger.Effects is [var effect] && effect.Op == op;
}
