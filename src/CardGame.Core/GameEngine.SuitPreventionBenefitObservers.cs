namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool SuitBenefitOriginalCandidate(ProgramSkillFrame root) => IsValidSuitPreventionBenefit(root, true);
    private bool MatchedJudgmentOriginalCandidate(ProgramSkillFrame root)
    {
        if (root.MatchedJudgmentPlacement is null || root.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } context) return false;
        var i = _resolutionStack.FindIndex(f => f.Id == root.Id);
        return i > 0 && _resolutionStack[i - 1] is TurnEndingBoundaryFrame parent && parent.Id == context.ParentFrameId &&
            parent.OwnerSeat == root.OwnerSeat && parent.OwnerSeat == _currentSeat && parent.TurnNumber == _turnNumber &&
            parent.ItemIndex >= 0 && parent.ItemIndex < parent.Items.Count && parent.Items[parent.ItemIndex].Candidate is { } candidate &&
            MountObserverCandidateMatches(root, candidate);
    }
    private bool SuitPlacementFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.SuitPreventionBenefit is { } r)
        {
            if (!SuitBenefitOriginalCandidate(root)) return false;
            if (child is DamageFrame damage) return r.Stage == SuitPreventionBenefitStage.Damaging && SuitPlacementDamageEdge(damage, root);
            if (child is BeforeDamageProgramWindowFrame before) return r.Stage == SuitPreventionBenefitStage.Damaging && SuitPlacementBeforeDamageEdge(before, root);
            if (child is DyingFrame dying) return r.Stage == SuitPreventionBenefitStage.LosingHp && dying.ResumesProgramSkill &&
                dying.ParentFrameId == root.Id && dying.VictimSeat == r.RecipientSeat && dying.KillerSeat is null &&
                CompleteProgramEventHistory().OfType<ProgramSkillHpLostEvent>().Any(e => e.FrameId == root.Id && e.SkillId == root.SkillId &&
                    e.TargetSeat == dying.VictimSeat && e.RemainingHp == 0 && e.Amount == Math.Min(r.HpBefore, SuitBenefitEffect(root).Amount));
            if (child is RecoveryReplacementFrame recovery)
                return r.Stage == SuitPreventionBenefitStage.CostPaid && r.CostFrom == CardLocation.Equipment(root.OwnerSeat) &&
                    _cardMovements.Any(m => m.Sequence == r.CostMovementSequence && m.CardId == r.CostCardId && m.CardKind == CardKind.SilverLion) &&
                    RecoveryReplacementFrameRidesOn(recovery, root) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                    recovery.Attempt.SourceSeat == root.OwnerSeat && recovery.Attempt.TargetSeat == root.OwnerSeat && recovery.Attempt.Amount == 1 &&
                    recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
                    recovery.Attempt.Completion.MoveReason?.Value == $"skill-program.{root.SkillId}.{SkillProgramEffectOp.DiscardSuitPreventDamageAndBenefit}";
            if (child is HpChangedTriggerWindowFrame hp)
                return hp.ResumeFrameId == root.Id && hp.Change.ParentFrameId == root.Id &&
                    hp.Continuation is PostEventContinuation.Program or PostEventContinuation.AwaitedProgramMovement &&
                    (r.Stage == SuitPreventionBenefitStage.CostPaid && r.CostFrom == CardLocation.Equipment(root.OwnerSeat) &&
                     _cardMovements.Any(m => m.Sequence == r.CostMovementSequence && m.CardId == r.CostCardId && m.CardKind == CardKind.SilverLion) &&
                     hp.Change.Kind == HpChangeKind.Recovery && hp.Change.TargetSeat == root.OwnerSeat && hp.Change.Amount == 1 ||
                     r.Stage == SuitPreventionBenefitStage.LosingHp && hp.Change.Kind == HpChangeKind.Loss && hp.Change.TargetSeat == r.RecipientSeat &&
                     hp.Change.HpBefore == r.HpBefore && hp.Change.HpAfter == Math.Max(0, r.HpBefore - SuitBenefitEffect(root).Amount));
            if (child is CardsMovedTriggerWindowFrame movement)
                return SuitPlacementMovementRoot(root, movement) && movement.Batch.Movements.All(m =>
                    r.Stage == SuitPreventionBenefitStage.CostPaid ? m.Sequence == r.CostMovementSequence && m.CardId == r.CostCardId &&
                        m.From == r.CostFrom && m.To == CardLocation.DiscardPile :
                    r.Stage == SuitPreventionBenefitStage.Drawing ? m.Sequence > r.MovementSequenceBefore && m.Sequence <= r.MovementSequenceAfter &&
                        (m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(r.RecipientSeat!.Value) && m.Reason.Value == $"skill-program.{root.SkillId}.suit-prevention-draw" ||
                         m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle) :
                    r.Stage == SuitPreventionBenefitStage.Gifting && m.CardId == r.CostCardId && m.From == CardLocation.DiscardPile &&
                        m.To == CardLocation.Hand(r.RecipientSeat!.Value) && m.Reason.Value == $"skill-program.{root.SkillId}.suit-prevention-gift");
            return false;
        }
        if (root.MatchedJudgmentPlacement is not { } placed || !MatchedJudgmentOriginalCandidate(root)) return false;
        if (child is CardsMovedTriggerWindowFrame moved)
            return SuitPlacementMovementRoot(root, moved) && moved.Batch.Movements.All(m =>
                placed.Stage == MatchedJudgmentPlacementStage.ChoosingDestination ? m.Sequence == placed.ResultMovementSequence && m.CardId == placed.CardId &&
                    m.From == CardLocation.Judgment(root.OwnerSeat) && m.To == placed.ResultFrom && m.Reason.Value == "skill-program.judgment.result" :
                placed.Stage == MatchedJudgmentPlacementStage.Placed ? m.Sequence == placed.PlacementMovementSequence && m.CardId == placed.CardId &&
                    m.From == placed.ResultFrom && m.To == (placed.OnTop ? CardLocation.DrawPile : CardLocation.Hand(placed.RecipientSeat!.Value)) :
                placed.Stage == MatchedJudgmentPlacementStage.SelfDiscarded && m.Sequence == placed.SelfDiscardMovementSequence &&
                    m.CardId == placed.SelfDiscardCardId && m.From == placed.SelfDiscardFrom && m.To == CardLocation.DiscardPile);
        if (child is HpChangedTriggerWindowFrame selfHp)
            return placed.Stage == MatchedJudgmentPlacementStage.SelfDiscarded && placed.SelfDiscardFrom == CardLocation.Equipment(root.OwnerSeat) &&
                _cardMovements.Any(m => m.Sequence == placed.SelfDiscardMovementSequence && m.CardId == placed.SelfDiscardCardId && m.CardKind == CardKind.SilverLion) &&
                selfHp.ResumeFrameId == root.Id && selfHp.Change.ParentFrameId == root.Id && selfHp.Change.TargetSeat == root.OwnerSeat &&
                selfHp.Change.Kind == HpChangeKind.Recovery && selfHp.Change.Amount == 1 && selfHp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        if (child is RecoveryReplacementFrame selfRecovery)
            return placed.Stage == MatchedJudgmentPlacementStage.SelfDiscarded && placed.SelfDiscardFrom == CardLocation.Equipment(root.OwnerSeat) &&
                _cardMovements.Any(m => m.Sequence == placed.SelfDiscardMovementSequence && m.CardId == placed.SelfDiscardCardId && m.CardKind == CardKind.SilverLion) &&
                RecoveryReplacementFrameRidesOn(selfRecovery, root) && selfRecovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                selfRecovery.Attempt.SourceSeat == root.OwnerSeat && selfRecovery.Attempt.TargetSeat == root.OwnerSeat && selfRecovery.Attempt.Amount == 1 &&
                selfRecovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
                selfRecovery.Attempt.Completion.MoveReason?.Value == $"skill-program.{root.SkillId}.{SkillProgramEffectOp.PlaceMatchedJudgmentCard}";
        return false;
    }
    private bool SuitPlacementMovementRoot(ProgramSkillFrame root, CardsMovedTriggerWindowFrame child) =>
        child.Batch.Id == child.Id && child.Batch.ParentFrameId == root.Id &&
        (child.Batch.AwaitingProgramFrameId is null || child.Batch.AwaitingProgramFrameId == root.Id) &&
        child.Batch.OriginOwnerSeat == root.OwnerSeat && child.Batch.OriginSkillId == root.SkillId &&
        child.Batch.OriginSkillInstanceId == root.SkillInstanceId && child.Batch.Movements.Count > 0 && child.Batch.Movements.All(m => _cardMovements.Contains(m));
    private bool SuitPlacementDamageEdge(DamageFrame damage, ProgramSkillFrame root) =>
        root.AttackAttempt is { } attack && damage.ParentFrameId == root.Id && damage.SourceSeat == attack.SourceSeat &&
        damage.TargetSeat == attack.TargetSeat && damage.Amount == attack.DamageAmount && damage.Nature == attack.Nature &&
        root.AttackReturn is not null && attack.DamageWasApplied;
    private static bool SuitPlacementBeforeDamageEdge(BeforeDamageProgramWindowFrame before, ProgramSkillFrame root) =>
        root.AttackAttempt is { } attack && (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == root.Id &&
        before.Continuation == BeforeDamageProgramContinuation.Attack && before.SourceSeat == attack.SourceSeat &&
        before.TargetSeat == attack.TargetSeat && before.Amount == attack.DamageAmount && before.Nature == attack.Nature && root.AttackReturn is not null;
    private bool SuitPlacementPipelineEdge(int index)
    {
        var child = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
        if (child is DamageFrame damage && parent is ProgramSkillFrame program) return SuitPlacementDamageEdge(damage, program);
        if (child is BeforeDamageProgramWindowFrame before && parent is ProgramSkillFrame attack) return SuitPlacementBeforeDamageEdge(before, attack);
        if (child is ProgramSkillFrame prevention && parent is BeforeDamageProgramWindowFrame beforeParent)
            return beforeParent.CandidateIndex >= 0 && beforeParent.CandidateIndex < beforeParent.Candidates.Count &&
                MountObserverCandidateMatches(prevention, beforeParent.Candidates[beforeParent.CandidateIndex].Candidate) &&
                prevention.WindowContext is { Window: SkillProgramTriggerWindow.BeforeDamageApplied } c && c.ParentFrameId == beforeParent.Id &&
                c.TargetSeat == beforeParent.TargetSeat && c.Amount == beforeParent.Amount;
        if (child is DamageTriggerWindowFrame after && parent is DamageFrame applied)
            return after.ParentFrameId == applied.Id && after.SourceSeat == applied.SourceSeat && after.TargetSeat == applied.TargetSeat &&
                after.SourceCardId is null && after.SourceCard is null;
        if (child is ProgramSkillFrame effect && parent is DamageTriggerWindowFrame window)
            return window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count &&
                MountObserverCandidateMatches(effect, window.Candidates[window.CandidateIndex].ToProgramCandidate()) &&
                effect.WindowContext is { } c && c.ParentFrameId == window.Id && c.Window == window.TriggerWindow && c.TargetSeat == window.TargetSeat;
        if (child is DyingFrame dying && parent is DamageFrame fatal)
            return dying.Continuation == DyingContinuationKind.Damage && dying.ParentFrameId == fatal.Id && dying.VictimSeat == fatal.TargetSeat &&
                CompleteProgramEventHistory().OfType<PlayerDyingEvent>().Any(e => e.ResolutionId == dying.Id && e.VictimSeat == dying.VictimSeat);
        return PaidTargetObserverEdge(index);
    }
    private ProgramSkillFrame? SuitPlacementObserverRoot(long? beforeDamageId = null)
    {
        for (var i = 1; i + 1 < _resolutionStack.Count; i++)
        {
            if (_resolutionStack[i] is not ProgramSkillFrame root ||
                beforeDamageId is { } beforeId && root.SuitPreventionBenefit?.BeforeDamageFrameId != beforeId ||
                !SuitPlacementFirstChild(root, _resolutionStack[i + 1])) continue;
            var valid = true;
            for (var child = i + 2; child < _resolutionStack.Count; child++)
            {
                if (_resolutionStack[child - 1] is DyingFrame d &&
                    (IsPaidHandRepaymentProgramAlcoholRide(child - 1, d) || IsPaidHandRepaymentRescueRide(child - 1, d) || PolicyCounterspellVirtualAlcoholRide(child - 1, d) || IsRoundPricedPileAlcoholRide(child - 1, d))) break;
                if (!SuitPlacementPipelineEdge(child)) { valid = false; break; }
            }
            if (valid) return root;
        }
        return null;
    }
    private bool HasSuitPreventionBenefitObserver(long beforeDamageId) => SuitPlacementObserverRoot(beforeDamageId) is not null;
    private bool IsSuitPlacementProgramDying() => ActiveDying is { } dying && SuitPlacementObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsSuitPreventionInsideDamageProgramDying()
    {
        if (ActiveDamageTrigger is not { } window || !IsSuitPlacementProgramDying()) return false;
        foreach (var original in _resolutionStack.OfType<BeforeDamageProgramWindowFrame>())
        {
            if (SuitPlacementObserverRoot(original.Id) is not { } root) continue;
            var i = _resolutionStack.FindIndex(f => f.Id == root.SuitPreventionBenefit!.OriginalAttackFrameId);
            if (i > 0 && _resolutionStack[i] is ProgramSkillFrame { AttackAttempt: not null, AttackReturn: { } returned } attack &&
                returned.ParentDamageWindowFrameId == window.Id && _resolutionStack[i - 1] is DamageTriggerWindowFrame parent && parent.Id == window.Id &&
                parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
                MountObserverCandidateMatches(attack, parent.Candidates[parent.CandidateIndex].ToProgramCandidate())) return true;
        }
        return false;
    }
}
