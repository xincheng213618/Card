namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_CardUseModules
{
    internal static CardUseEffectSource CreateProgramTurnEffectSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
}
