namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ObserverSubtrees
{
    internal static bool IsObserverSkillsChangedEdge(ResolutionFrame parent, ResolutionFrame child) =>
        child is ProgramLifecycleTriggerWindowFrame changed && parent is ProgramSkillFrame owner &&
        changed.Window == SkillProgramTriggerWindow.SkillsChanged && changed.ResumeProgramFrameId == owner.Id &&
        changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
        changed.CandidateIndex >= 0 && changed.CandidateIndex <= changed.Candidates.Count;
}
