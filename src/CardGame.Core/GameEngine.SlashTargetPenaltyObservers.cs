namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidTargetPenaltyReward(ProgramSkillFrame f)
    {
        if (!ValidTargetPenaltyPayment(f) || f.SlashTargetPenaltyDraft is not { Stage: SlashTargetPenaltyStage.RewardChildren, Recast: true, DrawAttempted: true } d ||
            d.ActualDrawCount < 0 || d.ActualDrawCount > d.PaidCardIds.Count || d.RewardBefore < d.SequenceAfter || d.RewardAfter < d.RewardBefore) return false;
        var movements = _cardMovements.Where(m => m.Sequence > d.RewardBefore && m.Sequence <= d.RewardAfter &&
            m.Reason == CardMoveReasons.RecastDraw && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(d.Identity.TargetSeat)).ToArray();
        return movements.Length == d.ActualDrawCount && CompleteProgramEventHistory().OfType<SlashTargetPenaltyDrawIssuedEvent>().Count(e =>
            e.ProgramFrameId == f.Id && e.TargetSeat == d.Identity.TargetSeat && e.RequestedCount == d.PaidCardIds.Count &&
            e.ActualCount == d.ActualDrawCount && e.SequenceBefore == d.RewardBefore && e.SequenceAfter == d.RewardAfter) == 1;
    }
    private ProgramSkillFrame? TargetPenaltyPaidObserverRoot(long? exactWindow = null)
    {
        for (var i = 1; i < _resolutionStack.Count; i++)
        {
            if (_resolutionStack[i] is not ProgramSkillFrame root || root.SlashTargetPenaltyDraft is not
                { Stage: SlashTargetPenaltyStage.CostChildren or SlashTargetPenaltyStage.RewardChildren } d ||
                !ValidTargetPenaltyPayment(root) || root.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending ||
                pending.SubjectSeat != d.Identity.TargetSeat || exactWindow is { } id && !_resolutionStack.Skip(i + 1).Any(f => f.Id == id &&
                    f is BeforeDamageProgramWindowFrame or DamageTriggerWindowFrame)) continue;
            if (d.Stage == SlashTargetPenaltyStage.RewardChildren && !ValidTargetPenaltyReward(root)) continue;
            if (i == _resolutionStack.Count - 1) return root;
            var first = _resolutionStack[i + 1];
            var lower = d.Stage == SlashTargetPenaltyStage.CostChildren ? d.SequenceBefore : d.RewardBefore;
            var upper = d.Stage == SlashTargetPenaltyStage.CostChildren ? d.SequenceAfter : d.RewardAfter;
            if (first is CardsMovedTriggerWindowFrame moved)
            {
                if (moved.Batch.ParentFrameId != root.Id || moved.Batch.AwaitingProgramFrameId is { } awaiting && awaiting != root.Id ||
                    moved.Batch.OriginSkillId != root.SkillId || moved.Batch.OriginSkillInstanceId != root.SkillInstanceId ||
                    moved.Batch.OriginOwnerSeat != root.OwnerSeat || moved.Batch.Movements.Count == 0 ||
                    moved.Batch.Movements.Any(m => m.Sequence <= lower || m.Sequence > upper || !_cardMovements.Contains(m))) continue;
            }
            else
            {
                var reason = SlashTargetPenaltyCostReason(root, d.Recast);
                if (d.Stage != SlashTargetPenaltyStage.CostChildren || !_cardMovements.Any(m =>
                    m.Sequence > lower && m.Sequence <= upper && d.PaidCardIds.Contains(m.CardId) && m.CardKind == CardKind.SilverLion &&
                    m.From == CardLocation.Equipment(d.Identity.TargetSeat) && m.To == CardLocation.DiscardPile && m.Reason.Value == reason)) continue;
                if (first is HpChangedTriggerWindowFrame hp)
                {
                    if (hp.Change.ParentFrameId != root.Id || hp.ResumeFrameId != root.Id || hp.Continuation != PostEventContinuation.AwaitedProgramMovement ||
                        hp.Change.Kind != HpChangeKind.Recovery || hp.Change.SourceSeat != d.Identity.TargetSeat ||
                        hp.Change.TargetSeat != d.Identity.TargetSeat || hp.Change.Amount != 1) continue;
                }
                else if (first is RecoveryReplacementFrame recovery)
                {
                    if (!RecoveryReplacementFrameRidesOn(recovery, root) || recovery.Return.Continuation != PostEventContinuation.AwaitedProgramMovement ||
                        recovery.Attempt.SourceSeat != d.Identity.TargetSeat || recovery.Attempt.TargetSeat != d.Identity.TargetSeat || recovery.Attempt.Amount != 1 ||
                        recovery.Attempt.Completion.Producer != RecoveryAttemptProducer.SilverLion || recovery.Attempt.Completion.MoveReason?.Value != reason) continue;
                }
                else continue;
            }
            var valid = true;
            for (var j = i + 1; j < _resolutionStack.Count; j++)
            {
                if (!PaidColorDamageClaimObserverEdge(j)) { valid = false; break; }
                if (_resolutionStack[j] is DyingFrame dying && (IsPaidHandRepaymentRescueRide(j, dying) ||
                    IsPaidHandRepaymentProgramAlcoholRide(j, dying) || PolicyCounterspellVirtualAlcoholRide(j, dying) ||
                    PaidObserverDamageVirtualAlcoholRide(j, dying))) break;
            }
            if (valid) return root;
        }
        return null;
    }
    private bool HasTargetPenaltyDamageObserver(long id) => TargetPenaltyPaidObserverRoot(id) is not null;
    private bool IsTargetPenaltyProgramDying() => ActiveDying is { } dying && TargetPenaltyPaidObserverRoot(ActiveDamageTrigger?.Id) is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.FrameId) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsTargetPenaltyNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.AfterHpRecovered or
                SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost) ||
            TargetPenaltyPaidObserverRoot() is not { SlashTargetPenaltyDraft: { } paid } root || root.Id == observer.Id ||
            ActiveCardAttack?.ResolutionId != paid.Identity.CardUseFrameId || CurrentDamageAttempt?.ResolutionId != paid.Identity.CardUseFrameId) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!)
            .GetPausedInstruction(observer.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }
    private bool IsTargetPenaltyMovement(ProgramSkillFrame root, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        pending.BeforeCount == 0 && pending.CoverageResultBind is null && effect?.Op == SkillProgramEffectOp.RequireTargetDiscardOrEquipmentRecast &&
        root.SlashTargetPenaltyDraft is { } d && pending.SubjectSeat == d.Identity.TargetSeat && ValidTargetPenaltyPayment(root) &&
        (d.Stage == SlashTargetPenaltyStage.CostChildren || d.Stage == SlashTargetPenaltyStage.RewardChildren && ValidTargetPenaltyReward(root));
    private bool HasTargetPenaltyUseObserver(long useId) => _resolutionStack.OfType<SlashTargetPenaltyWindowFrame>().Any(w =>
        w.ParentFrameId == useId && SlashTargetPenaltyWindowMatches(w) && (_resolutionStack.LastOrDefault()?.Id == w.Id ||
            _resolutionStack.LastOrDefault() is ProgramSkillFrame f && f.WindowContext?.ParentFrameId == w.Id && SlashTargetPenaltyProgramMatches(f) ||
            TargetPenaltyPaidObserverRoot()?.WindowContext?.ParentFrameId == w.Id));
    private void AssertTargetPenaltyWindows()
    {
        foreach (var w in _resolutionStack.OfType<SlashTargetPenaltyWindowFrame>())
            if (!SlashTargetPenaltyWindowMatches(w)) throw new InvalidOperationException("A target penalty window lost its exact issued targets.");
        foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>())
        {
            if (f.WindowContext?.Window != SkillProgramTriggerWindow.ActualSlashTargetPenalty) continue;
            if (!SlashTargetPenaltyProgramMatches(f) || f.SlashTargetPenaltyDraft is { } d &&
                (d.Identity != f.WindowContext.SlashTargetPenalty || d.Stage != SlashTargetPenaltyStage.Offered && !ValidTargetPenaltyPayment(f) ||
                 d.Stage == SlashTargetPenaltyStage.RewardChildren && !ValidTargetPenaltyReward(f)))
                throw new InvalidOperationException("A target penalty lost its own original instruction/payment.");
        }
    }
}
