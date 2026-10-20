using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_LastDamageSourceReciprocity
{
    internal static string LastDamageSourceReason(ProgramSkillFrame f, bool draw) =>
        $"skill-program.{f.SkillId}.last-damage-source.{(draw ? "draw" : "discard")}";

    internal static bool IsLastDamageSourceTrigger(SkillProgramTrigger trigger) =>
        trigger.Effects is [{ Op: SkillProgramEffectOp.LastDamageSourceReciprocity }];

    internal static LastDamageSourceReciprocityDirection LastDamageSourceDirection(SkillProgramTrigger trigger) =>
        trigger.Subject == SkillProgramTriggerSubject.DamageSource
            ? LastDamageSourceReciprocityDirection.DrawOwner : LastDamageSourceReciprocityDirection.DiscardSource;
}
