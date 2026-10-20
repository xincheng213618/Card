namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool DrainMouDiaoChanChildren(ProgramSkillFrame f)
    {
        return TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    }

    private void SealMouDiaoChanMovement(long id, long before)
    {
        var f = GetActiveProgramFrame(id); var r = f.MouDiaoChanMobile!;
        ReplaceRuntimeTop(f with { MouDiaoChanMobile = r with { After = _movementSequence } });
        foreach (var m in _cardMovements.Where(m => m.Sequence > before && m.Sequence <= _movementSequence).ToArray())
        {
            var fact = new MouDiaoChanMobileMovementEvent(id, m.CardId, m.From, m.To, m.Reason, m.Sequence);
            if (!ProgramEventHistory<MouDiaoChanMobileMovementEvent>().Contains(fact)) AdvanceEventRulesAndQueueFact(fact);
        }
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 31700)]
    private bool IsMouDiaoChanMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        e?.Op is SkillProgramEffectOp.ResolveMouDiaoChanLijian or SkillProgramEffectOp.ResolveMouDiaoChanBiyue && f.MouDiaoChanMobile is { Stage: MouDiaoChanMobileStage.CostChildren or MouDiaoChanMobileStage.DrawChildren } &&
        p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && ValidMouDiaoChan(f);
    private bool MouDiaoChanFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouDiaoChan(f) || f.MouDiaoChanMobile is not { } r) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouDiaoChanMobileStage.Drawing && f.NativeDraw is not null;
        if (IsMouDiaoChanSilverLionChild(f, child)) return true;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && IsMouDiaoChanMovement(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
                moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m) &&
                    ProgramEventHistory<MouDiaoChanMobileMovementEvent>().Count(e => e == new MouDiaoChanMobileMovementEvent(f.Id, m.CardId, m.From, m.To, m.Reason, m.Sequence)) == 1);
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool IsMouDiaoChanSilverLionChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouDiaoChanMobile is not { Stage: MouDiaoChanMobileStage.CostChildren } r || !ValidMouDiaoChan(f) ||
            ProgramEventHistory<MouDiaoChanMobileCostEvent>().LastOrDefault(e => e.FrameId == f.Id && e.Kind == CardKind.SilverLion && e.From == CardLocation.Equipment(f.OwnerSeat)) is not { } paid ||
            !_cardMovements.Any(m => m.Sequence == paid.Sequence && m.CardId == paid.CardId && m.CardKind == CardKind.SilverLion && m.From == paid.From && m.To == paid.To && m.Reason.Value == MouDiaoChanCostReason)) return false;
        if (child is RecoveryReplacementFrame recovery)
            return RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == MouDiaoChanCostReason &&
                recovery.Attempt.Amount == 1 && recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == f.OwnerSeat;
        return child is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id &&
            hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.Amount > 0 &&
            hp.Change.SourceSeat == f.OwnerSeat && hp.Change.TargetSeat == f.OwnerSeat && ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(e =>
                e.ResolutionId > f.Id && e.ResolutionId < hp.Change.Id && e.PlayerSeat == f.OwnerSeat && e.Reason.Value == MouDiaoChanCostReason && e.RecoveredAmount == hp.Change.Amount && e.RemainingHp == hp.Change.HpAfter);
    }
    private bool MouDiaoChanObserverEdge(int n) => IsMovementObserverEdge(n) || IsSameNameHandObserverEdge(n) || IsYanPuDyingExitEdge(n) ||
        TypedDamagePostEventEdge(_resolutionStack[n - 1], _resolutionStack[n]) || _resolutionStack[n - 1] is ProgramSkillFrame f && MouDiaoChanFirstChild(f, _resolutionStack[n]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[n], _resolutionStack[n - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[n], _resolutionStack[n - 1]);
    private bool MouDiaoChanObserverSuffix(int index) => _resolutionStack[index + 1] is DyingFrame dying && index + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(index + 1, dying) ||
        HasObserverSuffix(index, static (g, n) => g.MouDiaoChanObserverEdge(n), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? MouDiaoChanObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouDiaoChanFirstChild(f, c), static (g, n) => g.MouDiaoChanObserverSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 31700)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 31700)]
    private bool HasMouDiaoChanDamageObserver(long id) => MouDiaoChanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 31700)]
    private bool HasMouDiaoChanBeforeDamageObserver(long id) => MouDiaoChanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 31700)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 31700)]
    private bool IsMouDiaoChanDying() => ActiveDying is { } dying && MouDiaoChanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 31700)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 31700)]
    private bool AllowsMouDiaoChanNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || MouDiaoChanObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 31700)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 31700)]
    private bool IsMouDiaoChanRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (IsMouDiaoChanSilverLionChild(f, recovery)) return true;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return index >= 0 && child > index + 1 && MouDiaoChanFirstChild(f, _resolutionStack[index + 1]) && MouDiaoChanObserverSuffix(index) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 31700)]
    private bool ResumeMouDiaoChan(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouDiaoChanMobile: not null } f || f.Id != id) return false;
        ContinueMouDiaoChan(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 31700)]
    private bool ReturnMouDiaoChanMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeMouDiaoChan(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 31700)]
    private bool AdvanceMouDiaoChanChildren() => _pendingDecision is null && MouDiaoChanObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 31700)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 31700)]
    private bool ContinueAcceptedMouDiaoChan(ProgramSkillFrame f) => ValidMouDiaoChan(f) && f.MouDiaoChanMobile is { Paid: true };
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 31700)]
    private void AssertMouDiaoChan(ProgramSkillFrame f)
    { if (f.MouDiaoChanMobile is not null && !ValidMouDiaoChan(f)) throw new InvalidOperationException("Mou Diao Chan lost its paid source or native effect cursor."); }
}
