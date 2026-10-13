namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool FixedDistanceDebtFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (!ValidFixedDistanceDebtPayment(root) || root.FixedDistanceDebtPayment is not { CardId: not null } r) return false;
        if (child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged)
            return skills.ResumeProgramFrameId == root.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count;
        if (child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange)
            return state.ResumeProgramFrameId == root.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program &&
                state.CandidateIndex >= 0 && state.CandidateIndex <= state.Candidates.Count &&
                CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id &&
                    e.Change.ParentFrameId == root.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == root.Id && moved.ResumeProgramFrameId is null && moved.Batch.AwaitingProgramFrameId == root.Id &&
                moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter);
        var lion = r.From == CardLocation.Equipment(root.OwnerSeat) && _cardMovements.Any(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter &&
            m.CardId == r.CardId && m.CardKind == CardKind.SilverLion && m.From == r.From && m.To == CardLocation.DiscardPile && m.Reason.Value == FixedDistanceDebtReason);
        if (!lion) return false;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == root.Id && hp.ResumeFrameId == root.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == root.OwnerSeat && hp.Change.TargetSeat == root.OwnerSeat && hp.Change.Amount == 1;
        return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery, root) &&
            recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            recovery.Attempt.SourceSeat == root.OwnerSeat && recovery.Attempt.TargetSeat == root.OwnerSeat && recovery.Attempt.Amount == 1 &&
            recovery.Attempt.Completion.MoveReason?.Value == FixedDistanceDebtReason;
    }
    private ProgramSkillFrame? FixedDistanceDebtObserverRoot() => FindProgramObserverRoot(
        static (engine, root, child) => engine.FixedDistanceDebtFirstChild(root, child),
        static (engine, index) => engine.HasHandGainDebtObserverSuffix(index));
    private bool AllowsFixedDistanceDebtNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.SkillsChanged) ||
            FixedDistanceDebtObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var e = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!).GetPausedInstruction(observer.InstructionIndex).Effect;
        return e.Op == SkillProgramEffectOp.Damage && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, e.Target));
    }
    private bool IsFixedDistanceDebtProgramDying() => ActiveDying is { } dying && FixedDistanceDebtObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasFixedDistanceDebtDamageObserver(long window) => _resolutionStack.Any(f => f.Id == window && f is DamageTriggerWindowFrame) && FixedDistanceDebtObserverRoot() is not null;
    private bool TryAdvanceFixedDistanceDebtSubtree()
    {
        if (_pendingDecision is not null || FixedDistanceDebtObserverRoot() is null) return false;
        return TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
    }
}
