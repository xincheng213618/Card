namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool UnequalHandHpFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.UnequalHandHp is not { Payments.Count: > 0 } r || root.PendingMovementContinuation is null) return false;
        RequireUnequalHandHpReceipt(root); var p = r.Payments[^1];
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == root.Id && moved.ResumeProgramFrameId is null && moved.Batch.AwaitingProgramFrameId == root.Id &&
                moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > p.Before && m.Sequence <= p.After &&
                    m.Reason.Value == UnequalHandHpReason(root, p.Step));
        if (child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged)
            return skills.ResumeProgramFrameId == root.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count;
        if (child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange)
            return state.ResumeProgramFrameId == root.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program &&
                state.CandidateIndex >= 0 && state.CandidateIndex <= state.Candidates.Count &&
                CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == root.Id &&
                    e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        var lion = p.Step == "gift" && p.From == CardLocation.Equipment(root.OwnerSeat) && p.CardId is { } paid &&
            _cardMovements.Any(m => m.Sequence > p.Before && m.Sequence <= p.After && m.CardId == paid && m.CardKind == CardKind.SilverLion &&
                m.From == p.From && m.Reason.Value == UnequalHandHpReason(root, p.Step));
        if (!lion) return false;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == root.Id && hp.ResumeFrameId == root.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == root.OwnerSeat && hp.Change.TargetSeat == root.OwnerSeat && hp.Change.Amount == 1;
        return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery, root) &&
            recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            recovery.Attempt.SourceSeat == root.OwnerSeat && recovery.Attempt.TargetSeat == root.OwnerSeat && recovery.Attempt.Amount == 1 &&
            recovery.Attempt.Completion.MoveReason?.Value == UnequalHandHpReason(root, p.Step);
    }
    private ProgramSkillFrame? UnequalHandHpObserverRoot() => FindProgramObserverRoot(
        static (engine, root, child) => engine.UnequalHandHpFirstChild(root, child),
        static (engine, index) => engine.HasHandGainDebtObserverSuffix(index));
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 310)]
    private bool AllowsUnequalHandHpNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.SkillsChanged) ||
            UnequalHandHpObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var effect = GetPausedPrivateOfferEffect(observer);
        return effect is { Op: SkillProgramEffectOp.Damage } && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 100, group: 10)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 210)]
    private bool IsUnequalHandHpDying() => ActiveDying is { } dying && UnequalHandHpObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.FrameId) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1760)]
    private bool HasUnequalHandHpDamageObserver(long id) => UnequalHandHpObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 40)]
    private bool TryAdvanceUnequalHandHpSubtree() => _pendingDecision is null && UnequalHandHpObserverRoot() is not null &&
        TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
}
