namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_RandomAreaAcquisition
{
    internal static CardConversionSource RandomAreaSource(ProgramSkillFrame frame) => new(frame.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);

    internal static CardMoveReason RandomAreaReason(ProgramSkillFrame frame, int index) => new($"skill-program.{frame.SkillId}.random-area-acquisition.{frame.Id}.{index}.obtain");
}
