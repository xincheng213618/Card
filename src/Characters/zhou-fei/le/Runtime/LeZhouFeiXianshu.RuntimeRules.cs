namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhou_fei_le_LeZhouFeiXianshu
{
    internal static CardConversionSource LeZhouFeiXianshuSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
}
