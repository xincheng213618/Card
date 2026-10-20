namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidMouGsz(ProgramSkillFrame f)
    {
        if (f.MouGongsunZan is not { } r || r.Source != MouGszSource(f) || r.GameplayHash != f.GameplayHash || f.InstructionIndex != 1 || !IsValidPlayerSeat(r.OriginalTarget) ||
            !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence) return false;
        if (f.SkillId == MouGongsunZanComposition.Baojing)
            return f.TriggerId is null && f.ActivationId == "adjust-range" && r.Stage == MouGongsunZanStage.RangeChoice && r.UseId == 0 && r.ActionId == 0 && r.WindowId == 0 &&
                f.SelectedTargetSeats.SequenceEqual([r.OriginalTarget]) && r.Phase is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && phase.ActorSeat == f.OwnerSeat && IsRecordedActualDiscardRecoveryPhase(phase);
        return f.SkillId == MouGongsunZanComposition.Jiaodi && MouGszUse(f) is { } use && use.Id == r.UseId && use.Action!.ActionId == r.ActionId && use.TargetSeats.Contains(r.OriginalTarget) &&
            ProgramEventHistory<MouGongsunZanStartedEvent>().Count(e => e == new MouGongsunZanStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.UseId, r.ActionId, r.OriginalTarget, r.OwnerRange, r.TargetRange)) == 1 &&
            ProgramEventHistory<MouGongsunZanMovedEvent>().Where(e => e.FrameId == f.Id).All(e => _cardMovements.Count(m => m.CardId == e.CardId && m.CardKind == e.PrintedKind && m.From == e.From && m.To == e.To && m.Reason == e.Reason && m.Sequence == e.Sequence) == 1);
    }
    private void SealMouGsz(long id, string purpose)
    {
        var f = GetActiveProgramFrame(id); var r = f.MouGongsunZan!; var after = _movementSequence;
        ReplaceRuntimeTop(f with { MouGongsunZan = r with { After = after } });
        foreach (var m in _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= after).ToArray())
            AdvanceEventRulesAndQueueFact(new MouGongsunZanMovedEvent(id, purpose, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence));
    }
    private bool DrainMouGsz(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 32700)]
    private bool MouGszMovementProof(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => e?.Op == SkillProgramEffectOp.ResolveMouGongsunZanJiaodi && ValidMouGsz(f) && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && f.MouGongsunZan!.Stage is MouGongsunZanStage.ObtainChildren or MouGongsunZanStage.DiscardChildren;
    private bool MouGszLionChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouGsz(f) || f.MouGongsunZan is not { Stage: MouGongsunZanStage.DiscardChildren } r) return false;
        var paid = ProgramEventHistory<MouGongsunZanMovedEvent>().LastOrDefault(e => e.FrameId == f.Id && e.PrintedKind == CardKind.SilverLion && e.From.Zone == CardZoneKind.Equipment && e.Reason.Value == MouGszDiscardReason && e.Sequence > r.Before && e.Sequence <= r.After);
        if (paid?.From.OwnerSeat is not { } owner) return false;
        if (child is RecoveryReplacementFrame recovery) return RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason == paid.Reason && recovery.Attempt.Amount == 1 && recovery.Attempt.SourceSeat == owner && recovery.Attempt.TargetSeat == owner;
        return child is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == owner && hp.Change.TargetSeat == owner &&
            ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(e => e.PlayerSeat == owner && e.ResolutionId > f.Id && e.ResolutionId < hp.Change.Id && e.Reason == paid.Reason && e.RecoveredAmount == hp.Change.Amount && e.RemainingHp == hp.Change.HpAfter);
    }
    private bool MouGszFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouGsz(f) || f.MouGongsunZan!.Stage is not (MouGongsunZanStage.ObtainChildren or MouGongsunZanStage.DiscardChildren)) return false;
        var r = f.MouGongsunZan!;
        if (MouGszLionChild(f, child)) return true;
        if (child is RecoveryReplacementFrame recovery) return ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray());
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp);
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) && moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && ProgramEventHistory<MouGongsunZanMovedEvent>().Any(e => e.FrameId == f.Id && e.CardId == m.CardId && e.Sequence == m.Sequence && e.From == m.From && e.To == m.To && e.Reason == m.Reason));
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id && (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouGszSuffix(int i) => HasObserverSuffix(i, static (g, n) => g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) ||
        g._resolutionStack[n - 1] is ProgramSkillFrame f && g.MouGszFirstChild(f, g._resolutionStack[n]) || NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? MouGszObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouGszFirstChild(f, c), static (g, i) => g.MouGszSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 32700)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 32700)]
    private bool MouGszDamage(long id) => MouGszObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 32700)]
    private bool MouGszBeforeDamage(long id) => MouGszObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 32700)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 32700)]
    private bool MouGszDying() => ActiveDying is { } d && MouGszObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 32700)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 32700)]
    private bool MouGszRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame child)
    {
        if (!ValidMouGsz(f)) return false;
        if (MouGszLionChild(f, child) || ProgramRecoveryReplacementReceiptMatches(f, child, CompleteProgramEventHistory().ToArray())) return true;
        var a = _resolutionStack.FindIndex(x => x.Id == f.Id); var b = _resolutionStack.FindIndex(x => x.Id == child.Id);
        return a >= 0 && b > a + 1 && MouGszFirstChild(f, _resolutionStack[a + 1]) && MouGszSuffix(a) && RecoveryReplacementFrameRidesOn(child, _resolutionStack[b - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 32700)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 32700)]
    private bool KeepAcceptedMouGsz(ProgramSkillFrame f) => f.SkillId == MouGongsunZanComposition.Jiaodi && ValidMouGsz(f);
    [ProgramContinuation(ProgramContinuationKind.Resume, 32700)]
    private bool ResumeMouGsz(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouGongsunZan: not null } f || f.Id != id) return false; ContinueMouGsz(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 32700)] private bool ReturnMouGszMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeMouGsz(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 32700)] private bool AdvanceMouGszChildren() => _pendingDecision is null && MouGszObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 32700)]
    private void AssertMouGsz(ProgramSkillFrame f) { if (f.MouGongsunZan is not null && !ValidMouGsz(f)) throw new InvalidOperationException("Gongsun Zan lost his accepted native designation or paid original movement child."); }
}
