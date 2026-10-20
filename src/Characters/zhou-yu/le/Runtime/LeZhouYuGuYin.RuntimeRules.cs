namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhou_yu_le_LeZhouYuGuYin
{
    internal static CardConversionSource GuYinSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    internal static bool IsGuYinDiscardTrigger(SkillProgramTrigger t) => t.Effects is [{ Op: SkillProgramEffectOp.ResolveGuYinInitialEntityDiscardDraw }];
}
