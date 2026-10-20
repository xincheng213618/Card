namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidMouGn(ProgramSkillFrame f)
    {
        if (f.MouGanNing is not { } r || r.Source != MouGnSource(f) || r.GameplayHash != f.GameplayHash || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.Wrong is < 0 or > 4 || r.Discarded < 0 || r.Discarded > r.Wrong || r.Deposited is < 0 or > 3 || r.GuessedMask is < 0 or > 15 ||
            ProgramEventHistory<MouGanNingStartedEvent>().Count(e => e == new MouGanNingStartedEvent(f.Id, r.Source, r.GameplayHash, r.Op, r.Phase, r.TargetSeat, r.UseId, r.ActionId)) != 1) return false;
        if (r.Op == SkillProgramEffectOp.ResolveMouGanNingWeiTarget)
        {
            if (f.SkillId != MouGanNingMobileComposition.Fenwei || f.TriggerId != "spend-wei" || f.ActivationId != f.TriggerId || r.Phase is not null || f.WindowContext is not { } c || MouGnParent(c) is not { } parent || parent.Id != r.UseId || parent.Action!.ActionId != r.ActionId || r.Targets.Count == 0 || r.Targets.Distinct().Count() != r.Targets.Count || r.Targets.Any(t => !parent.TargetSeats.Contains(t)) || r.TargetIndex < 0 || r.TargetIndex >= r.Targets.Count) return false;
        }
        else if (f.TriggerId is not null || f.WindowContext is not null || r.Phase is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != f.OwnerSeat || !IsRecordedActualDiscardRecoveryPhase(phase) ||
            (r.Op == SkillProgramEffectOp.ResolveMouGanNingQixi ? f.SkillId != MouGanNingMobileComposition.Qixi || f.ActivationId != "guess-suits" || !f.SelectedTargetSeats.SequenceEqual([r.TargetSeat]) : f.SkillId != MouGanNingMobileComposition.Fenwei || f.ActivationId != "place-wei" || f.SelectedTargetSeats.Count != 0)) return false;
        return ProgramEventHistory<MouGanNingMovementEvent>().Where(e => e.FrameId == f.Id).All(e => _cardMovements.Count(m => m.Sequence == e.Sequence && m.CardId == e.CardId && m.CardKind == e.Kind && m.From == e.From && m.To == e.To && m.Reason == e.Reason) == 1) &&
            ProgramEventHistory<MouGanNingDiscardedEvent>().Count(e => e.FrameId == f.Id) == r.Discarded && ProgramEventHistory<MouGanNingWeiPlacedEvent>().Count(e => e.FrameId == f.Id && e.To.Zone == CardZoneKind.PublicPersistentPile && _cardMovements.Any(m => m.Sequence == e.Sequence && m.CardId == e.CardId && m.To == e.To && m.From == e.From)) == r.Deposited;
    }
    private void SealMouGn(long id)
    {
        var f = GetActiveProgramFrame(id); var r = f.MouGanNing!; ReplaceRuntimeTop(f with { MouGanNing = r with { After = _movementSequence } });
        foreach (var m in _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= _movementSequence).ToArray())
        {
            var fact = new MouGanNingMovementEvent(id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence);
            if (!ProgramEventHistory<MouGanNingMovementEvent>().Contains(fact)) AdvanceEventRulesAndQueueFact(fact);
        }
    }
    private bool DrainMouGn(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 31900)]
    private bool IsMouGnMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => e?.Op is SkillProgramEffectOp.ResolveMouGanNingQixi or SkillProgramEffectOp.ResolveMouGanNingFenwei or SkillProgramEffectOp.ResolveMouGanNingWeiTarget && ValidMouGn(f) && f.MouGanNing!.Stage is MouGanNingStage.MovementChildren or MouGanNingStage.DrawChildren && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool MouGnLionChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouGn(f) || f.MouGanNing is not { Stage: MouGanNingStage.MovementChildren } r) return false;
        var paid = ProgramEventHistory<MouGanNingMovementEvent>().LastOrDefault(e => e.FrameId == f.Id && e.Sequence > r.Before && e.Kind == CardKind.SilverLion && e.From.Zone == CardZoneKind.Equipment);
        if (paid?.From.OwnerSeat is not { } owner) return false;
        if (child is RecoveryReplacementFrame recovery) return RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason == paid.Reason && recovery.Attempt.Amount == 1 && recovery.Attempt.SourceSeat == owner && recovery.Attempt.TargetSeat == owner;
        return child is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == owner && hp.Change.TargetSeat == owner && ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(e => e.PlayerSeat == owner && e.ResolutionId > f.Id && e.ResolutionId < hp.Change.Id && e.Reason == paid.Reason && e.RecoveredAmount == hp.Change.Amount && e.RemainingHp == hp.Change.HpAfter);
    }
    private bool MouGnFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouGn(f)) return false; var r = f.MouGanNing!;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouGanNingStage.Drawing && f.NativeDraw is not null;
        if (MouGnLionChild(f, child)) return true;
        if (child is RecoveryReplacementFrame recovery) return ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray());
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp);
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) && moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && ProgramEventHistory<MouGanNingMovementEvent>().Contains(new(f.Id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence)));
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id && (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouGnSuffix(int index) => HasObserverSuffix(index, static (g, n) => g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) || g._resolutionStack[n - 1] is ProgramSkillFrame f && g.MouGnFirstChild(f, g._resolutionStack[n]) || NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? MouGnObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.MouGnFirstChild(f, child), static (g, i) => g.MouGnSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 31900)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 31900)]
    private bool HasMouGnDamage(long id) => MouGnObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 31900)]
    private bool HasMouGnBeforeDamage(long id) => MouGnObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 31900)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 31900)]
    private bool IsMouGnDying() => ActiveDying is { } d && MouGnObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 31900)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 31900)]
    private bool MouGnRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (!ValidMouGn(f)) return false;
        if (MouGnLionChild(f, recovery) || ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray())) return true;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return i >= 0 && c > i + 1 && MouGnFirstChild(f, _resolutionStack[i + 1]) && MouGnSuffix(i) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 31900)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 31900)]
    private bool KeepMouGnAccepted(ProgramSkillFrame f) => f.MouGanNing is { Accepted: true } && ValidMouGn(f);
    [ProgramContinuation(ProgramContinuationKind.Resume, 31900)]
    private bool ResumeMouGn(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouGanNing: not null } f || f.Id != id) return false; ContinueMouGn(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 31900)]
    private bool ReturnMouGnMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeMouGn(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 31900)]
    private bool AdvanceMouGnChildren() => _pendingDecision is null && MouGnObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 31900)]
    private void AssertMouGn(ProgramSkillFrame f) { if (f.MouGanNing is not null && !ValidMouGn(f)) throw new InvalidOperationException("Gan Ning lost its actual source, guess/Wei issue or signed native movement."); }
    private IReadOnlyList<PlayerMarkerSnapshot>? AppendMouGanNingWei(int seat, IReadOnlyList<PlayerMarkerSnapshot>? ordinary) => MouGnWei(seat) is not null ? Array.AsReadOnly((ordinary ?? []).Where(m => m.Kind != PlayerMarkerKind.MouGanNingWei).Append(new PlayerMarkerSnapshot(PlayerMarkerKind.MouGanNingWei, "威", 1)).OrderBy(m => m.Kind).ToArray()) : ordinary;
}
