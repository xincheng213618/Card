namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool RandomAreaFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.RandomAreaAcquisition is not { } r || !ValidRandomAreaAcquisition(root)) return false;
        if (OrderedPrintedSkillLossSkillsChangedEdge(root, child)) return true;
        if (child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange)
            return state.ResumeProgramFrameId == root.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program &&
                ProgramEventHistory<CharacterStateChangedEvent>().Count(e => e.Change.Id == state.Id && e.Change.ParentFrameId == root.Id &&
                    e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window) == 1;
        if (r.Stage != RandomAreaAcquisitionStage.MovementChildren || r.Steps.Count == 0) return false;
        var step = r.Steps[^1];
        if (child is CardsMovedTriggerWindowFrame moved)
        {
            if (moved.Batch.ParentFrameId != root.Id || moved.Batch.AwaitingProgramFrameId != root.Id || moved.ResumeProgramFrameId is not null ||
                moved.Batch.OriginOwnerSeat != root.OwnerSeat || moved.Batch.OriginSkillId != root.SkillId || moved.Batch.OriginSkillInstanceId != root.SkillInstanceId ||
                moved.Batch.NativeDrawInvocation is not null) return false;
            if (moved.Batch.Id == step.MovementBatchId) return moved.Batch.Movements.SequenceEqual(step.Movements);
            // Only the exact paid loss of this Wooden Ox can own its native grain discard.
            return moved.Batch.ParentBatchId == step.MovementBatchId && step.Movements.Any(m => m.CardKind == CardKind.WoodenOx && m.From == CardLocation.Equipment(step.TargetSeat)) &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > step.SequenceAfter &&
                    m.From == CardLocation.WoodenOxGrain(step.TargetSeat) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard);
        }
        var lion = step.Movements.Where(m => m.CardKind == CardKind.SilverLion && m.From == CardLocation.Equipment(step.TargetSeat)).ToArray();
        if (lion.Length != 1) return false;
        if (child is RecoveryReplacementFrame recovery)
            return RecoveryReplacementFrameRidesOn(recovery, root) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                recovery.Attempt.SourceSeat == step.TargetSeat && recovery.Attempt.TargetSeat == step.TargetSeat && recovery.Attempt.Amount == 1 &&
                recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason == RandomAreaReason(root, step.SeatIndex) &&
                TypedRecoveryApplicationBoundsMatch(recovery, CompleteProgramEventHistory().ToArray());
        return child is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == root.Id && hp.ResumeFrameId == root.Id &&
            hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery &&
            hp.Change.SourceSeat == step.TargetSeat && hp.Change.TargetSeat == step.TargetSeat && hp.Change.Amount == 1;
    }
    private bool RandomAreaObserverEdge(int index)
    {
        var parent = _resolutionStack[index - 1]; var child = _resolutionStack[index];
        if (parent is ProgramSkillFrame { RandomAreaAcquisition: not null } root) return RandomAreaFirstChild(root, child);
        if (parent is RecoveryReplacementFrame recovery)
        {
            var history = CompleteProgramEventHistory().ToArray();
            if (child is HpChangedTriggerWindowFrame hp) return TypedRecoveryHpReturnMatches(recovery, hp, history);
            if (child is CardsMovedTriggerWindowFrame moved) return TypedRecoveryRewardReturnMatches(recovery, moved, history);
        }
        return NativeDrawAttemptRidesOn(child, parent) || CharacterTurnedOverFrameRidesOn(child, parent) ||
            OrderedPrintedSkillLossSkillsChangedEdge(parent, child) || TypedDamagePostEventEdge(parent, child) ||
            HalfHandPaidDamageObserverEdge(index) || PreventionDrawObserverEdge(index);
    }
    private ProgramSkillFrame? RandomAreaObserverRoot() => FindProgramObserverRoot(static (g, root, child) => g.RandomAreaFirstChild(root, child),
        static (g, index) => g.HasObserverSuffix(index, static (host, child) => host.RandomAreaObserverEdge(child),
            static (host, child, dying) => host.IsSuspendedOrPaidRescueObserverSuffix(child, dying)));
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 10)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 10)]
    private bool IsRandomAreaRecoveryParent(ProgramSkillFrame frame, RecoveryReplacementFrame recovery) =>
        frame.RandomAreaAcquisition is not null && RandomAreaFirstChild(frame, recovery);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 2010)]
    private bool HasRandomAreaDamageObserver(long id) => RandomAreaObserverRoot() is { } root &&
        (root.RandomAreaAcquisition!.DamageWindowId == id || _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id));
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamagePrelude, 20)]
    private bool HasRandomAreaBeforeDamageObserver(long id) => RandomAreaObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 20, group: 30)]
    private bool IsRandomAreaDying() => ActiveDying is { } dying && RandomAreaObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 80)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1810)]
    private bool AllowsRandomAreaNestedDamage(ProgramSkillFrame frame, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || frame.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != frame.Id || RandomAreaObserverRoot() is not { } root || root.Id == frame.Id)
            return false;
        var effect = TypedRecoveryPausedEffect(frame);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(frame, reference) : ResolveProgramEffectTarget(frame, effect.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 1245)]
    private bool AdvanceRandomAreaChildren() => _pendingDecision is null && RandomAreaObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
