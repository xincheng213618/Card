namespace CardGame.Core;

internal sealed partial class MoZhangFeiRuntime
{
    private bool RedLossChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.MoZhangFei is not { Stage: MoZhangFeiStage.EffectChildren, Modified: true,
                Color: MoZhangFeiColor.Red, RedRequestedHpLoss: > 0, RedHpBefore: { } before } r || !Valid(root) ||
            r.RedRequestedHpLoss != before ||
            facts.History<MoZhangFeiRedLossRequestedEvent>().Count(e => e.FrameId == root.Id &&
                e.TargetSeat == r.TargetSeat && e.HpBefore == before && e.RequestedAmount == r.RedRequestedHpLoss) != 1 ||
            facts.History<ProgramSkillHpLostEvent>().Where(e => e.FrameId == root.Id).ToArray() is not [var lost] ||
            lost.SkillId != root.SkillId || lost.TargetSeat != r.TargetSeat || lost.Amount != r.RedRequestedHpLoss ||
            lost.RemainingHp != 0) return false;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == root.Id && hp.ResumeFrameId == root.Id &&
                hp.Continuation == PostEventContinuation.Program && hp.CardId is null && hp.CardKind is null &&
                hp.Change.Kind == HpChangeKind.Loss && hp.Change.SourceSeat is null && hp.Change.TargetSeat == r.TargetSeat &&
                hp.Change.HpBefore == before && hp.Change.HpAfter == 0 && hp.Change.Amount == before;
        if (child is not DyingFrame dying || dying.ParentFrameId != root.Id || dying.VictimSeat != r.TargetSeat ||
            dying.Continuation != DyingContinuationKind.ProgramSkill || dying.KillerSeat is not null ||
            dying.ResponderIndex < 0 || dying.ResponderIndex >= dying.ResponderSeats.Count ||
            dying.ResponderSeats.Count > facts.PlayerCount || dying.ResponderSeats.Distinct().Count() != dying.ResponderSeats.Count ||
            dying.ResponderSeats.Any(s => s < 0 || s >= facts.PlayerCount) ||
            facts.History<PlayerDyingEvent>().Count(e => e.ResolutionId == dying.Id && e.VictimSeat == r.TargetSeat && e.KillerSeat is null) != 1)
            return false;
        return health.LastPendingLoss(root.Id, r.TargetSeat) is { SourceSeat: null, HpAfter: 0 } invoice &&
            invoice.HpBefore == before && invoice.Amount == before;
    }
    private bool FirstHealthChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.MoZhangFei is not { } r || !Valid(root)) return false;
        if (r.Op == SkillProgramEffectOp.ResolveMoZhangFeiChenshi && r.Stage == MoZhangFeiStage.RecoveryChildren)
            return health.ProgramRecoveryChild(root, child) && (child switch
            {
                RecoveryReplacementFrame recovery => recovery.Attempt.SourceSeat == root.OwnerSeat &&
                    recovery.Attempt.TargetSeat == root.OwnerSeat && recovery.Attempt.Amount == 1,
                HpChangedTriggerWindowFrame hp => hp.Change.SourceSeat == root.OwnerSeat &&
                    hp.Change.TargetSeat == root.OwnerSeat && hp.Change.Amount == 1,
                _ => false
            });
        if (RedLossChild(root, child)) return true;
        if (child is NativeDrawAttemptFrame attempt)
            return root.NativeDraw is { ReturnKind: NativeDrawAttemptReturnKind.ProgramOperation,
                Operation: SkillProgramEffectOp.ResolveMoZhangFeiZhuohun } &&
                RuntimeSkills.RuntimeRules_Core_NativeDrawAttempts.NativeDrawAttemptRidesOn(attempt, root);
        if (child is CardsMovedTriggerWindowFrame movement)
            return RuntimeSkills.RuntimeRules_Core_CommonRules.ProgramMovementReturnMatches(root, movement);
        return child is ProgramLifecycleTriggerWindowFrame skills &&
            RuntimeSkills.RuntimeRules_Core_ObserverSubtrees.IsObserverSkillsChangedEdge(root, skills);
    }
    private ProgramSkillFrame? HealthObserverRoot()
    {
        foreach (var root in frames.OfType<ProgramSkillFrame>())
            if (frames.ChildOf(root.Id) is { } child && FirstHealthChild(root, child) && health.PaidObserverSuffix(root.Id))
                return root;
        return null;
    }
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 33700, group: 33700)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 33700)]
    internal bool IsOwnedHealthDying()
    {
        if (health.ActiveDying is not { } dying || HealthObserverRoot() is not { } root) return false;
        for (ResolutionFrame? current = dying; current is not null; current = frames.ParentOf(current.Id))
            if (current.Id == root.Id) return true;
        return false;
    }
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 33700)]
    internal bool OwnsHealthDamageObserver(long windowId)
    {
        if (frames.Find(windowId) is not DamageTriggerWindowFrame || HealthObserverRoot() is not { } root) return false;
        for (ResolutionFrame? current = root; current is not null; current = frames.ParentOf(current.Id))
            if (current.Id == windowId) return true;
        return false;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 33700)]
    internal bool AdvanceHealthChildren() => HealthObserverRoot() is not null && healthOperations.AdvanceObserverSubtree();
}
