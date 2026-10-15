namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool WhirlFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (!CanContinuePrivateWhirl(root) || root.PrivateWhirl is not { } r || child is not CardsMovedTriggerWindowFrame moved) return false;
        return moved.Batch.ParentFrameId == root.Id && moved.Batch.AwaitingProgramFrameId == root.Id && moved.ResumeProgramFrameId is null &&
            moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After && m.Reason.Value.StartsWith("skill-program.private-whirl.", StringComparison.Ordinal));
    }
    private ProgramSkillFrame? PrivateWhirlObserverRoot() => FindProgramObserverRoot(static (engine, root, child) => engine.WhirlFirstChild(root, child), static (engine, index) => engine.SameNameHandObserverSuffix(index));
    private bool HasPrivateWhirlDamageObserver(long id) => PrivateWhirlObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) && _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsPrivateWhirlDying() => ActiveDying is { } dying && PrivateWhirlObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsPrivateWhirlNestedDamage(ProgramSkillFrame observer, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) || PrivateWhirlObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var e = GetPausedPrivateOfferEffect(observer); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature && target == (e.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, e.Target));
    }
    private bool TryAdvancePrivateWhirlSubtree() => _pendingDecision is null && PrivateWhirlObserverRoot() is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
}
