namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool RiceFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.RicePile is not { } r) return false; RequireRiceReceipt(root);
        if (child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged)
            return skills.ResumeProgramFrameId == root.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count;
        if (root.PendingMovementContinuation is not null && child is CardsMovedTriggerWindowFrame moved)
        {
            var before = r.Stage == RicePileStage.DrawChildren ? r.DrawBefore : r.Before; var after = r.Stage == RicePileStage.DrawChildren ? r.DrawAfter : r.After;
            return moved.Batch.ParentFrameId == root.Id && moved.Batch.AwaitingProgramFrameId == root.Id && moved.ResumeProgramFrameId is null &&
                moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > before && m.Sequence <= after &&
                    (r.Stage == RicePileStage.DrawChildren ? m.Reason.Value == RiceDrawReason : r.Operation == SkillProgramEffectOp.ReplaceJudgmentFromPublicRice ?
                        m.Reason == CardMoveReasons.ProgramJudgmentOldCard || m.Reason == CardMoveReasons.ProgramJudgmentReplace :
                        m.Reason.Value == (r.Operation == SkillProgramEffectOp.DrawAndStoreTwoPublicRice ? RiceStoreReason : RiceGainReason)));
        }
        if (child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange)
            return state.ResumeProgramFrameId == root.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program &&
                CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == root.Id &&
                    e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        var lion = r.CardIds.Select((id, index) => (id, from: r.From[index])).Any(p => p.from == CardLocation.Equipment(root.OwnerSeat) &&
            _cardMovements.Any(m => m.Sequence > r.Before && m.Sequence <= r.After && m.CardId == p.id && m.CardKind == CardKind.SilverLion && m.From == p.from));
        if (r.Stage != RicePileStage.RecoveryChildren && !lion) return false;
        var continuation = lion ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == root.Id && hp.ResumeFrameId == root.Id && hp.Continuation == continuation && hp.Change.Kind == HpChangeKind.Recovery &&
                hp.Change.SourceSeat == root.OwnerSeat && hp.Change.TargetSeat == root.OwnerSeat && hp.Change.Amount == 1;
        return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery, root) && recovery.Return.Continuation == continuation &&
            recovery.Attempt.SourceSeat == root.OwnerSeat && recovery.Attempt.TargetSeat == root.OwnerSeat && recovery.Attempt.Amount == 1 &&
            (lion ? recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == RiceStoreReason :
                recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program && r.RecoveryIssued);
    }
    private ProgramSkillFrame? RiceObserverRoot() => FindProgramObserverRoot(
        static (engine, root, child) => engine.RiceFirstChild(root, child),
        static (engine, index) => engine.HasMovementObserverSuffix(index));
    private bool AllowsRiceNestedDamage(ProgramSkillFrame observer, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            RiceObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var effect = GetPausedPrivateOfferEffect(observer);
        return effect is { Op: SkillProgramEffectOp.Damage } && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 470)]
    private bool TryAdvanceRiceSubtree() => _pendingDecision is null && RiceObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    private bool HasRiceDamageObserver(long id) => RiceObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsRiceProgramDying() => ActiveDying is { } dying && RiceObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
}
