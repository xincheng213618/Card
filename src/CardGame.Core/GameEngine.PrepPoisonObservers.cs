namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool PrepPoisonFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.PrepPoisonOutcome is not { } r) return false;
        RequirePrepPoisonOutcome(root);
        if (r.Cured) return OrderedPrintedSkillLossSkillsChangedEdge(root, child);
        if (!r.HpLossApplied) return false;
        if (child is DyingFrame dying)
            return r.HpAfter <= 0 && dying.ParentFrameId == root.Id && dying.VictimSeat == root.OwnerSeat &&
                dying.Continuation == DyingContinuationKind.ProgramSkill && dying.KillerSeat is null &&
                ProgramEventHistory<PlayerDyingEvent>().Count(e => e.ResolutionId == dying.Id && e.VictimSeat == root.OwnerSeat && e.KillerSeat is null) == 1;
        return child is HpChangedTriggerWindowFrame hp && hp.ResumeFrameId == root.Id && hp.Change.ParentFrameId == root.Id &&
            hp.Change.TargetSeat == root.OwnerSeat && hp.Change.SourceSeat is null && hp.Change.Kind == HpChangeKind.Loss &&
            hp.Change.HpBefore == r.HpBefore && hp.Change.HpAfter == r.HpAfter && hp.Change.Amount == r.ActualHpLoss &&
            hp.Continuation == PostEventContinuation.Program;
    }
    private ProgramSkillFrame? PrepPoisonObserverRoot(long? rootId = null) => FindProgramObserverRoot(
        static (e, root, child) => e.PrepPoisonFirstChild(root, child),
        static (e, index) => e.HasObserverSuffix(index,
            static (host, child) => NativeDrawAttemptRidesOn(host._resolutionStack[child], host._resolutionStack[child - 1]) ||
                host.OrderedPrintedSkillLossObserverEdge(child) || host.PreventionDrawObserverEdge(child),
            static (host, child, dying) => host.IsSuspendedOrPaidRescueObserverSuffix(child, dying)), rootId);
    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeResponseHealth, 1248)]
    private bool AdvancePrepPoisonObserver()
    {
        if (_pendingDecision is not null || PrepPoisonObserverRoot() is null) return false;
        if (_resolutionStack.LastOrDefault() is NativeDrawAttemptFrame native)
        { AdvanceRuntimeFrame(native.Id); AdvanceRulesAndPublishState(); return true; }
        return TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 1248)]
    private void AssertPrepPoisonChildren(ProgramSkillFrame root)
    {
        if (root.PrepPoisonOutcome is null) return;
        var index = _resolutionStack.FindIndex(f => f.Id == root.Id);
        if (index + 1 < _resolutionStack.Count && PrepPoisonObserverRoot(root.Id) is null)
            throw new InvalidOperationException("Preparation poison lost its exact paid HP/skill-loss child and continuous native return subtree.");
    }
}
