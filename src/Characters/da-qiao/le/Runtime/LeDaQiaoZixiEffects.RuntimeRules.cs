namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_da_qiao_le_LeDaQiaoZixiEffects
{
    internal static CardConversionSource ZixiEffectsSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    internal static bool ZixiEffectsKind(CardKind kind) => CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.GetProgramCardCategory(kind) == SkillProgramCardCategory.Basic || CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsOrdinaryTrick(kind);
}
