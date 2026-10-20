namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool WeiwanLionRemoval(ProgramSkillFrame f, int seat) => f.Weiwan is { } r &&
        (r.Stage == WeiwanStage.CostChildren && r.PrintedKind == CardKind.SilverLion && r.CostFrom == CardLocation.Equipment(seat) && r.CostTo != r.CostFrom ||
         r.Stage == WeiwanStage.ObtainChildren && r.Samples.Any(s => s.PrintedKind == CardKind.SilverLion && s.From == CardLocation.Equipment(seat)));
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 380)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 370)]
    private bool IsWeiwanRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.Weiwan is not { } r || !HasPaidWeiwanSelection(f)) return false;
        if (RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement)
            return r.Stage is WeiwanStage.CostChildren or WeiwanStage.ObtainChildren &&
                recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
                recovery.Attempt.SourceSeat == recovery.Attempt.TargetSeat && recovery.Attempt.Amount == 1 &&
                recovery.Attempt.Completion.MoveReason == new CardMoveReason(r.Stage == WeiwanStage.CostChildren ? WeiwanCostReason : WeiwanObtainReason) &&
                WeiwanLionRemoval(f, recovery.Attempt.TargetSeat);
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id); var child = _resolutionStack.FindIndex(frame => frame.Id == recovery.Id);
        return index >= 0 && child > index + 1 && WeiwanFirstChild(f, _resolutionStack[index + 1]) && WeiwanObserverSuffix(index) &&
            RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    private bool WeiwanFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.Weiwan is not { } r || !HasPaidWeiwanSelection(f)) return false;
        AssertWeiwan(f);
        if (child is IdentityAppearanceFrame appearance) return r.Stage == WeiwanStage.HealthIssuing && r.Branch == WeiwanBranch.LoseHp &&
            appearance.Receipt.Entry == IdentityAppearanceEntry.ProgramHpLoss && appearance.Receipt.ParentFrameId == f.Id &&
            appearance.Receipt.OwnerSeat == r.TargetSeat && appearance.Receipt.RequestedDeduction == 1 && appearance.Receipt.OriginalInstructionIndex == f.InstructionIndex;
        if (child is DyingFrame dying) return r.Stage == WeiwanStage.HealthChildren && r.Branch == WeiwanBranch.LoseHp && r.HpLossCaptured &&
            dying.ParentFrameId == f.Id && dying.VictimSeat == r.TargetSeat && dying.KillerSeat is null && dying.Continuation == DyingContinuationKind.ProgramSkill;
        if (child is RecoveryReplacementFrame recovery) return IsWeiwanRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id &&
            (r.Stage == WeiwanStage.HealthChildren && r.Branch == WeiwanBranch.LoseHp && r.HpLossCaptured &&
                hp.Continuation == PostEventContinuation.Program && hp.Change.Kind == HpChangeKind.Loss &&
                hp.Change.TargetSeat == r.TargetSeat && hp.Change.SourceSeat is null && hp.Change.Amount == 1 ||
             r.Stage is WeiwanStage.CostChildren or WeiwanStage.ObtainChildren &&
                hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery &&
                hp.Change.SourceSeat == hp.Change.TargetSeat && hp.Change.Amount == 1 && WeiwanLionRemoval(f, hp.Change.TargetSeat));
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state) return state.ResumeProgramFrameId == f.Id &&
            state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e =>
                e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id && e.Change.Window == state.Window);
        return child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is not null && ProgramMovementReturnMatches(f, moved) &&
            moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
            moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
            (moved.Batch.Id == (r.Stage == WeiwanStage.CostChildren ? r.CostBatchId : r.Stage == WeiwanStage.ObtainChildren ? r.ObtainBatchId : 0) &&
                moved.Batch.Movements.All(m => WeiwanPrimaryMovement(f, m)) || WeiwanOxSpill(f, moved.Batch));
    }
    private bool WeiwanLifecycleEdge(int index) => _resolutionStack[index - 1] is ProgramLifecycleTriggerWindowFrame w &&
        _resolutionStack[index] is ProgramSkillFrame f && w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count &&
        MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex]) && f.WindowContext is { } context && context.ParentFrameId == w.Id && context.Window == w.Window;
    private bool WeiwanObserverSuffix(int index) => HasObserverSuffix(index,
        static (g, next) => g.IsMovementObserverEdge(next) || g.IsSameNameHandObserverEdge(next) || g.WeiwanLifecycleEdge(next) ||
            g.TypedDamagePostEventEdge(g._resolutionStack[next - 1], g._resolutionStack[next]) ||
            g._resolutionStack[next - 1] is ProgramSkillFrame { Zenrun: not null } zenrun && g.ZenrunFirstChild(zenrun, g._resolutionStack[next]) ||
            NativeDrawAttemptRidesOn(g._resolutionStack[next], g._resolutionStack[next - 1]) ||
            g.RecoveryReplacementFrameRidesOn(g._resolutionStack[next], g._resolutionStack[next - 1]) || g.IsYanPuDyingExitEdge(next),
        static (g, next, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(next, dying));
    private ProgramSkillFrame? WeiwanObserverRoot() => FindProgramObserverRoot(
        static (g, f, child) => g.WeiwanFirstChild(f, child), static (g, index) => g.WeiwanObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1150)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1260)]
    private bool HasWeiwanDamageObserver(long id) => WeiwanObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1290)]
    private bool HasWeiwanBeforeDamageObserver(long id) => WeiwanObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1120)]
    private bool IsWeiwanDying() => ActiveDying is { } dying && WeiwanObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1690)]
    private bool AllowsWeiwanNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || WeiwanObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature && !sourceLess &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 28620)]
    private bool AdvanceWeiwanChildren() => _pendingDecision is null && WeiwanObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
