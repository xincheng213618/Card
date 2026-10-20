namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_NativeDrawAttempts
{
    internal static bool NativeDrawAttemptRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
        child is NativeDrawAttemptFrame attempt &&
            attempt.Context.ParentFrameId == parent.Id && parent.NativeDraw is { } paid &&
            paid.ActiveAttemptId == attempt.Id && paid.InstructionIndex == attempt.Return.InstructionIndex &&
            paid.RecipientIndex == attempt.Return.RecipientIndex && paid.ReturnKind == attempt.Return.Kind ||
        parent is NativeDrawAttemptFrame native && child is ProgramLifecycleTriggerWindowFrame window &&
            window.Id == native.ActiveWindowFrameId && window.NativeDrawAttempt == native.Context &&
            window.Continuation == ProgramLifecycleContinuation.ResumeNativeDrawAttempt ||
        child is ProgramSkillFrame programChild && parent is ProgramLifecycleTriggerWindowFrame lifecycle &&
            lifecycle.NativeDrawAttempt is { } context && programChild.WindowContext?.NativeDraw == context &&
            programChild.WindowContext.ParentFrameId == lifecycle.Id &&
            lifecycle.CandidateIndex >= 0 && lifecycle.CandidateIndex < lifecycle.Candidates.Count &&
            CardGame.Core.RuntimeSkills.RuntimeRules_Core_DamageTargetMountObserver.MountObserverCandidateMatches(programChild, lifecycle.Candidates[lifecycle.CandidateIndex]);
}
