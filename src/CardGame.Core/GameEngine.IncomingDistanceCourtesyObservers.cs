namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IncomingDistanceCourtesyFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.IncomingDistanceCourtesy is not { Stage: IncomingDistanceCourtesyStage.MovementChildren, Movements.Count: > 0 } r ||
            root.PendingMovementContinuation is null) return false;
        RequireIncomingDistanceCourtesyReceipt(root); var m = r.Movements[^1];
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == root.Id && moved.ResumeProgramFrameId is null && moved.Batch.AwaitingProgramFrameId == root.Id &&
                moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(move => _cardMovements.Contains(move) &&
                    move.Sequence > m.Before && move.Sequence <= m.After && move.Reason.Value == IncomingDistanceCourtesyReason(root, m.StepIndex));
        if (child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged)
            return skills.ResumeProgramFrameId == root.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count;
        if (child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange)
            return state.ResumeProgramFrameId == root.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program &&
                state.CandidateIndex >= 0 && state.CandidateIndex <= state.Candidates.Count &&
                CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id &&
                    e.Change.ParentFrameId == root.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        var courtesyRecovery = r.Kind == IncomingDistanceCourtesyKind.Courtesy && r.Benefits[r.NextBenefitIndex] == EndingCourtesyBenefit.TurnOwnerRecoverOne;
        var lionRecovery = r.Kind == IncomingDistanceCourtesyKind.FactionDistance && m.From == CardLocation.Equipment(root.OwnerSeat) &&
            m.CardId is { } id && _cardMovements.Any(move => move.CardId == id && move.CardKind == CardKind.SilverLion &&
                move.Sequence > m.Before && move.Sequence <= m.After && move.Reason.Value == IncomingDistanceCourtesyReason(root, 0));
        if (!courtesyRecovery && !lionRecovery) return false;
        var source = root.OwnerSeat; var target = courtesyRecovery ? r.ActualTurnOwnerSeat : root.OwnerSeat;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == root.Id && hp.ResumeFrameId == root.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == source && hp.Change.TargetSeat == target && hp.Change.Amount == 1;
        return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery, root) &&
            recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.SourceSeat == source &&
            recovery.Attempt.TargetSeat == target && recovery.Attempt.Amount == 1 &&
            (courtesyRecovery ? recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program && recovery.Attempt.Completion.InstructionIndex == 1 :
                recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == IncomingDistanceCourtesyReason(root, 0));
    }
    private ProgramSkillFrame? IncomingDistanceCourtesyObserverRoot() => FindProgramObserverRoot(
        static (engine, root, child) => engine.IncomingDistanceCourtesyFirstChild(root, child),
        static (engine, index) => engine.HasHandGainDebtObserverSuffix(index));
    private bool AllowsIncomingDistanceCourtesyNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.SkillsChanged) ||
            IncomingDistanceCourtesyObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var e = GetPausedPrivateOfferEffect(observer);
        return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, e.Target));
    }
    private bool IsIncomingDistanceCourtesyDying() => ActiveDying is { } dying && IncomingDistanceCourtesyObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.FrameId) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasIncomingDistanceCourtesyDamageObserver(long id) => IncomingDistanceCourtesyObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool TryAdvanceIncomingDistanceCourtesySubtree() => _pendingDecision is null && IncomingDistanceCourtesyObserverRoot() is not null &&
        TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
}
