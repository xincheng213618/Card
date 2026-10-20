namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhu_rong_mou_mobile_MouZhuRong
{
    internal static CardConversionSource MouZhuRongSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
}
