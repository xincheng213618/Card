namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool LiuPiFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.LiuPiExchange is not { } r) return false;
        AssertLiuPiExchange(root);
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.ResumeProgramFrameId == root.Id && moved.Batch.ParentFrameId == root.Id &&
                moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter &&
                    (m.Reason.Value == LiuPiMovementReason(r.Stage) || r.Stage == LiuPiExchangeStage.RevealChildren && m.Reason == CardMoveReasons.Reshuffle &&
                        m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile));
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } changed &&
            changed.ResumeProgramFrameId == root.Id && changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? LiuPiObserverRoot() => FindProgramObserverRoot(
        static (engine, root, child) => engine.LiuPiFirstChild(root, child), static (engine, index) => engine.HasMovementObserverSuffix(index));
    private bool HasLiuPiDamageObserver(long id) => LiuPiObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsLiuPiDying() => ActiveDying is { } dying && LiuPiObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsLiuPiNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || LiuPiObserverRoot() is not { } root || root.Id == f.Id ||
            f.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged)) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 26120)]
    private bool AdvanceLiuPiSubtree() => _pendingDecision is null && LiuPiObserverRoot() is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
}
