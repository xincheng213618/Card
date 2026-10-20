namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_bian_fu_ren_ol_BianFuRenPrograms
{
    internal static bool IsBianFuRenOperation(SkillProgramEffectOp operation) =>
        operation is SkillProgramEffectOp.WanweiObtainMatchingLostCard or SkillProgramEffectOp.YuejianRevealAndClaimUsedCard;
}
