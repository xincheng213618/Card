namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool DrainMouPt(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 30500)]
    private bool IsMouPtMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.ResolveMouPangTongLianhuanTargets && ValidMouPtTargets(f) && pending == f.PendingMovementContinuation &&
        pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    private bool MouPtFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouPtTargets(f)) return false;
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.Movements.Count > 0 &&
            moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.All(m => ProgramEventHistory<MouPangTongMovementSignedEvent>().Contains(new(f.Id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence)));
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp) && hp.Change.ParentFrameId == f.Id && ProgramRecoveryReceiptMatches(f, hp, CompleteProgramEventHistory().ToArray());
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id && ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray());
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouPtObserverSuffix(int i) => HasObserverSuffix(i, static (g, n) => g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) ||
        g.IsYanPuDyingExitEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) ||
        g._resolutionStack[n - 1] is ProgramSkillFrame p && g.MouPtFirstChild(p, g._resolutionStack[n]) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]),
        static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? MouPtObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.MouPtFirstChild(f, child), static (g, i) => g.MouPtObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 30500)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 30500)]
    private bool HasMouPtDamageObserver(long id) => MouPtObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 30500)]
    private bool HasMouPtBeforeDamageObserver(long id) => MouPtObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 30500)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 30500)]
    private bool IsMouPtDying() => ActiveDying is { } dying && MouPtObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 30500)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 30500)]
    private bool AllowsMouPtNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || MouPtObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return !sourceLess && e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 30500)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 30500)]
    private bool IsMouPtRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.MouPangTongTargets is null || !ValidMouPtTargets(f)) return false;
        if (recovery.ParentFrameId == f.Id) return ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray());
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return index >= 0 && child > index + 1 && MouPtFirstChild(f, _resolutionStack[index + 1]) && MouPtObserverSuffix(index) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 30500)]
    private void AssertMouPt(ProgramSkillFrame f)
    { if (f.MouPangTongTargets is not null && !ValidMouPtTargets(f)) throw new InvalidOperationException("Lianhuan changed its frozen native Use or target queue."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 30500)]
    private bool ContinueMouPtPaidSource(ProgramSkillFrame f) => f.MouPangTongTargets is not null && ValidMouPtTargets(f) ||
        f.SkillId == MouPangTongComposition.Lianhuan && f.TriggerId == "pay-for-chain" && f.InstructionIndex is 1 or 2 &&
        ProgramEventHistory<ProgramSkillHpLostEvent>().Any(e => e.FrameId == f.Id && e.SkillId == f.SkillId && e.TargetSeat == f.OwnerSeat && e.Amount == 1) ||
        f.SkillId == MouPangTongComposition.Niepan && f.TriggerId == "rebirth" && f.InstructionIndex is >= 1 and <= 6 && f.WindowContext is { } context &&
        _resolutionStack.OfType<DyingFrame>().Any(d => d.Id == context.ParentFrameId && d.VictimSeat == f.OwnerSeat) &&
        _skillRuntimeState.GetUsage(f.OwnerSeat, f.SkillId, "rebirth", SkillUsageScope.Game) == 1;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 30500)]
    private bool FinishMouPtDeadSource(ProgramSkillFrame f) => f.MouPangTongTargets is not null && ValidMouPtTargets(f);
    [ProgramContinuation(ProgramContinuationKind.Resume, 30500)]
    private bool ResumeMouPt(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouPangTongTargets: not null } f || f.Id != id) return false; ContinueMouPtTargets(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 30500)]
    private bool ReturnMouPtMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeMouPt(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 30500)]
    private bool AdvanceMouPtChildren() => _pendingDecision is null && MouPtObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
