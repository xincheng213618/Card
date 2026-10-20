namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zou_shi_le_LeZouShiYunzheng
{
    internal static CardConversionSource YunzhengSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
}
