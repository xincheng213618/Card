namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsDuXiRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) =>
        f.DuXiQuxi is { Stage: DuXiQuxiStage.ObtainChildren, ObtainedFrom.Zone: CardZoneKind.Equipment, ObtainedCardId: { } id } r &&
        GetAdvancedCard(id).Kind == CardKind.SilverLion && recovery.ParentFrameId == f.Id &&
        recovery.Return == new RecoveryReplacementReturn(PostEventContinuation.Program, f.Id) &&
        recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == DuXiObtainReason &&
        recovery.Attempt.SourceSeat == r.MoreHandSeat && recovery.Attempt.TargetSeat == r.MoreHandSeat && recovery.Attempt.Amount == 1 &&
        ProgramEventHistory<DuXiQuxiCardObtainedEvent>().Any(e => e.FrameId == f.Id && e.CardId == id && e.SequenceAfter == r.SequenceAfter);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26300)]
    private void AssertDuXi(ProgramSkillFrame f)
    {
        if (f.DuXiQuxi is not { } r) return;
        if (f.SkillId != "ol:quxi" || r.Source != DuXiIssuer(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex ||
            f.WindowContext is not { } context || r.ParentWindowId != context.ParentFrameId || r.ActualTurnNumber != _turnNumber ||
            !Enum.IsDefined(r.Stage) || r.SequenceBefore < 0 || r.SequenceAfter < r.SequenceBefore || r.SequenceAfter > _movementSequence ||
            r.Initial && context.Window != SkillProgramTriggerWindow.PlayEnding ||
            !r.Initial && context.Window is not (SkillProgramTriggerWindow.RoundStarting or SkillProgramTriggerWindow.CharacterDied) ||
            r.Initial && ProgramEventHistory<DuXiQuxiAcceptedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.ActualTurnNumber == r.ActualTurnNumber) != 1)
            throw new InvalidOperationException("Quxi lost its exact issued source, actual lifecycle parent or owning instruction.");
        if (r.LessHandSeat is { } less && (r.MoreHandSeat is not { } more || !IsValidPlayerSeat(less) || !IsValidPlayerSeat(more) || less == more || less == f.OwnerSeat || more == f.OwnerSeat ||
            ProgramEventHistory<DuXiQuxiPairChosenEvent>().Count(e => e.FrameId == f.Id && e.LessHandSeat == less && e.MoreHandSeat == more && e.LessHandCount < e.MoreHandCount) != 1))
            throw new InvalidOperationException("Quxi lost its original unequal-hand other-player pair.");
        if (r.ObtainedCardId is { } id && (r.ObtainedFrom is not { } from || from.OwnerSeat != r.MoreHandSeat ||
            from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || r.SequenceAfter != r.SequenceBefore + 1 ||
            _cardMovements.Count(m => m.Sequence == r.SequenceAfter && m.CardId == id && m.From == from &&
                (m.To == CardLocation.Hand(r.LessHandSeat!.Value) || m.CardKind == CardKind.GeneralWeapon && m.To == CardLocation.OutsideGame) && m.Reason.Value == DuXiObtainReason) != 1 ||
            ProgramEventHistory<DuXiQuxiCardObtainedEvent>().Count(e => e.FrameId == f.Id && e.CardId == id && e.SequenceBefore == r.SequenceBefore && e.SequenceAfter == r.SequenceAfter) != 1))
            throw new InvalidOperationException("Quxi lost its one actual opaque-slot card acquisition payment.");
    }
    private bool DuXiFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.DuXiQuxi is not { } r) return false;
        AssertDuXi(f);
        if (child is CardsMovedTriggerWindowFrame moved)
            return r.Stage == DuXiQuxiStage.ObtainChildren && moved.ResumeProgramFrameId == f.Id && moved.Batch.ParentFrameId == f.Id &&
                moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements is [var movement] && movement.CardId == r.ObtainedCardId && movement.From == r.ObtainedFrom &&
                movement.Reason.Value == DuXiObtainReason && movement.Sequence == r.SequenceAfter && _cardMovements.Contains(movement);
        if (child is RecoveryReplacementFrame recovery)
            return IsDuXiRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp)
            return r.Stage == DuXiQuxiStage.ObtainChildren && r.ObtainedFrom?.Zone == CardZoneKind.Equipment && r.ObtainedCardId is { } lion &&
                GetAdvancedCard(lion).Kind == CardKind.SilverLion && hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id &&
                hp.Continuation == PostEventContinuation.Program && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.TargetSeat == r.MoreHandSeat && hp.Change.Amount == 1;
        if (child is ProgramLifecycleTriggerWindowFrame { CharacterStateContinuation: CharacterStateContinuation.Program } state)
            return r.Stage == DuXiQuxiStage.FaceDownChildren && state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange;
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills &&
            skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private bool DuXiObserverSuffix(int rootIndex) => HasObserverSuffix(rootIndex,
        static (engine, i) => engine.IsMovementObserverEdge(i) || engine.RecoveryReplacementFrameRidesOn(engine._resolutionStack[i], engine._resolutionStack[i - 1]),
        static (engine, i, dying) => engine.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? DuXiObserverRoot() => FindProgramObserverRoot(static (engine, f, c) => engine.DuXiFirstChild(f, c), static (engine, i) => engine.DuXiObserverSuffix(i));
    private bool HasDuXiDamageObserver(long id) => DuXiObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) && _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsDuXiDying() => ActiveDying is { } dying && DuXiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsDuXiNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || DuXiObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 26300)]
    private bool AdvanceDuXiSubtree() => _pendingDecision is null && DuXiObserverRoot() is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
}
