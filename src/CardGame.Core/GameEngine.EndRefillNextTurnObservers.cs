namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool EndRefillFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.EndRefillNextTurn is not { Stage: EndRefillNextTurnStage.MovementChildren } r || root.PendingMovementContinuation is null) return false;
        RequireEndRefillNextTurnReceipt(root);
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == root.Id && moved.ResumeProgramFrameId is null && moved.Batch.AwaitingProgramFrameId == root.Id &&
                moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter &&
                    m.Reason.Value == EndRefillReason(root));
        return child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged &&
            skills.ResumeProgramFrameId == root.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
            skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count;
    }
    private ProgramSkillFrame? EndRefillObserverRoot() => FindProgramObserverRoot(
        static (engine, root, child) => engine.EndRefillFirstChild(root, child),
        static (engine, index) => engine.HasHandGainDebtObserverSuffix(index));
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 330)]
    private bool AllowsEndRefillNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.SkillsChanged) ||
            EndRefillObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var effect = GetPausedPrivateOfferEffect(observer);
        return effect is { Op: SkillProgramEffectOp.Damage } && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 120, group: 10)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 230)]
    private bool IsEndRefillDying() => ActiveDying is { } dying && EndRefillObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.FrameId) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1780)]
    private bool HasEndRefillDamageObserver(long id) => EndRefillObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 60)]
    private bool TryAdvanceEndRefillSubtree() => _pendingDecision is null && EndRefillObserverRoot() is not null &&
        TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
}
