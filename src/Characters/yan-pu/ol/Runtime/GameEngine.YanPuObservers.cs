namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool YanPuFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.YanPu is not { Movements.Count: > 0 } r || root.PendingMovementContinuation is null) return false;
        AssertYanPu(root); var m = r.Movements[^1];
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == root.Id && (moved.ResumeProgramFrameId == root.Id || moved.Batch.AwaitingProgramFrameId == root.Id) &&
                moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(move => _cardMovements.Contains(move) && move.Sequence > m.Before && move.Sequence <= m.After &&
                    (move.Reason.Value == m.Reason || m.Reason.EndsWith(".draw", StringComparison.Ordinal) && move.Reason == CardMoveReasons.Reshuffle));
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == root.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange)
            return state.ResumeProgramFrameId == root.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == root.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        if (child is RecoveryReplacementFrame recovery) return IsYanPuRecoveryParent(root, recovery);
        if (child is not HpChangedTriggerWindowFrame hp || hp.Change.ParentFrameId != root.Id || hp.ResumeFrameId != root.Id ||
            hp.Continuation != PostEventContinuation.AwaitedProgramMovement || hp.Change.Kind != HpChangeKind.Recovery || hp.Change.Amount < 1) return false;
        return r.Stage == YanPuStage.RecoveryChildren && hp.Change.SourceSeat == root.OwnerSeat && hp.Change.TargetSeat == r.Target ||
            r.Stage == YanPuStage.GiftChildren && r.Promise is { From.Zone: CardZoneKind.Equipment } promise &&
            hp.Change.SourceSeat == root.OwnerSeat && hp.Change.TargetSeat == root.OwnerSeat &&
            _cardMovements.Any(move => move.Sequence == promise.MovementSequence && move.CardId == promise.CardId && move.CardKind == CardKind.SilverLion);
    }
    private bool IsYanPuRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.YanPu is not { } r || !RecoveryReplacementFrameRidesOn(recovery, f) || recovery.Return.Continuation != PostEventContinuation.AwaitedProgramMovement ||
            recovery.Attempt.SourceSeat != f.OwnerSeat || recovery.Attempt.Amount != 1) return false;
        return r.Stage == YanPuStage.RecoveryChildren && recovery.Attempt.TargetSeat == r.Target && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program &&
            recovery.Attempt.Completion.InstructionIndex == 1 || r.Stage == YanPuStage.GiftChildren && r.Promise is { From.Zone: CardZoneKind.Equipment } p &&
            recovery.Attempt.TargetSeat == f.OwnerSeat && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            recovery.Attempt.Completion.MoveReason?.Value == YanPuGiftReason && _cardMovements.Any(m => m.Sequence == p.MovementSequence && m.CardId == p.CardId && m.CardKind == CardKind.SilverLion);
    }
    private ProgramSkillFrame? YanPuObserverRoot() => FindProgramObserverRoot(static (e, root, child) => e.YanPuFirstChild(root, child),
        static (e, index) => e.HasObserverSuffix(index, static (engine, child) => engine.IsMovementObserverEdge(child) ||
            engine.IsYanPuDyingExitEdge(child) || RecoveryReplacementFrameRidesOn(engine._resolutionStack[child], engine._resolutionStack[child - 1]),
            static (engine, child, dying) => engine.IsSuspendedOrPaidRescueObserverSuffix(child, dying)));
    private bool HasYanPuDamageObserver(long id) => YanPuObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsYanPuDying() => ActiveDying is { } dying && YanPuObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsYanPuNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || YanPuObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 26410)]
    private bool AdvanceYanPuSubtree() => _pendingDecision is null && YanPuObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    // The real Dying has already completed. Its immutable return names the
    // immediate still-paused native parent, so arbitrary lifecycle descendants
    // cannot borrow the exit boundary's authority.
    private bool IsYanPuDyingExitEdge(int index) => index > 0 && index < _resolutionStack.Count &&
        _resolutionStack[index] is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.DyingExited,
            Continuation: ProgramLifecycleContinuation.ResumeYanPuDyingExit, YanPuDyingExit: { } ret } window &&
        _resolutionStack[index - 1].Id == ret.DyingParentFrameId && window.OwnerSeat == ret.VictimSeat &&
        ret.Round == _roundNumber && ret.Turn == _turnNumber && ret.TurnOwner == _currentSeat &&
        ProgramEventHistory<DyingResolvedEvent>().Count(e => e.ResolutionId == ret.DyingFrameId && e.VictimSeat == ret.VictimSeat && e.Survived) == 1;
}
