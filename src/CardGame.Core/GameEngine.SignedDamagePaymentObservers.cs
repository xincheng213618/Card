namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool SignedDamagePaymentFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (!IsValidSignedDamagePayment(root, true) || root.SignedDamagePayment is not { Stage: SignedDamagePaymentStage.Paid } r ||
            root.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != root.OwnerSeat) return false;
        if (child is CardsMovedTriggerWindowFrame movement)
            return movement.Batch.Id == movement.Id && movement.Batch.ParentFrameId == root.Id &&
                (movement.Batch.AwaitingProgramFrameId is null || movement.Batch.AwaitingProgramFrameId == root.Id) &&
                movement.Batch.OriginOwnerSeat == root.OwnerSeat && movement.Batch.OriginSkillId == root.SkillId &&
                movement.Batch.OriginSkillInstanceId == root.SkillInstanceId && movement.Batch.Movements.Count == 1 &&
                movement.Batch.Movements.All(m => m.Sequence == r.CostMovementSequence && m.CardId == r.CostCardId && m.From == r.CostFrom &&
                    m.To == CardLocation.DiscardPile && _cardMovements.Contains(m));
        if (r.CostFrom != CardLocation.Equipment(root.OwnerSeat) || !_cardMovements.Any(m => m.Sequence == r.CostMovementSequence &&
            m.CardId == r.CostCardId && m.CardKind == CardKind.SilverLion && m.From == r.CostFrom && m.To == CardLocation.DiscardPile)) return false;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == root.Id && hp.ResumeFrameId == root.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.Amount == 1 && hp.Change.SourceSeat == root.OwnerSeat && hp.Change.TargetSeat == root.OwnerSeat;
        return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery, root) &&
            recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.SourceSeat == root.OwnerSeat &&
            recovery.Attempt.TargetSeat == root.OwnerSeat && recovery.Attempt.Amount == 1 && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            recovery.Attempt.Completion.MoveReason?.Value == $"skill-program.{root.SkillId}.{SkillProgramEffectOp.DiscardOwnedCardToAdjustCurrentDamage}";
    }
    private ProgramSkillFrame? SignedDamagePaymentObserverRoot(long? exactWindowId = null)
    {
        for (var index = 1; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root || !SignedDamagePaymentFirstChild(root, _resolutionStack[index + 1])) continue;
            if (exactWindowId is { } id && root.SignedDamagePayment!.BeforeDamageFrameId != id &&
                !_resolutionStack.Skip(index + 1).Any(f => f.Id == id && f is BeforeDamageProgramWindowFrame or DamageTriggerWindowFrame)) continue;
            var valid = true;
            for (var child = index + 1; child < _resolutionStack.Count; child++)
            {
                if (!HalfHandPaidDamageObserverEdge(child)) { valid = false; break; }
                // Prove the incoming Dying edge before using a complete mature
                // native/multi-material/provider/zero-entity/round Alcohol suffix.
                if (_resolutionStack[child] is DyingFrame dying && (IsPaidHandRepaymentRescueRide(child, dying) ||
                    IsPaidHandRepaymentProgramAlcoholRide(child, dying) || PolicyCounterspellVirtualAlcoholRide(child, dying) ||
                    PaidObserverDamageVirtualAlcoholRide(child, dying) || IsRoundPricedPileAlcoholRide(child, dying))) break;
            }
            if (valid) return root;
        }
        return null;
    }
    private bool HasSignedDamagePaymentObserver(long exactWindowId) => SignedDamagePaymentObserverRoot(exactWindowId) is not null;
    private bool HasSignedDamagePaymentAttackObserver(IDamageAttempt attack) => SignedDamagePaymentObserverRoot() is { } root &&
        root.SignedDamagePayment!.OriginalAttackFrameId == attack.ResolutionId && root.SignedDamagePayment.SourceSeat == attack.SourceSeat &&
        root.SignedDamagePayment.TargetSeat == attack.TargetSeat && root.SignedDamagePayment.Nature == GetDamageNature(attack);
    private bool IsSignedDamagePaymentProgramDying() => ActiveDying is { } dying && SignedDamagePaymentObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsSignedDamagePaymentNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or
                SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged)) return false;
        var e = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!).GetPausedInstruction(observer.InstructionIndex).Effect;
        return e.Op == SkillProgramEffectOp.Damage && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, e.Target)) &&
            SignedDamagePaymentObserverRoot() is { } root && root.Id != observer.Id && root.SignedDamagePayment!.OriginalAttackFrameId == CurrentDamageAttempt?.ResolutionId;
    }
    private bool TryAdvanceSignedDamagePaymentSubtree()
    {
        if (_pendingDecision is not null || SignedDamagePaymentObserverRoot() is null) return false;
        var top = _resolutionStack.LastOrDefault();
        if (top is ProgramSkillFrame { AttackAttempt: not null } attack)
        {
            if (attack.AttackReturn is null || CurrentDamageAttempt?.ResolutionId != attack.Id || ActiveDying is not null ||
                _resolutionStack.Any(f => f is DamageFrame d && d.ParentFrameId == attack.Id ||
                    f is BeforeDamageProgramWindowFrame b && (b.ContinuationAttackResolutionId ?? b.ParentFrameId) == attack.Id))
                throw new InvalidOperationException("Paid damage child must finish its actual damage/Dying before its program tail.");
            CompleteDamageAttack(new ProgramAttackHandle(this, attack.Id)); AdvanceRulesAndPublishState(); return true;
        }
        if (top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or
            ProgramCardTriggerWindowFrame or BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame)
        { AdvanceRuntimeFrame(top.Id); AdvanceRulesAndPublishState(); return true; }
        if (top is DeathFrame death) { ContinueDeathResolution(death.Id); AdvanceRulesAndPublishState(); return true; }
        // RecoveryReplacement is deliberately handled by its existing earlier dispatch.
        return false;
    }
}
