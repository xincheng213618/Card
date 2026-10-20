namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ExtraEffectsAndRankGuessFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.ExtraEffectsAndRankGuess is not { } r) return false;
        RequireExtraEffectsAndRankGuess(root);
        if (root.PendingMovementContinuation is { } movement && IsExtraEffectsAndRankGuessMovement(root, GetPausedPrivateOfferEffect(root), movement) &&
            child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == root.Id && moved.Batch.AwaitingProgramFrameId == root.Id && moved.ResumeProgramFrameId is null &&
                moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId &&
                moved.Batch.OriginSkillInstanceId == root.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter &&
                    m.Reason.Value is RankGuessRevealReason or RankGuessGainReason or ExtraEffectCostReason);
        return child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged &&
            skills.ResumeProgramFrameId == root.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? ExtraEffectsAndRankGuessObserverRoot() => FindProgramObserverRoot(
        static (engine, root, child) => engine.ExtraEffectsAndRankGuessFirstChild(root, child),
        static (engine, index) => engine.SameNameHandObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1600)]
    private bool HasExtraEffectsAndRankGuessDamageObserver(long id) => ExtraEffectsAndRankGuessObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 10, group: 130)]
    private bool IsExtraEffectsAndRankGuessDying() => ActiveDying is { } dying && ExtraEffectsAndRankGuessObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 360)]
    private bool AllowsExtraEffectsAndRankGuessNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            ExtraEffectsAndRankGuessObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var e = GetPausedPrivateOfferEffect(observer);
        return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 90)]
    private bool TryAdvanceExtraEffectsAndRankGuessSubtree() => _pendingDecision is null && ExtraEffectsAndRankGuessObserverRoot() is not null &&
        TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
}
