namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void SealMouHanDangMovements(long id, long before)
    {
        var f = GetActiveProgramFrame(id); var r = f.MouHanDang!;
        ReplaceRuntimeTop(f with { MouHanDang = r with { After = _movementSequence } });
        foreach (var m in _cardMovements.Where(m => m.Sequence > before && m.Sequence <= _movementSequence).ToArray())
        {
            var fact = new MouHanDangMovementSignedEvent(id, m.CardId, m.From, m.To, m.Reason, m.Sequence);
            if (!ProgramEventHistory<MouHanDangMovementSignedEvent>().Contains(fact)) AdvanceEventRulesAndQueueFact(fact);
        }
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 30700)]
    private bool IsMouHanDangMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        e?.Op is SkillProgramEffectOp.ResolveMouHanDangGongqi or SkillProgramEffectOp.ResolveMouHanDangJiefan && f.MouHanDang is { Stage: MouHanDangStage.DiscardChildren or MouHanDangStage.DrawChildren } &&
        p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && ValidMouHanDang(f);
    private bool MouHanDangFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouHanDang(f) || f.MouHanDang is not { } r) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouHanDangStage.Drawing && f.NativeDraw is not null;
        if (IsMouHanDangSilverLionChild(f, child)) return true;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && IsMouHanDangMovement(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
                moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m) &&
                    ProgramEventHistory<MouHanDangMovementSignedEvent>().Count(e => e == new MouHanDangMovementSignedEvent(f.Id, m.CardId, m.From, m.To, m.Reason, m.Sequence)) == 1);
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool IsMouHanDangSilverLionChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouHanDang is not { Stage: MouHanDangStage.DiscardChildren, PaidCardId: { } id } r || !ValidMouHanDang(f) ||
            ProgramEventHistory<MouHanDangDiscardedEvent>().SingleOrDefault(e => e.FrameId == f.Id && e.Index == r.Index && e.CardId == id && e.Sequence == r.PaymentSequence) is not { From.Zone: CardZoneKind.Equipment } paid ||
            !_cardMovements.Any(m => m.Sequence == paid.Sequence && m.CardId == id && m.CardKind == CardKind.SilverLion && m.From == paid.From && m.To == paid.To && m.Reason.Value == MouHanDangDiscardReason)) return false;
        if (child is RecoveryReplacementFrame recovery)
            return RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == MouHanDangDiscardReason &&
                recovery.Attempt.Amount == 1 && recovery.Attempt.SourceSeat == paid.Seat && recovery.Attempt.TargetSeat == paid.Seat;
        return child is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id &&
            hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.Amount > 0 &&
            hp.Change.SourceSeat == paid.Seat && hp.Change.TargetSeat == paid.Seat && ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(e =>
                e.ResolutionId > f.Id && e.ResolutionId < hp.Change.Id && e.PlayerSeat == paid.Seat && e.Reason.Value == MouHanDangDiscardReason && e.RecoveredAmount == hp.Change.Amount && e.RemainingHp == hp.Change.HpAfter);
    }
    private bool MouHanDangObserverEdge(int n) => IsMovementObserverEdge(n) || IsSameNameHandObserverEdge(n) || IsYanPuDyingExitEdge(n) ||
        TypedDamagePostEventEdge(_resolutionStack[n - 1], _resolutionStack[n]) || _resolutionStack[n - 1] is ProgramSkillFrame f && MouHanDangFirstChild(f, _resolutionStack[n]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[n], _resolutionStack[n - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[n], _resolutionStack[n - 1]);
    private bool MouHanDangObserverSuffix(int index) => _resolutionStack[index + 1] is DyingFrame dying && index + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(index + 1, dying) ||
        HasObserverSuffix(index, static (g, n) => g.MouHanDangObserverEdge(n), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? MouHanDangObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouHanDangFirstChild(f, c), static (g, n) => g.MouHanDangObserverSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 30700)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 30700)]
    private bool HasMouHanDangDamageObserver(long id) => MouHanDangObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 30700)]
    private bool HasMouHanDangBeforeDamageObserver(long id) => MouHanDangObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 30700)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 30700)]
    private bool IsMouHanDangDying() => ActiveDying is { } dying && MouHanDangObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 30700)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 30700)]
    private bool AllowsMouHanDangNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || MouHanDangObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 30700)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 30700)]
    private bool IsMouHanDangRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (IsMouHanDangSilverLionChild(f, recovery)) return true;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return index >= 0 && child > index + 1 && MouHanDangFirstChild(f, _resolutionStack[index + 1]) && MouHanDangObserverSuffix(index) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 30700)]
    private bool ResumeMouHanDang(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouHanDang: not null } f || f.Id != id) return false;
        ContinueMouHanDang(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 30700)]
    private bool ReturnMouHanDangMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeMouHanDang(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 30700)]
    private bool AdvanceMouHanDangChildren() => _pendingDecision is null && MouHanDangObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 30700)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 30700)]
    private bool ContinueAcceptedMouHanDang(ProgramSkillFrame f) => ValidMouHanDang(f) && f.MouHanDang is { Gongqi: false, Branch: not null } or { Gongqi: true, PaidCardId: not null };
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 30700)]
    private void AssertMouHanDang(ProgramSkillFrame f)
    { if (f.MouHanDang is not null && !ValidMouHanDang(f)) throw new InvalidOperationException("Mou Han Dang lost its paid source or native effect cursor."); }
}
