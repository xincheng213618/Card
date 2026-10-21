namespace CardGame.Core;

internal sealed partial class MouXiaoQiaoOlRuntime
{
    [ProgramContinuation(ProgramContinuationKind.Movement, 34000)]
    internal bool MovementReturned(ProgramSkillFrame f) => f.MouXiaoQiaoOl is not null && f.PendingMovementContinuation is not null && Resume(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 34000)]
    internal bool MovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation movement) =>
        f.MouXiaoQiaoOl is { Paid: true, Stage: MouXiaoQiaoOlStage.GiftChildren or MouXiaoQiaoOlStage.DrawChildren or MouXiaoQiaoOlStage.NativeChildren } &&
        Valid(f) && effect?.Op == f.MouXiaoQiaoOl.Operation && movement == f.PendingMovementContinuation &&
        movement.BeforeCount == 0 && movement.CoverageResultBind is null;
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 34000)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 34000)]
    internal bool RecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame child) => f.MouXiaoQiaoOl is not null && Valid(f) && (health.ProgramRecoveryChild(f, child)||facts.GiftRecoveryChild(f,child));
    private bool FirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouXiaoQiaoOl is not { } r || !Valid(f)) return false;
        if (child is PublicHandRevealWindowFrame revealed) return revealed.ParentFrameId == f.Id && r.Stage == MouXiaoQiaoOlStage.RevealChildren;
        if (child is NativeDrawAttemptFrame draw) return r.Stage == MouXiaoQiaoOlStage.Drawing &&
            RuntimeSkills.RuntimeRules_Core_NativeDrawAttempts.NativeDrawAttemptRidesOn(draw, f);
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is not null &&
            RuntimeSkills.RuntimeRules_Core_CommonRules.ProgramMovementReturnMatches(f, moved) &&
            moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After);
        if (health.ProgramRecoveryChild(f, child)||facts.GiftRecoveryChild(f,child)) return true;
        if (r.Stage != MouXiaoQiaoOlStage.HealthChildren || r.Recover || r.TargetSeat < 0) return false;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Change.Kind == HpChangeKind.Loss &&
                hp.Change.SourceSeat is null && hp.Change.TargetSeat == r.TargetSeat && hp.Change.Amount == 1 &&
                facts.History<ProgramSkillHpLostEvent>().Any(e => e.FrameId == f.Id && e.TargetSeat == r.TargetSeat && e.Amount == 1);
        return child is DyingFrame dying && dying.ParentFrameId == f.Id && dying.VictimSeat == r.TargetSeat &&
            dying.Continuation == DyingContinuationKind.ProgramSkill && dying.KillerSeat is null &&
            health.LastPendingLoss(f.Id, r.TargetSeat) is { Amount: 1, Kind: HpChangeKind.Loss };
    }
    private ProgramSkillFrame? ObserverRoot() => frames.OfType<ProgramSkillFrame>().LastOrDefault(f =>
        frames.ChildOf(f.Id) is { } child && FirstChild(f, child) && (child is DyingFrame || health.PaidObserverSuffix(f.Id)));
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 34000)]
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 34000)]
    internal bool DyingProof()
    {
        if (health.ActiveDying is not { } d || ObserverRoot() is not { } root) return false;
        for (ResolutionFrame? p = d; p is not null; p = frames.ParentOf(p.Id)) if (p.Id == root.Id) return true;
        return false;
    }
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 34000)]
    internal bool ObserverDamage(long id)
    {
        if (ObserverRoot() is not { } root) return false;
        for (ResolutionFrame? p = root; p is not null; p = frames.ParentOf(p.Id)) if (p.Id == id) return true;
        return false;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 34000)]
    internal bool AdvanceChildren() => ObserverRoot() is not null && healthOperations.AdvanceObserverSubtree();
}
