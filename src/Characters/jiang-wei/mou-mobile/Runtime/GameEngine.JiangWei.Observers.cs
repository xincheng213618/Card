namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidMouJw(ProgramSkillFrame f)
    {
        if (f.MouJiangWei is not { } r || f.InstructionIndex != 1 || r.Source != MouJwSource(f) || r.GameplayHash != f.GameplayHash || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.Targets.Distinct().Count() != r.Targets.Count || r.Targets.Any(s => !IsValidPlayerSeat(s)) ||
            ProgramEventHistory<MouJiangWeiStartedEvent>().Count(e => e == new MouJiangWeiStartedEvent(f.Id, r.Source, r.GameplayHash, r.Op, r.WindowId, r.Phase)) != 1) return false;
        if (r.Op == SkillProgramEffectOp.ResolveMouJiangWeiTiaoxin)
        {
            if (f.SkillId != MouJiangWeiComposition.Tiaoxin || f.ActivationId != "taunt" || r.Phase is not { Kind: ActualDiscardRecoveryPhaseKind.Play } p || p.ActorSeat != f.OwnerSeat || !IsRecordedActualDiscardRecoveryPhase(p) || r.Budget is < 0 or > 4 || r.Targets.Count > r.Budget || r.Targets.Contains(f.OwnerSeat) || r.Cursor < 0 || r.Cursor > r.Targets.Count) return false;
            if (r.Accepted && (ProgramEventHistory<MouJiangWeiSelectionClosedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.Count == r.Targets.Count) != 1 || r.Targets.Where((s, i) => !ProgramEventHistory<MouJiangWeiTargetAcceptedEvent>().Any(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.Index == i && e.TargetSeat == s)).Any())) return false;
            if (!r.Accepted && r.Stage != MouJiangWeiStage.Targets) return false;
        }
        else if (r.Op != SkillProgramEffectOp.ResolveMouJiangWeiZhiji || f.SkillId != MouJiangWeiComposition.Zhiji || f.TriggerId != "awakening" || f.ActivationId != f.TriggerId || r.Phase is not { Kind: ActualDiscardRecoveryPhaseKind.Preparation } prep || prep.ActorSeat != f.OwnerSeat || !IsRecordedActualDiscardRecoveryPhase(prep) || f.WindowContext?.ParentFrameId != r.WindowId ||
            !_resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(w => w.Id == r.WindowId && w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex])) || !ProgramEventHistory<MouJiangWeiAwakenedEvent>().Any(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash)) return false;
        if (r.Payment is { } paid && (!_cardMovements.Any(m => m.CardId == paid.CardId && m.CardKind == paid.Kind && m.From == paid.From && m.To == paid.To && m.Sequence == paid.Sequence && m.Reason.Value == MouJwGiftReason) || !ProgramEventHistory<MouJiangWeiGiftPaidEvent>().Any(e => e.FrameId == f.Id && e.Index == r.Cursor && e.Payment == paid))) return false;
        return true;
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 33200)]
    private bool MouJwMovementProof(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => ValidMouJw(f) && p == f.PendingMovementContinuation && e?.Op == f.MouJiangWei!.Op && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && f.MouJiangWei.Stage is MouJiangWeiStage.GiftChildren or MouJiangWeiStage.AwakeningChildren;
    private bool MouJwLionChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouJw(f) || f.MouJiangWei is not { Stage: MouJiangWeiStage.GiftChildren, Payment: { Kind: CardKind.SilverLion, From.Zone: CardZoneKind.Equipment } p } r) return false;
        var seat = p.From.OwnerSeat!.Value;
        if (child is RecoveryReplacementFrame recovery) return RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == MouJwGiftReason && recovery.Attempt.SourceSeat == seat && recovery.Attempt.TargetSeat == seat;
        return child is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == seat && hp.Change.TargetSeat == seat && ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(e => e.PlayerSeat == seat && e.Reason.Value == MouJwGiftReason && e.ResolutionId > f.Id && e.ResolutionId < hp.Change.Id && e.RecoveredAmount == hp.Change.Amount && e.RemainingHp == hp.Change.HpAfter);
    }
    private bool MouJwFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouJw(f)) return false; var r = f.MouJiangWei!;
        if (r.Stage == MouJiangWeiStage.NativeUse && child is CardUseFrame use) return ValidMouJwUse(use, f);
        if (MouJwLionChild(f, child)) return true;
        if (child is RecoveryReplacementFrame recovery) return ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray());
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp);
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is not null && ProgramMovementReturnMatches(f, moved) && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id && (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouJwNativeEdge(int n)
    {
        if (_resolutionStack[n - 1] is CardUseFrame { MouJiangWeiRequest: { } o } use && _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == o.FrameId) is { } parent && ValidMouJwUse(use, parent) && _resolutionStack[n] is ProgramCardTriggerWindowFrame window) return window.ParentFrameId == use.Id && window.Action.ActionId == use.Action!.ActionId;
        if (_resolutionStack[n - 1] is ProgramCardTriggerWindowFrame w && LifecycleCardUse(w.ParentFrameId) is { MouJiangWeiRequest: not null } && _resolutionStack[n] is ProgramSkillFrame f && w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count) return MountObserverCandidateMatches(f, ToSharedCandidate(w.Candidates[w.CandidateIndex])) && f.WindowContext == CreateCardActionProgramContext(w, w.Candidates[w.CandidateIndex]);
        return false;
    }
    private bool MouJwSuffix(int n) => HasObserverSuffix(n, static (g, i) => g.IsMovementObserverEdge(i) || g.IsSameNameHandObserverEdge(i) || g.IsYanPuDyingExitEdge(i) || g.TypedDamagePostEventEdge(g._resolutionStack[i - 1], g._resolutionStack[i]) || g.MouJwNativeEdge(i) || g._resolutionStack[i - 1] is ProgramSkillFrame f && g.MouJwFirstChild(f, g._resolutionStack[i]) || NativeDrawAttemptRidesOn(g._resolutionStack[i], g._resolutionStack[i - 1]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[i], g._resolutionStack[i - 1]), static (g, i, d) => g.IsSuspendedOrPaidRescueObserverSuffix(i, d));
    private ProgramSkillFrame? MouJwObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.MouJwFirstChild(f, child), static (g, n) => g.MouJwSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 33200)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 33200)] private bool MouJwDamage(long id) => MouJwObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 33200)] private bool MouJwBeforeDamage(long id) => MouJwObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 33200)] [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 33200)] private bool MouJwDying() => ActiveDying is { } d && MouJwObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 33200)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 33200)] private bool MouJwRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame child)
    { if (!ValidMouJw(f)) return false; if (MouJwLionChild(f, child) || ProgramRecoveryReplacementReceiptMatches(f, child, CompleteProgramEventHistory().ToArray())) return true; var a = _resolutionStack.FindIndex(x => x.Id == f.Id); var b = _resolutionStack.FindIndex(x => x.Id == child.Id); return a >= 0 && b > a + 1 && MouJwFirstChild(f, _resolutionStack[a + 1]) && MouJwSuffix(a) && RecoveryReplacementFrameRidesOn(child, _resolutionStack[b - 1]); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 33200)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 33200)] private bool KeepMouJwPaid(ProgramSkillFrame f) => f.MouJiangWei is { Accepted: true } && ValidMouJw(f);
    [ProgramContinuation(ProgramContinuationKind.Resume, 33200)] private bool ResumeMouJw(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouJiangWei: not null } f || f.Id != id) return false; ContinueMouJw(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 33200)] private bool ReturnMouJwMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeMouJw(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 33200)] private bool AdvanceMouJwChildren() => _pendingDecision is null && MouJwObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 33200)] private void AssertMouJw(ProgramSkillFrame f) { if (f.MouJiangWei is not null && !ValidMouJw(f)) throw new InvalidOperationException("Mobile Jiang Wei lost its exact activation, charge reservation or typed child return."); }
}
