namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_JudgedRankSlashPolicies
{
    internal static bool IsOtherActualBasicDiscardTrigger(SkillProgramTrigger trigger) =>
        trigger.Effects is [{ Op: SkillProgramEffectOp.DrawFromOtherActualBasicDiscard }];
}
