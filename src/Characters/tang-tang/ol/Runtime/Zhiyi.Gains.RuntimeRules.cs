namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_tang_tang_ol_Zhiyi_Gains
{
    internal static bool IsTangTangZhiyiTrigger(SkillProgramTrigger t) => t.Effects is [{ Op: SkillProgramEffectOp.ResolveZhiyiGainColorBenefit }];
}
