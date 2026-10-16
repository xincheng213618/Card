namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsCaoSongLiluRecoveryParent(ProgramSkillFrame producer, RecoveryReplacementFrame recovery) =>
        producer.CaoSongLilu is { Stage: CaoSongLiluStage.RecoveryChildren, Grows: true, GiftCount: > 0 } receipt &&
        receipt.Source == CaoSongSource(producer) && recovery.ParentFrameId == producer.Id &&
        recovery.Return == new RecoveryReplacementReturn(PostEventContinuation.Program, producer.Id) &&
        recovery.Attempt.Completion == new RecoveryAttemptCompletion(RecoveryAttemptProducer.Program, producer.InstructionIndex) &&
        recovery.Attempt.SourceSeat == producer.OwnerSeat && recovery.Attempt.TargetSeat == producer.OwnerSeat && recovery.Attempt.Amount == 1 &&
        ProgramEventHistory<CaoSongLiluGiftCommittedEvent>().Any(e =>
            e.ProgramFrameId == producer.Id && e.Source == receipt.Source && e.Grows);

    private bool CaoSongLiluFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.CaoSongLilu is not { } receipt) return false;
        AssertCaoSongLilu(root);
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.ResumeProgramFrameId == root.Id && moved.Batch.ParentFrameId == root.Id &&
                moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId &&
                moved.Batch.OriginSkillInstanceId == root.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > receipt.SequenceBefore && m.Sequence <= receipt.SequenceAfter &&
                    m.Reason.Value == (receipt.Stage == CaoSongLiluStage.Drawing ? LiluDrawReason : LiluGiftReason));
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == root.Id && hp.ResumeFrameId == root.Id && hp.Continuation == PostEventContinuation.Program &&
                hp.Change.TargetSeat == root.OwnerSeat && hp.Change.Amount == 1 &&
                (receipt.Stage == CaoSongLiluStage.MaximumChildren && hp.Change.Kind == HpChangeKind.MaximumHp ||
                 receipt.Stage == CaoSongLiluStage.RecoveryChildren && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == root.OwnerSeat);
        if (child is RecoveryReplacementFrame recovery) return IsCaoSongLiluRecoveryParent(root, recovery);
        return child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged &&
            skills.ResumeProgramFrameId == root.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }

    private ProgramSkillFrame? CaoSongLiluObserverRoot() => FindProgramObserverRoot(
        static (engine, root, child) => engine.CaoSongLiluFirstChild(root, child),
        static (engine, index) => engine.HasMovementObserverSuffix(index));

    private bool HasCaoSongLiluDamageObserver(long id) => CaoSongLiluObserverRoot() is { } root &&
        _resolutionStack.Any(frame => frame.Id == id && frame is DamageTriggerWindowFrame) &&
        _resolutionStack.FindIndex(frame => frame.Id == id) > _resolutionStack.FindIndex(frame => frame.Id == root.Id);

    private bool IsCaoSongLiluDying() => ActiveDying is { } dying && CaoSongLiluObserverRoot() is { } root &&
        _resolutionStack.FindIndex(frame => frame.Id == dying.Id) > _resolutionStack.FindIndex(frame => frame.Id == root.Id);

    private bool AllowsCaoSongLiluNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or
                SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or
                SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.SkillsChanged) ||
            CaoSongLiluObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var effect = GetPausedPrivateOfferEffect(observer);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 26030)]
    private bool TryAdvanceCaoSongLiluSubtree() => _pendingDecision is null && CaoSongLiluObserverRoot() is not null &&
        TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
}
