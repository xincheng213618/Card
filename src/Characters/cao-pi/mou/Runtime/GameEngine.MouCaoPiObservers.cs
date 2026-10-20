namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 220)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 210)]
    private bool IsMouCaoPiRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) =>
        f.MouCaoPi is { Stage: MouCaoPiStage.RecoveryChildren, Option: "grow" } r &&
        RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
        recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == r.TargetSeat && recovery.Attempt.Amount == 1 &&
        recovery.Attempt.Completion == new RecoveryAttemptCompletion(RecoveryAttemptProducer.Program, f.InstructionIndex);
    private bool MouCaoPiFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouCaoPi is not { Stage: not MouCaoPiStage.Choosing } r) return false;
        AssertMouCaoPi(f);
        if (child is NativeDrawAttemptFrame) return r.Stage == MouCaoPiStage.DrawRequest && NativeDrawAttemptRidesOn(child, f) &&
            f.NativeDraw is { RequestedCount: 3, Operation: SkillProgramEffectOp.MouCaoPiXingshang };
        if (child is RecoveryReplacementFrame recovery) return IsMouCaoPiRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id &&
            hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.TargetSeat == r.TargetSeat &&
            (r.Stage == MouCaoPiStage.RecoveryChildren && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == f.OwnerSeat && hp.Change.Amount == 1 ||
             r.Stage == MouCaoPiStage.MaximumChildren && hp.Change.Kind == HpChangeKind.MaximumHp && hp.Change.Amount == 1);
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange)
            return state.ResumeProgramFrameId == f.Id && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id);
        return r.Stage == MouCaoPiStage.DrawChildren && child is CardsMovedTriggerWindowFrame moved &&
            ProgramMovementReturnMatches(f, moved) && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
            moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m =>
                _cardMovements.Contains(m) && m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter &&
                (m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(r.TargetSeat) && m.Reason.Value == MouCaoPiDrawReason ||
                 m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle));
    }
    private ProgramSkillFrame? MouCaoPiObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.MouCaoPiFirstChild(f, c), static (e, i) => e.HasObserverSuffix(i,
        static (g, n) => g.IsMovementObserverEdge(n) || g.IsYanPuDyingExitEdge(n) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) ||
            NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]),
        static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 980)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1090)]
    private bool HasMouCaoPiDamageObserver(long id) => MouCaoPiObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1120)]
    private bool HasMouCaoPiBeforeDamageObserver(long id) => MouCaoPiObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 950)]
    private bool IsMouCaoPiDying() => ActiveDying is { } d && MouCaoPiObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1520)]
    private bool AllowsMouCaoPiNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || MouCaoPiObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 28000)]
    private bool AdvanceMouCaoPiChildren() => _pendingDecision is null && MouCaoPiObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
