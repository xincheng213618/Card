namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsNamedHandChoiceMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation movement)
    {
        if (movement.SubjectSeat != f.OwnerSeat || movement.BeforeCount != 0 || movement.CoverageResultBind is not null) return false;
        if (effect?.Op is SkillProgramEffectOp.RecordNamedPrivateAmbush or SkillProgramEffectOp.NullifyMatchingHandUse &&
            f.NamedAmbush is { Stage: NamedAmbushStage.MovementChildren }) { RequireNamedAmbushReceipt(f); return true; }
        if (effect?.Op == SkillProgramEffectOp.ResolveDamageSourceHandChoice && f.DamageSourceHandChoice is { Stage: DamageSourceHandChoiceStage.MovementChildren })
        { RequireDamageSourceHandChoice(f); return true; }
        return false;
    }
    private bool NamedHandChoiceFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.NamedAmbush is null && root.DamageSourceHandChoice is null) return false;
        var effect = GetPausedPrivateOfferEffect(root);
        if (root.PendingMovementContinuation is { } movement && IsNamedHandChoiceMovement(root, effect, movement))
        {
            if (child is CardsMovedTriggerWindowFrame moved)
            {
                if (moved.Batch.ParentFrameId != root.Id || moved.Batch.AwaitingProgramFrameId != root.Id || moved.ResumeProgramFrameId is not null ||
                    moved.Batch.OriginOwnerSeat != root.OwnerSeat || moved.Batch.OriginSkillId != root.SkillId ||
                    moved.Batch.OriginSkillInstanceId != root.SkillInstanceId || moved.Batch.Movements.Count == 0) return false;
                if (root.NamedAmbush is { } ambush)
                    return moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > ambush.MovementBefore && m.Sequence <= ambush.MovementAfter &&
                        m.CardId == ambush.Deposit!.CardId && m.Reason.Value == (ambush.Removing ? NamedAmbushRemoveReason : NamedAmbushRecordReason));
                var r = root.DamageSourceHandChoice!;
                return moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter &&
                    m.Reason.Value == (r.Draw ? DamageSourceHandDrawReason : DamageSourceHandDiscardReason));
            }
            return child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged &&
                skills.ResumeProgramFrameId == root.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        }
        if (root.DamageSourceHandChoice is not { Stage: DamageSourceHandChoiceStage.DamageIssued } || root.AttackAttempt is not { } attack || root.AttackReturn is null) return false;
        RequireDamageSourceHandChoice(root);
        if (child is BeforeDamageProgramWindowFrame before)
            return before.ParentFrameId == root.Id && before.Continuation == BeforeDamageProgramContinuation.Attack && before.SourceSeat == attack.SourceSeat &&
                (before.RedirectedTargetSeat ?? before.TargetSeat) == attack.TargetSeat && before.Amount > 0 && before.Nature == attack.Nature;
        if (child is DyingFrame { Continuation: DyingContinuationKind.AttackHpLoss } replaced) return PaidObserverAttackHpLossDyingMatches(root, replaced);
        return child is DamageFrame damage && damage.ParentFrameId == root.Id && damage.SourceSeat == attack.SourceSeat && damage.TargetSeat == attack.TargetSeat &&
            damage.Amount == attack.DamageAmount && damage.Nature == attack.Nature;
    }
    private ProgramSkillFrame? NamedHandChoiceObserverRoot() => FindProgramObserverRoot(
        static (engine, root, child) => engine.NamedHandChoiceFirstChild(root, child),
        static (engine, index) => engine.SameNameHandObserverSuffix(index));
    private bool HasNamedHandChoiceDamageObserver(long id) => NamedHandChoiceObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsNamedHandChoiceDying() => ActiveDying is { } dying && NamedHandChoiceObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsNamedHandChoiceNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            NamedHandChoiceObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var e = GetPausedPrivateOfferEffect(observer);
        return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 80)]
    private bool TryAdvanceNamedHandChoiceSubtree() => _pendingDecision is null && NamedHandChoiceObserverRoot() is not null &&
        TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
}
