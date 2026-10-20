namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_bian_fu_ren_ol_Wanwei_Qualifications
{
    internal static bool IsBianFuRenWanweiTrigger(SkillProgramTrigger trigger) => trigger.Effects
        .Any(effect => effect.Op == SkillProgramEffectOp.WanweiObtainMatchingLostCard);
}
