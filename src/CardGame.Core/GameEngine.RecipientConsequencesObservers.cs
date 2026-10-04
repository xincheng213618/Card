namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool RecipientConsequencesFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        AssertRecipientConsequences(f);
        if (child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count;
        if (f.BlackGiftContest is { } r)
        {
            if (r.Stage == BlackGiftContestStage.Pindian && child is PindianFrame p)
                return p.Id == r.PindianFrameId && p.ParentFrameId == f.Id && p.SkillId == f.SkillId && p.SourceSeat == r.RecipientSeat &&
                    p.OpponentSeat == r.SecondSeat && p.ProgramResultBind == BlackGiftResultBind(f);
            if (r.Stage == BlackGiftContestStage.LossPaid && r.Losses.LastOrDefault() is { } loss)
            {
                if (child is DyingFrame dying)
                    return dying.ParentFrameId == f.Id && dying.Continuation == DyingContinuationKind.ProgramSkill &&
                        dying.VictimSeat == loss.Seat && dying.KillerSeat is null && loss.AfterHp == 0 &&
                        CompleteProgramEventHistory().OfType<PlayerDyingEvent>().Count(e => e.ResolutionId == dying.Id && e.VictimSeat == loss.Seat && e.KillerSeat is null) == 1;
                return child is HpChangedTriggerWindowFrame hp && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.Program &&
                    hp.Change.ParentFrameId == f.Id && hp.Change.Kind == HpChangeKind.Loss && hp.Change.TargetSeat == loss.Seat &&
                    hp.Change.HpBefore == loss.BeforeHp && hp.Change.HpAfter == loss.AfterHp && hp.Change.Amount == 1;
            }
            if (child is CardsMovedTriggerWindowFrame moved)
            {
                if (moved.ResumeProgramFrameId is not null || moved.Batch.AwaitingProgramFrameId != f.Id || moved.Batch.OriginOwnerSeat != f.OwnerSeat ||
                    moved.Batch.OriginSkillId != f.SkillId || moved.Batch.OriginSkillInstanceId != f.SkillInstanceId ||
                    moved.Batch.Movements.Count == 0 || !moved.Batch.Movements.All(_cardMovements.Contains)) return false;
                if (r.Stage == BlackGiftContestStage.GiftPaid)
                    return moved.Batch.ParentFrameId == f.Id && moved.Batch.Movements.All(m => m.Sequence > r.GiftBefore && m.Sequence <= r.GiftAfter &&
                        m.CardId == r.GiftCardId && m.From == CardLocation.Hand(f.OwnerSeat) && m.To == CardLocation.Hand(r.RecipientSeat) && m.Reason.Value == BlackGiftReason(f));
                if (r.Stage == BlackGiftContestStage.Pindian && BlackGiftResultMatches(f, out var result))
                    return moved.Batch.ParentFrameId == r.PindianFrameId && moved.Batch.Movements.All(m =>
                        (m.CardId == result.SourceCardId || m.CardId == result.OpponentCardId) &&
                        (m.Reason == CardMoveReasons.PindianReveal && m.To == CardLocation.Processing &&
                            (m.From == CardLocation.Hand(m.CardId == result.SourceCardId ? result.SourceSeat : result.OpponentSeat) || m.From == CardLocation.DrawPile) ||
                         m.Reason == CardMoveReasons.PindianFinish && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile ||
                         m.Reason.Value == "program.pindian.claim" && m.From == CardLocation.Processing &&
                            m.To.Zone == CardZoneKind.Hand && m.To.OwnerSeat is { } claimant && IsValidPlayerSeat(claimant) ||
                         m.Reason.Value == "pindian.reserve-top" && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing));
                if (r.Stage == BlackGiftContestStage.DiscardPaid && r.Discard is { } paid)
                    return moved.Batch.ParentFrameId == f.Id && moved.Batch.Movements.All(m =>
                        (m.Sequence > paid.Before && m.Sequence <= paid.After &&
                         paid.CardIds.Select((id, i) => (id, from: paid.Locations[i])).Any(p => p.id == m.CardId && p.from == m.From &&
                            m.Reason.Value == BlackGiftDiscardReason(f) && (m.To == CardLocation.DiscardPile || m.To == CardLocation.OutsideGame && GetAdvancedCard(m.CardId).IsGeneralWeapon))) ||
                        // Equipment hooks run after the original physical-cost invoice is frozen.
                        (m.Sequence > paid.After && m.From == CardLocation.WoodenOxGrain(paid.WinnerSeat) &&
                         m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard &&
                         paid.CardIds.Select((id, i) => (id, from: paid.Locations[i])).Any(p => p.from == CardLocation.Equipment(paid.WinnerSeat) &&
                            _cardMovements.Any(cost => cost.Sequence > paid.Before && cost.Sequence <= paid.After && cost.CardId == p.id &&
                                cost.CardKind == CardKind.WoodenOx && cost.From == p.from && cost.To == CardLocation.DiscardPile &&
                                cost.Reason.Value == BlackGiftDiscardReason(f)))));
            }
            if (r.Stage == BlackGiftContestStage.DiscardPaid && r.Discard is { } invoice && invoice.CardIds.Any(id =>
                _cardMovements.Any(m => m.Sequence > invoice.Before && m.Sequence <= invoice.After && m.CardId == id && m.CardKind == CardKind.SilverLion &&
                    m.From == CardLocation.Equipment(invoice.WinnerSeat) && m.Reason.Value == BlackGiftDiscardReason(f))))
            {
                if (child is HpChangedTriggerWindowFrame hp)
                    return hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.ParentFrameId == f.Id &&
                        hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == invoice.WinnerSeat && hp.Change.TargetSeat == invoice.WinnerSeat && hp.Change.Amount == 1;
                return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery, f) &&
                    recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.SourceSeat == invoice.WinnerSeat &&
                    recovery.Attempt.TargetSeat == invoice.WinnerSeat && recovery.Attempt.Amount == 1 && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
                    recovery.Attempt.Completion.MoveReason?.Value == BlackGiftDiscardReason(f);
            }
            return false;
        }
        if (f.PrintedLordBenefit is { Stage: PrintedLordBenefitStage.RecoveryPaid } q)
        {
            if (child is RecoveryReplacementFrame recovery)
                return RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.Program &&
                    recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == q.BeneficiarySeat && recovery.Attempt.Amount == q.RecoveryRequested &&
                    recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program && recovery.Attempt.Completion.InstructionIndex == f.InstructionIndex;
            return child is HpChangedTriggerWindowFrame hp && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.Program &&
                hp.Change.ParentFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == f.OwnerSeat &&
                hp.Change.TargetSeat == q.BeneficiarySeat && hp.Change.Amount == q.RecoveryRequested;
        }
        return false;
    }

    private bool RecipientConsequencesPrefix(ProgramSkillFrame f, int last)
    {
        var root = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (root < 0 || last < root || last >= _resolutionStack.Count ||
            f.BlackGiftContest is null && f.PrintedLordBenefit is null ||
            f.BlackGiftContest is not null && !ValidBlackGiftContest(f) || f.PrintedLordBenefit is not null && !ValidPrintedLordBenefit(f)) return false;
        if (root == last) return true;
        if (!RecipientConsequencesFirstChild(f, _resolutionStack[root + 1])) return false;
        for (var i = root + 2; i <= last; i++)
        {
            if (_resolutionStack[i - 1] is DyingFrame d &&
                (IsRoundPricedPileAlcoholRide(i - 1, d) || IsPaidHandRepaymentProgramAlcoholRide(i - 1, d) || IsPaidHandRepaymentRescueRide(i - 1, d) ||
                    PolicyCounterspellVirtualAlcoholRide(i - 1, d) || PaidObserverDamageVirtualAlcoholRide(i - 1, d))) return last == _resolutionStack.Count - 1;
            if (_resolutionStack[i] is ProgramSkillFrame observer && _resolutionStack[i - 1] is ProgramLifecycleTriggerWindowFrame skills &&
                skills.Window == SkillProgramTriggerWindow.SkillsChanged && observer.WindowContext?.ParentFrameId == skills.Id &&
                skills.CandidateIndex >= 0 && skills.CandidateIndex < skills.Candidates.Count &&
                MountObserverCandidateMatches(observer, skills.Candidates[skills.CandidateIndex])) continue;
            if (_resolutionStack[i] is ProgramLifecycleTriggerWindowFrame changed && _resolutionStack[i - 1] is ProgramSkillFrame changedParent &&
                changed.Window == SkillProgramTriggerWindow.SkillsChanged && changed.ResumeProgramFrameId == changedParent.Id &&
                changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && changed.CandidateIndex >= 0 &&
                changed.CandidateIndex <= changed.Candidates.Count) continue;
            if (!DyingSuitsStructuralEdge(_resolutionStack[i - 1], _resolutionStack[i]) && !HalfHandPaidDamageObserverEdge(i) && !PaidTargetObserverEdge(i)) return false;
        }
        return true;
    }
    private ProgramSkillFrame? RecipientConsequencesObserverRoot() => _resolutionStack.OfType<ProgramSkillFrame>()
        .LastOrDefault(f => (f.BlackGiftContest is not null || f.PrintedLordBenefit is not null) && RecipientConsequencesPrefix(f, _resolutionStack.Count - 1));
    private bool IsRecipientConsequencesProgramDying() => ActiveDying is { } d && RecipientConsequencesObserverRoot() is { } root &&
        _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == root.Id);
    private bool HasRecipientConsequencesDamageObserver(long id)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame);
        return index >= 2 && _resolutionStack[index] is DamageTriggerWindowFrame w && _resolutionStack[index - 1] is DamageFrame damage &&
            w.ParentFrameId == damage.Id && _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == damage.ParentFrameId) is { AttackAttempt: not null, AttackReturn: not null } attack &&
            CurrentDamageAttempt?.ResolutionId == attack.Id && RecipientConsequencesObserverRoot() is { } root &&
            _resolutionStack.FindIndex(f => f.Id == root.Id) < _resolutionStack.FindIndex(f => f.Id == attack.Id);
    }
    private bool AllowsRecipientConsequencesNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != observer.Id || observer.InstructionIndex < 1 ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or
                SkillProgramTriggerWindow.SkillsChanged)) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!).GetPausedInstruction(observer.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.Damage || amount != effect.Amount || source != effect.ActorReference || nature != effect.DamageNature ||
            target != (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target))) return false;
        return CurrentDamageAttempt is { } attack && RecipientConsequencesObserverRoot() is { } root &&
            _resolutionStack.FindIndex(f => f.Id == attack.ResolutionId) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    }
    private bool TryAdvanceRecipientConsequencesSubtree()
    {
        if (_pendingDecision is not null || RecipientConsequencesObserverRoot() is null) return false;
        var top = _resolutionStack.LastOrDefault();
        if (top is ProgramSkillFrame { AttackAttempt: not null, AttackReturn: not null } attack)
        {
            if (CurrentDamageAttempt?.ResolutionId != attack.Id || ActiveDying is not null ||
                _resolutionStack.Any(f => f is DamageFrame damage && damage.ParentFrameId == attack.Id || f is BeforeDamageProgramWindowFrame before &&
                    (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == attack.Id)) return false;
            CompleteDamageAttack(new ProgramAttackHandle(this, attack.Id)); AdvanceRulesAndPublishState(); return true;
        }
        if (top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or
            ProgramCardTriggerWindowFrame or BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame)
        { AdvanceRuntimeFrame(top.Id); AdvanceRulesAndPublishState(); return true; }
        if (top is DeathFrame death) { ContinueDeathResolution(death.Id); AdvanceRulesAndPublishState(); return true; }
        return false;
    }
    private void AssertRecipientConsequencesSubtrees()
    {
        foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.BlackGiftContest is not null || f.PrintedLordBenefit is not null))
            if (!RecipientConsequencesPrefix(f, _resolutionStack.Count - 1))
                throw new InvalidOperationException("Recipient consequences lost their continuous exact paid-child subtree.");
    }
}
