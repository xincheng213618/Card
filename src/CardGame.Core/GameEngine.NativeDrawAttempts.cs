namespace CardGame.Core;

public sealed partial class GameEngine
{
    private NativeDrawAttemptRuntime NativeDrawAttempts => GetRuntimeModule<NativeDrawAttemptRuntime>();
    private bool HasNativeDrawAttemptCapability => _contentRegistry.ProgramDependencies
        .HasTriggerWindow(SkillProgramTriggerWindow.NativeDrawStarting);

    private bool TryBeginNativeDrawAttempt(ResolutionFrame parent) => NativeDrawAttempts.TryBegin(parent);
    private NativeDrawAttemptFrame? ExactNativeDrawAttempt(ProgramSkillWindowContext context) =>
        NativeDrawAttempts.ExactNativeDrawAttempt(context);
    private bool IsExactNativeDrawCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        NativeDrawAttempts.IsExactNativeDrawCandidate(candidate, context);
    private ProgramSkillWindowContext AttachNativeDrawContext(ProgramLifecycleTriggerWindowFrame window,
        ProgramSkillWindowContext context) => NativeDrawAttempts.AttachNativeDrawContext(window, context);
    private void CompleteNativeDrawReplacement(ProgramSkillFrame frame, int sourceSeat,
        IReadOnlyList<int> gainedIds, CardMoveReason reason, long sequenceBefore) =>
        NativeDrawAttempts.CompleteNativeDrawReplacement(frame, sourceSeat, gainedIds, reason, sequenceBefore);
    private void ReturnNativeDrawStartingProgram(ProgramSkillFrame frame, bool completed) =>
        NativeDrawAttempts.ReturnNativeDrawStartingProgram(frame, completed);
    private void ReturnNativeDrawAttemptWindow(ProgramLifecycleTriggerWindowFrame window) =>
        NativeDrawAttempts.ReturnNativeDrawAttemptWindow(window);
    private void ContinueNativeDrawAttempt(long id) => NativeDrawAttempts.ContinueNativeDrawAttempt(id);
    private static bool NativeDrawAttemptRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_NativeDrawAttempts.NativeDrawAttemptRidesOn(child, parent);
    private void AssertNativeDrawAttempts() => NativeDrawAttempts.AssertNativeDrawAttempts();
}
