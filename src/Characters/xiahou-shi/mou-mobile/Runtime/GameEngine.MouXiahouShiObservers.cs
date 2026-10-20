namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void SealMouXiahouShiMovements(long id, long before)
    {
        var f = GetActiveProgramFrame(id); var after = _movementSequence;
        ReplaceRuntimeTop(f = f with { MouXiahouShiYanyu = f.MouXiahouShiYanyu is { } y ? y with { After = after } : null,
            MouXiahouShiQiaoshi = f.MouXiahouShiQiaoshi is { } q ? q with { After = after } : null });
        foreach (var m in _cardMovements.Where(m => m.Sequence > before && m.Sequence <= after))
        {
            var fact = new MouXiahouShiMovementSignedEvent(id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence);
            if (!ProgramEventHistory<MouXiahouShiMovementSignedEvent>().Contains(fact)) AdvanceEventRulesAndQueueFact(fact);
        }
    }
    private bool MouXiahouShiMovementSigned(ProgramSkillFrame f, CardMovementRecord m) => _cardMovements.Contains(m) &&
        ProgramEventHistory<MouXiahouShiMovementSignedEvent>().Count(e => e == new MouXiahouShiMovementSignedEvent(f.Id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence)) == 1;
    private bool IsMouXiahouShiMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation pending) => e is not null && MouXiahouShiComposition.Handles(e.Op) &&
        ValidMouXiahouShi(f) && pending == f.PendingMovementContinuation && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        (f.MouXiahouShiYanyu?.Stage is MouXiahouShiYanyuStage.DiscardChildren or MouXiahouShiYanyuStage.DrawChildren ||
         f.MouXiahouShiQiaoshi?.Stage is MouXiahouShiQiaoshiStage.RecoveryChildren or MouXiahouShiQiaoshiStage.DrawChildren);
    private bool DrainMouXiahouShiChildren(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private bool MouXiahouShiFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouXiahouShi(f)) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return f.NativeDraw is not null &&
            (f.MouXiahouShiYanyu?.Stage == MouXiahouShiYanyuStage.Drawing || f.MouXiahouShiQiaoshi?.Stage == MouXiahouShiQiaoshiStage.Drawing);
        if (child is RecoveryReplacementFrame recovery) return IsMouXiahouShiDirectRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp && f.MouXiahouShiQiaoshi is { Stage: MouXiahouShiQiaoshiStage.RecoveryChildren, Branch: "recover-and-draw" } q)
            return ProgramHpReturnMatches(f, hp) && ProgramRecoveryReceiptMatches(f, hp, CompleteProgramEventHistory().ToArray()) && hp.Change.Kind == HpChangeKind.Recovery &&
                hp.Change.SourceSeat == q.DamageSourceSeat && hp.Change.TargetSeat == f.OwnerSeat && hp.Change.HpBefore == q.RecoveryHpBefore;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } pending && IsMouXiahouShiMovement(f, GetPausedPrivateOfferEffect(f), pending))
        {
            var before = f.MouXiahouShiYanyu?.Before ?? f.MouXiahouShiQiaoshi!.Before; var after = f.MouXiahouShiYanyu?.After ?? f.MouXiahouShiQiaoshi!.After;
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
                moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.All(m => m.Sequence > before && m.Sequence <= after && MouXiahouShiMovementSigned(f, m));
        }
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool IsMouXiahouShiDirectRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) =>
        f.MouXiahouShiQiaoshi is { Stage: MouXiahouShiQiaoshiStage.RecoveryChildren, Branch: "recover-and-draw", RecoveryReturned: false } q && ValidMouXiahouShiQiaoshi(f) &&
        recovery.ParentFrameId == f.Id && recovery.Attempt.SourceSeat == q.DamageSourceSeat && recovery.Attempt.TargetSeat == f.OwnerSeat &&
        recovery.Attempt.HpBefore == q.RecoveryHpBefore && ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray());
    private bool MouXiahouShiObserverEdge(int n) => IsMovementObserverEdge(n) || IsSameNameHandObserverEdge(n) || IsYanPuDyingExitEdge(n) ||
        TypedDamagePostEventEdge(_resolutionStack[n - 1], _resolutionStack[n]) || _resolutionStack[n - 1] is ProgramSkillFrame f && MouXiahouShiFirstChild(f, _resolutionStack[n]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[n], _resolutionStack[n - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[n], _resolutionStack[n - 1]);
    private bool MouXiahouShiObserverSuffix(int index) => _resolutionStack[index + 1] is DyingFrame dying && index + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(index + 1, dying) ||
        HasObserverSuffix(index, static (g, n) => g.MouXiahouShiObserverEdge(n), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? MouXiahouShiObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouXiahouShiFirstChild(f, c), static (g, n) => g.MouXiahouShiObserverSuffix(n));
    private bool HasMouXiahouShiDamageObserver(long id) => MouXiahouShiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasMouXiahouShiBeforeDamageObserver(long id) => MouXiahouShiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsMouXiahouShiDying() => ActiveDying is { } dying && MouXiahouShiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsMouXiahouShiNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || MouXiahouShiObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    private bool IsMouXiahouShiRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (IsMouXiahouShiDirectRecoveryParent(f, recovery)) return true;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return index >= 0 && child > index + 1 && MouXiahouShiFirstChild(f, _resolutionStack[index + 1]) && MouXiahouShiObserverSuffix(index) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    private void ContinueMouXiahouShi(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id ||
            f.MouXiahouShiYanyu is null && f.MouXiahouShiQiaoshi is null) return;
        AssertMouXiahouShi(f);
        if (f.MouXiahouShiYanyu?.Stage is MouXiahouShiYanyuStage.TargetChoice or MouXiahouShiYanyuStage.Drawing ||
            f.MouXiahouShiQiaoshi?.Stage is MouXiahouShiQiaoshiStage.SourceChoice or MouXiahouShiQiaoshiStage.Drawing) return;
        if (DrainMouXiahouShiChildren(f)) return;
        f = GetActiveProgramFrame(id);
        if (_winner != Winner.None) { FinishMouXiahouShi(f); return; }
        if (f.MouXiahouShiYanyu is { Stage: MouXiahouShiYanyuStage.DiscardChildren } y)
        {
            if (!_players[f.OwnerSeat].IsAlive) { FinishMouXiahouShi(f); return; }
            ReplaceRuntimeTop(f = f with { MouXiahouShiYanyu = y with { Stage = MouXiahouShiYanyuStage.Drawing }, PendingMovementContinuation = null });
            BeginOperationProgramDraw(f, f.OwnerSeat, 1, new(MouXiahouShiDrawReason)); return;
        }
        if (f.MouXiahouShiQiaoshi is { Stage: MouXiahouShiQiaoshiStage.RecoveryChildren, Branch: "recover-and-draw" } q)
        {
            if (q.RecoveryReturned) throw new InvalidOperationException("Qiaoshi cannot resume an already returned recovery twice.");
            var requests = ProgramEventHistory<ProgramRecoveryRequestedEvent>().Where(e => e.ProgramFrameId == f.Id && e.InstructionIndex == f.InstructionIndex).ToArray();
            var direct = ProgramEventHistory<ProgramRecoveryAppliedEvent>().Where(e => e.ProgramFrameId == f.Id && e.Receipt.InstructionIndex == f.InstructionIndex).ToArray();
            if (q.BaseRecovery > 0 && requests.Length + direct.Length != 1 || q.BaseRecovery == 0 && requests.Length + direct.Length != 0 ||
                requests.Any(e => e.GameplayHash != f.GameplayHash || e.SourceSeat != q.DamageSourceSeat || e.TargetSeat != f.OwnerSeat || e.HpBefore != q.RecoveryHpBefore) ||
                direct.Any(e => e.Receipt.GameplayHash != f.GameplayHash || e.Receipt.SourceSeat != q.DamageSourceSeat || e.Receipt.TargetSeat != f.OwnerSeat || e.Receipt.HpBefore != q.RecoveryHpBefore))
                throw new InvalidOperationException("Qiaoshi must return its single genuine native recovery attempt before drawing.");
            ReplaceRuntimeTop(f = f with { MouXiahouShiQiaoshi = q with { RecoveryReturned = true }, PendingMovementContinuation = null });
            AdvanceEventRulesAndQueueFact(new MouXiahouShiQiaoshiRecoveryReturnedEvent(f.Id, q.DamageSourceSeat, f.OwnerSeat, q.RequestedRecovery));
            if (!_players[q.DamageSourceSeat].IsAlive) { FinishMouXiahouShi(f); return; }
            ReplaceRuntimeTop(f = f with { MouXiahouShiQiaoshi = f.MouXiahouShiQiaoshi! with { Stage = MouXiahouShiQiaoshiStage.Drawing } });
            BeginOperationProgramDraw(f, q.DamageSourceSeat, 2, new(MouXiahouShiQiaoshiDrawReason)); return;
        }
        if (f.MouXiahouShiYanyu?.Stage != MouXiahouShiYanyuStage.DrawChildren && f.MouXiahouShiQiaoshi?.Stage != MouXiahouShiQiaoshiStage.DrawChildren)
            throw new InvalidOperationException("Mou Xiahou Shi cannot advance an unreturned native effect.");
        FinishMouXiahouShi(f);
    }
    private void AssertMouXiahouShi(ProgramSkillFrame f)
    { if ((f.MouXiahouShiYanyu is not null || f.MouXiahouShiQiaoshi is not null) && !ValidMouXiahouShi(f)) throw new InvalidOperationException("Mou Xiahou Shi lost its exact source, actual boundary or paid native receipt."); }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 29800)]
    private void AssertMouXiahouShiFrame(ProgramSkillFrame f) => AssertMouXiahouShi(f);
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 29800)]
    private void AssertMouXiahouShiFrames()
    { foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>()) AssertMouXiahouShi(f); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29800)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29800)]
    private bool PermitAcceptedMouXiahouShi(ProgramSkillFrame f) => PermitIssuedMouXiahouShi(f);
    [ProgramContinuation(ProgramContinuationKind.Resume, 29800)]
    private bool ResumeMouXiahouShi(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.MouXiahouShiYanyu is null && f.MouXiahouShiQiaoshi is null) return false;
        ContinueMouXiahouShi(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 29800)]
    private bool ReturnMouXiahouShiMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeMouXiahouShi(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 29800)]
    private bool AdvanceMouXiahouShiChildren() => _pendingDecision is null && MouXiahouShiObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
