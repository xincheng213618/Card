namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidFireTargetBenefit(ProgramSkillFrame f)
    {
        if (f.FireTargetBenefit is not { } r || f.InstructionIndex != r.InstructionIndex ||
            ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!)
                .GetPausedInstruction(f.InstructionIndex).Effect.Op != SkillProgramEffectOp.DrawFireTargetAndGrantTurnUseQuota ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } c ||
            c.ParentFrameId != r.DamageWindowId || c.DamageFrameId != r.DamageFrameId || c.SourceSeat != r.OwnerSeat ||
            c.TargetSeat != r.TargetSeat || r.OwnerSeat != f.OwnerSeat || r.DrawCount is < 0 or > 1 ||
            r.SequenceBefore < 0 || r.SequenceAfter < r.SequenceBefore ||
            r.ActualTurnNumber != _turnNumber || r.ActualTurnOwnerSeat != _currentSeat) return false;
        var parent = _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == r.DamageWindowId);
        if (parent is null || parent.TriggerWindow != SkillProgramTriggerWindow.AfterDamageApplied || parent.ParentFrameId != r.DamageFrameId ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex].ToProgramCandidate())) return false;
        var attack = GetDamageTriggerAttack(parent);
        if (!attack.DamageWasApplied || attack.DamageAmount < 1 || attack.IsSourceLess || GetDamageNature(attack) != DamageNature.Fire ||
            attack.SourceSeat != f.OwnerSeat || attack.TargetSeat != r.TargetSeat || c.Amount != attack.DamageAmount) return false;
        if (r.Source != CreateProgramTurnEffectSource(f) || r.GrantSequence < 0) return false;
        var grants = CompleteProgramEventHistory().OfType<TurnTargetCardQuotaAllowanceGrantedEvent>().Where(e => e.Allowance.ParentFrameId == f.Id).ToArray();
        if (r.GrantSequence == 0)
        { if (grants.Length != 0 || _turnCardUseEffects.TargetCardQuotaAllowances.Any(g => g.ParentFrameId == f.Id)) return false; }
        else
        {
            var grant = _turnCardUseEffects.TargetCardQuotaAllowances.SingleOrDefault(g => g.GrantSequence == r.GrantSequence);
            if (grant is null || grant.ParentFrameId != f.Id || grant.EffectIndex != r.InstructionIndex - 1 || grant.Source != r.Source ||
                grant.TargetSeat != r.TargetSeat || grant.DamageWindowId != r.DamageWindowId || grant.TurnNumber != r.ActualTurnNumber ||
                grant.TurnSeat != r.ActualTurnOwnerSeat || grants is not [var issued] || issued.Allowance != grant) return false;
        }
        var moves = _cardMovements.Where(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter &&
            m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(r.TargetSeat) && m.Reason.Value == FireTargetDrawReason).ToArray();
        return moves.Length == r.DrawCount && CompleteProgramEventHistory().OfType<ProgramFireTargetDrawIssuedEvent>().Count(e =>
            e.ProgramFrameId == f.Id && e.DamageWindowId == r.DamageWindowId && e.OwnerSeat == f.OwnerSeat &&
            e.TargetSeat == r.TargetSeat && e.ActualCount == r.DrawCount && e.SequenceBefore == r.SequenceBefore && e.SequenceAfter == r.SequenceAfter) == 1;
    }
    private void AssertFireTargetBenefit(ProgramSkillFrame f)
    {
        if (f.FireTargetBenefit is not null && !ValidFireTargetBenefit(f))
            throw new InvalidOperationException("A fire-target benefit lost its actual once-issued draw and frozen turn allowance.");
    }
    private ProgramSkillFrame? FireTargetBenefitObserverRoot(long? windowId = null)
    {
        for (var index = 0; index + 2 < _resolutionStack.Count; index++)
        {
        if (
            _resolutionStack[index] is not DamageTriggerWindowFrame { TriggerWindow: SkillProgramTriggerWindow.AfterDamageApplied } window ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            _resolutionStack[index + 1] is not ProgramSkillFrame { FireTargetBenefit: { } r } root ||
            r.DamageWindowId != window.Id || r.DamageFrameId != window.ParentFrameId || !ValidFireTargetBenefit(root) ||
            windowId is { } requested && window.Id != requested && !_resolutionStack.Skip(index + 2).Any(f => f.Id == requested) ||
            !MountObserverCandidateMatches(root, window.Candidates[window.CandidateIndex].ToProgramCandidate()) ||
            root.PendingMovementContinuation is not { CoverageResultBind: null } pending || pending.SubjectSeat != r.TargetSeat ||
            _resolutionStack[index + 2] is not CardsMovedTriggerWindowFrame first ||
            first.Batch.ParentFrameId != root.Id || first.Batch.AwaitingProgramFrameId is { } awaiting && awaiting != root.Id ||
            first.Batch.OriginOwnerSeat != root.OwnerSeat || first.Batch.OriginSkillId != root.SkillId ||
            first.Batch.OriginSkillInstanceId != root.SkillInstanceId || first.Batch.Movements is not [var draw] ||
            draw.Sequence <= r.SequenceBefore || draw.Sequence > r.SequenceAfter || draw.From != CardLocation.DrawPile ||
            draw.To != CardLocation.Hand(r.TargetSeat) || draw.Reason.Value != FireTargetDrawReason || !_cardMovements.Contains(draw)) continue;
        var aligned = true;
        for (var i = index + 3; i < _resolutionStack.Count; i++)
        {
            if (!HalfHandPaidDamageObserverEdge(i)) { aligned = false; break; }
            // Prove the incoming Dying edge before delegating its complete suffix.
            if (_resolutionStack[i] is DyingFrame dying &&
                (IsPaidHandRepaymentRescueRide(i, dying) || IsPaidHandRepaymentProgramAlcoholRide(i, dying) ||
                 PaidObserverDamageVirtualAlcoholRide(i, dying) || TieredRoundZeroDyingRescueRide(i, dying))) break;
        }
        if (aligned) return root;
        }
        return null;
    }
    private bool HasFireTargetBenefitObserver(long windowId) => FireTargetBenefitObserverRoot(windowId) is not null;
    private bool IsFireTargetBenefitProgramDying() => ActiveDamageTrigger is { } w && ActiveDying is { } dying &&
        FireTargetBenefitObserverRoot(w.Id) is not null && _resolutionStack.OfType<DyingFrame>().Any(f => f.Id == dying.FrameId);
}
