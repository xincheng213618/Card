namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhou_yu_le_LeZhouYuPinglu
{
    internal static CardConversionSource PingluSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
}
