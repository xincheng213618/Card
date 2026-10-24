namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void ContinueBijingPaidGroup(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { BijingPunish: { Paid: true } } frame || frame.Id != id) return;
        AssertBijingUnpaidSelection(frame);
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return;
        frame = GetActiveProgramFrame(id);
        ReplaceRuntimeTop(frame with { BijingPunish = null, PendingMovementContinuation = null });
        AdvanceRuntimeProgram(id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 7171)]
    private bool ResumeBijingPaidGroup(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { BijingPunish.Paid: true } frame || frame.Id != id) return false;
        ContinueBijingPaidGroup(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 7171)]
    private bool ReturnBijingPaidMovement(ProgramSkillFrame frame) => frame.BijingPunish is { Paid: true } && ResumeBijingPaidGroup(frame.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 7171)]
    private bool IsBijingPaidMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.BijingPunishDiscardPhase && frame.BijingPunish is { Paid: true } state &&
        pending == frame.PendingMovementContinuation && pending.SubjectSeat == state.TurnOwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    private bool BijingLionChild(ProgramSkillFrame frame, ResolutionFrame child)
    {
        if (frame.BijingPunish is not { Paid: true } state || !state.SelectedCardIds.Select((id, i) =>
                GetAdvancedCard(id).Kind == CardKind.SilverLion && state.SelectedLocations[i] == CardLocation.Equipment(state.TurnOwnerSeat)).Any(lion => lion)) return false;
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.bijing-punish");
        if (child is RecoveryReplacementFrame recovery) return RecoveryReplacementFrameRidesOn(recovery, frame) &&
            recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            recovery.Attempt.Completion.MoveReason == reason && recovery.Attempt.Amount == 1 && recovery.Attempt.SourceSeat == state.TurnOwnerSeat && recovery.Attempt.TargetSeat == state.TurnOwnerSeat;
        return child is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == frame.Id && hp.ResumeFrameId == frame.Id &&
            hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == state.TurnOwnerSeat && hp.Change.TargetSeat == state.TurnOwnerSeat &&
            ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(e => e.PlayerSeat == state.TurnOwnerSeat && e.Reason == reason && e.RecoveredAmount == hp.Change.Amount && e.RemainingHp == hp.Change.HpAfter);
    }
    private bool BijingFirstPaidChild(ProgramSkillFrame frame, ResolutionFrame child)
    {
        if (frame.BijingPunish is not { Paid: true } state) return false;
        AssertBijingUnpaidSelection(frame);
        if (BijingLionChild(frame, child)) return true;
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(frame, moved) &&
            moved.Batch.Movements.All(m => m.Sequence > state.Before && m.Sequence <= state.After && _cardMovements.Contains(m));
        if (child is RecoveryReplacementFrame recovery) return ProgramRecoveryReplacementReceiptMatches(frame, recovery, CompleteProgramEventHistory().ToArray());
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(frame, hp);
        return child is ProgramLifecycleTriggerWindowFrame lifecycle && lifecycle.ResumeProgramFrameId == frame.Id &&
            (lifecycle.Window == SkillProgramTriggerWindow.SkillsChanged && lifecycle.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             lifecycle.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == lifecycle.Id && e.Change.ParentFrameId == frame.Id));
    }
    private ProgramSkillFrame? BijingPaidObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.BijingFirstPaidChild(f, child),
        static (g, i) => g.HasObserverSuffix(i, static (e, n) => e.IsMovementObserverEdge(n) || e.IsSameNameHandObserverEdge(n) || e.IsYanPuDyingExitEdge(n) ||
            e.TypedDamagePostEventEdge(e._resolutionStack[n - 1], e._resolutionStack[n]) || e.RecoveryReplacementFrameRidesOn(e._resolutionStack[n], e._resolutionStack[n - 1]) ||
            NativeDrawAttemptRidesOn(e._resolutionStack[n], e._resolutionStack[n - 1]), static (e, n, d) => e.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 7171)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 7171)]
    private bool IsBijingRecoveryParent(ProgramSkillFrame frame, RecoveryReplacementFrame recovery)
    {
        if (frame.BijingPunish is not { Paid: true }) return false;
        if (BijingLionChild(frame, recovery) || ProgramRecoveryReplacementReceiptMatches(frame, recovery, CompleteProgramEventHistory().ToArray())) return true;
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id); var child = _resolutionStack.FindIndex(f => f.Id == recovery.Id);
        return index >= 0 && child > index + 1 && BijingFirstPaidChild(frame, _resolutionStack[index + 1]) && BijingPaidObserverRoot()?.Id == frame.Id && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 7171)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 7171)]
    private bool HasBijingPaidDamage(long id) => BijingPaidObserverRoot() is { } frame && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == frame.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 7171)]
    private bool HasBijingBeforeDamage(long id) => BijingPaidObserverRoot() is { } frame && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == frame.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 7171)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 7171)]
    private bool IsBijingPaidDying() => ActiveDying is { } dying && BijingPaidObserverRoot() is { } frame && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == frame.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 7171)]
    private bool AdvanceBijingPaidChildren() => _pendingDecision is null && BijingPaidObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 7171)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 7171)]
    private bool KeepPaidBijingGroup(ProgramSkillFrame frame) => frame.BijingPunish is { Paid: true };
}
