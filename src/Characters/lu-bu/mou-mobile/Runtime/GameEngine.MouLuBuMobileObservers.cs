namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidMouLb(ProgramSkillFrame f) => f.MouLuBuLiyu is { } r && r.Source == MouLbSource(f) && r.GameplayHash == f.GameplayHash && f.InstructionIndex == 1 &&
        f.TriggerId == "obtain-draw-and-choose" && f.ActivationId == f.TriggerId && f.WindowContext is { Window: SkillProgramTriggerWindow.AfterDamageApplied } c &&
        c.ParentFrameId == r.WindowId && c.DamageFrameId == r.DamageFrameId && c.SourceSeat == f.OwnerSeat && c.TargetSeat == r.VictimSeat && c.Amount == r.DamageAmount &&
        Enum.IsDefined(r.Stage) && r.DamageAmount > 0 && r.Selections >= 0 && r.Selections <= r.DamageAmount && r.ActualObtained >= 0 && r.ActualObtained <= r.Selections &&
        r.Before >= 0 && r.After >= r.Before && r.After <= _movementSequence && r.CategoryMask is >= 0 and <= 7 &&
        ProgramEventHistory<MouLuBuLiyuStartedEvent>().Count(e => e == new MouLuBuLiyuStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.DamageFrameId, r.OuterUseId, r.OuterActionId, r.VictimSeat, r.DamageAmount, r.ActualTurn)) == 1 &&
        ProgramEventHistory<MouLuBuLiyuObtainedEvent>().Where(e => e.FrameId == f.Id).All(e => _cardMovements.Count(m => m.Sequence == e.Sequence && m.CardId == e.CardId && m.CardKind == e.PrintedKind && m.From == e.From && m.To == e.To && m.Reason.Value == MouLbObtainReason) == 1) &&
        ProgramEventHistory<MouLuBuLiyuObtainedEvent>().Count(e => e.FrameId == f.Id && e.ActualGain) == r.ActualObtained;
    private void SealMouLb(long id, long before)
    {
        var f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { MouLuBuLiyu = f.MouLuBuLiyu! with { After = _movementSequence } });
        foreach (var m in _cardMovements.Where(m => m.Sequence > before && m.Sequence <= _movementSequence).ToArray())
        {
            var e = new MouLuBuLiyuMovementEvent(id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence);
            if (!ProgramEventHistory<MouLuBuLiyuMovementEvent>().Contains(e)) AdvanceEventRulesAndQueueFact(e);
        }
    }
    private bool DrainMouLb(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 31600)]
    private bool MouLbMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => e?.Op == SkillProgramEffectOp.ResolveMouLuBuLiyu && ValidMouLb(f) &&
        p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool MouLbLionChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouLuBuLiyu is not { Stage: MouLuBuLiyuStage.ObtainChildren } || !ValidMouLb(f)) return false;
        var paid = ProgramEventHistory<MouLuBuLiyuObtainedEvent>().LastOrDefault(e => e.FrameId == f.Id && e.PrintedKind == CardKind.SilverLion && e.From.Zone == CardZoneKind.Equipment);
        if (paid?.From.OwnerSeat is not { } owner || _cardMovements.SingleOrDefault(m => m.Sequence == paid.Sequence) is not { } movement) return false;
        if (child is RecoveryReplacementFrame recovery) return RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason == movement.Reason && recovery.Attempt.Amount == 1 &&
            recovery.Attempt.SourceSeat == owner && recovery.Attempt.TargetSeat == owner;
        return child is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            hp.Change.Kind == HpChangeKind.Recovery && hp.Change.TargetSeat == owner && hp.Change.SourceSeat == owner &&
            ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(e => e.PlayerSeat == owner && e.ResolutionId > f.Id && e.ResolutionId < hp.Change.Id && e.Reason == movement.Reason && e.RecoveredAmount == hp.Change.Amount && e.RemainingHp == hp.Change.HpAfter);
    }
    private bool MouLbFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouLb(f)) return false; var r = f.MouLuBuLiyu!;
        if (child is CardUseFrame use) return r.Stage == MouLuBuLiyuStage.DuelIssued && use.DamageTargetDuelOrigin is { } o && MatchesMouLbDuelParent(f, o);
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouLuBuLiyuStage.Drawing && f.NativeDraw is not null;
        if (MouLbLionChild(f, child)) return true;
        if (child is RecoveryReplacementFrame recovery) return ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray());
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp);
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) && moved.Batch.Movements.All(m =>
            m.Sequence > r.Before && m.Sequence <= r.After && ProgramEventHistory<MouLuBuLiyuMovementEvent>().Contains(new(f.Id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence)));
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouLbNativeEdge(int index)
    {
        if (_resolutionStack[index - 1] is CardUseFrame use && use.DamageTargetDuelOrigin is { } o && IsMouLbDuelOrigin(o) && _resolutionStack[index] is ProgramCardTriggerWindowFrame window)
            return window.ParentFrameId == use.Id && window.Action.ActionId == use.Action!.ActionId;
        if (_resolutionStack[index - 1] is ProgramCardTriggerWindowFrame parent && LifecycleCardUse(parent.ParentFrameId)?.DamageTargetDuelOrigin is { } origin && IsMouLbDuelOrigin(origin) &&
            _resolutionStack[index] is ProgramSkillFrame child && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count)
        { var c = parent.Candidates[parent.CandidateIndex]; return MountObserverCandidateMatches(child, ToSharedCandidate(c)) && child.WindowContext == CreateCardActionProgramContext(parent, c); }
        return false;
    }
    private bool MouLbObserverSuffix(int index) => HasObserverSuffix(index, static (g, n) => g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) ||
        g.IsYanPuDyingExitEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) || g.MouLbNativeEdge(n) ||
        g._resolutionStack[n - 1] is ProgramSkillFrame f && g.MouLbFirstChild(f, g._resolutionStack[n]) || NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) ||
        g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? MouLbObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.MouLbFirstChild(f, child), static (g, n) => g.MouLbObserverSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 31600)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 31600)]
    private bool MouLbDamageObserver(long id) => MouLbObserverRoot() is { } root && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 31600)]
    private bool MouLbBeforeDamageObserver(long id) => MouLbObserverRoot() is { } root && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 31600)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 31600)]
    private bool MouLbDying() => ActiveDying is { } d && MouLbObserverRoot() is { } root && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == root.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 31600)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 31600)]
    private bool MouLbRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (!ValidMouLb(f)) return false;
        if (MouLbLionChild(f, recovery) || ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray())) return true;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return i >= 0 && c > i + 1 && MouLbFirstChild(f, _resolutionStack[i + 1]) && MouLbObserverSuffix(i) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 31600)]
    private void AssertMouLb(ProgramSkillFrame f) { if (f.MouLuBuLiyu is not null && !ValidMouLb(f)) throw new InvalidOperationException("Mobile Liyu lost its actual damage, issued cost or native child receipt."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 31600)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 31600)]
    private bool KeepPaidMouLb(ProgramSkillFrame f) => f.MouLuBuLiyu is { Paid: true } && ValidMouLb(f);
    [ProgramContinuation(ProgramContinuationKind.Resume, 31600)]
    private bool ResumeMouLb(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouLuBuLiyu: not null } f || f.Id != id) return false; ContinueMouLb(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 31600)]
    private bool ReturnMouLbMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeMouLb(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 31600)]
    private bool AdvanceMouLbChildren() => _pendingDecision is null && MouLbObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
