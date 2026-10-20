namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_du_fu_ren_ol_Yise_Qualifications
{
    internal static bool IsDuFuRenYiseTrigger(SkillProgramTrigger t) => t.Effects is [{ Op: SkillProgramEffectOp.ResolveYiseOwnedGain }];
}
