namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_CharacterTurnedOverPrograms
{
    internal static bool CharacterTurnedOverFrameRidesOn(ResolutionFrame ride, ResolutionFrame beneath)
    {
        if (ride is ProgramLifecycleTriggerWindowFrame
            { Window: SkillProgramTriggerWindow.CharacterTurnedOver,
              Continuation: ProgramLifecycleContinuation.ResumeCharacterStateChange,
              CharacterStateContinuation: CharacterStateContinuation.Program } window)
            return beneath is ProgramSkillFrame && window.ResumeProgramFrameId == beneath.Id;
        if (ride is not ProgramSkillFrame { WindowContext: { Window: SkillProgramTriggerWindow.CharacterTurnedOver } context } program ||
            beneath is not ProgramLifecycleTriggerWindowFrame
                { Window: SkillProgramTriggerWindow.CharacterTurnedOver,
                  Continuation: ProgramLifecycleContinuation.ResumeCharacterStateChange } parent ||
            context.ParentFrameId != parent.Id || context.TargetSeat != parent.OwnerSeat ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count) return false;
        var candidate = parent.Candidates[parent.CandidateIndex];
        return program.OwnerSeat == candidate.OwnerSeat && program.SkillId == candidate.SkillId &&
            program.SkillInstanceId == candidate.SkillInstanceId && program.TriggerId == candidate.BindingId;
    }
}
