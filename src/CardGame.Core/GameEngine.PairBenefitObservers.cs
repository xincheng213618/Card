namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void CaptureFixedRecipientDraw(long frameId, int targetSeat, CardMoveReason reason, long sequenceBefore, int actualCount)
    {
        var f = GetActiveProgramFrame(frameId); if (f.FixedRecipient is not { } r) return;
        if (!ValidFixedRecipientReceipt(f) || r.DrawIssued || targetSeat != r.RecipientSeat || reason.Value != $"skill-program.{f.SkillId}.{SkillProgramEffectOp.Draw}" ||
            ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect is not
                { Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 3 })
            throw new InvalidOperationException("Fixed-recipient drawing differs from its original issuance.");
        var after = PairBenefitSequence;
        ReplaceRuntimeTop(f with { FixedRecipient = r with { DrawIssued = true, DrawSequenceBefore = sequenceBefore, DrawSequenceAfter = after, ActualDrawCount = actualCount } });
        AdvanceEventRulesAndQueueFact(new FixedRecipientBenefitDrawIssuedEvent(f.Id, r.IssuanceProgramFrameId, r.RecipientSeat, r.DeathReplay, sequenceBefore, after, actualCount));
    }
    private void CaptureFixedRecipientRecovery(long frameId, int owner, int target, int amount)
    {
        var f = GetActiveProgramFrame(frameId); if (f.FixedRecipient is not { } r) return;
        if (!ValidFixedRecipientReceipt(f) || !r.DrawIssued || r.RecoveryHpBefore is not null || owner != f.OwnerSeat || target != r.RecipientSeat || amount is < 1 or > 1 ||
            ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect is not
                { Op: SkillProgramEffectOp.Recover, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 1 })
            throw new InvalidOperationException("Fixed-recipient recovery differs from its original issuance.");
        ReplaceRuntimeTop(f with { FixedRecipient = r with { RecoveryHpBefore = _players[target].Hp, RecoveryAmount = amount } });
        AdvanceEventRulesAndQueueFact(new FixedRecipientBenefitRecoveryRequestedEvent(f.Id, r.IssuanceProgramFrameId, target, r.DeathReplay, _players[target].Hp, amount));
    }
    private bool PairBenefitFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.PairObtain is not null && !ValidPairObtain(f) || f.ShownPairGift is not null && !ValidShownPairGift(f) || f.FixedRecipient is not null && !ValidFixedRecipientReceipt(f)) return false;
        if ((f.PairObtain is { AwaitingMovement: true } || f.ShownPairGift?.Stage is ProgramShownPairGiftStage.AwaitingGift or ProgramShownPairGiftStage.AwaitingReward) &&
            (f.PendingMovementContinuation is not { SubjectSeat: var subject, BeforeCount: 0, CoverageResultBind: null } || subject != f.OwnerSeat)) return false;
        long before, after; string reason; int recipient; ProgramPairObtainPayment? paid = null;
        if (f.PairObtain is { AwaitingMovement: true } pair && f.PendingMovementContinuation is not null)
        {
            paid = pair.Cursor == 0 ? pair.FirstPayment : pair.SecondPayment;
            if (paid is null || paid.SameHand) return false;
            before = paid.SequenceBefore; after = paid.SequenceAfter; recipient = f.OwnerSeat; reason = PairBenefitMoveReason(f, false);
        }
        else if (f.ShownPairGift is { Stage: ProgramShownPairGiftStage.AwaitingGift, SameHand: false } gift && f.PendingMovementContinuation is not null)
        { before = gift.SequenceBefore; after = gift.SequenceAfter; recipient = gift.RecipientSeat!.Value; reason = PairBenefitMoveReason(f, true); }
        else if (f.ShownPairGift is { Stage: ProgramShownPairGiftStage.AwaitingReward } reward && f.PendingMovementContinuation is not null)
        { before = reward.DrawSequenceBefore; after = reward.DrawSequenceAfter; recipient = f.OwnerSeat; reason = $"{PairBenefitMoveReason(f, true)}.reward"; }
        else if (f.FixedRecipient is { DrawIssued: true, RecoveryHpBefore: null } fixedDraw)
        { before = fixedDraw.DrawSequenceBefore; after = fixedDraw.DrawSequenceAfter; recipient = fixedDraw.RecipientSeat; reason = $"skill-program.{f.SkillId}.{SkillProgramEffectOp.Draw}"; }
        else if (f.FixedRecipient is { RecoveryHpBefore: { } hpBefore, RecoveryAmount: 1 } benefit)
        {
            if (child is HpChangedTriggerWindowFrame hp)
                return hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.Program && hp.Change.ParentFrameId == f.Id &&
                    hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == f.OwnerSeat && hp.Change.TargetSeat == benefit.RecipientSeat &&
                    hp.Change.HpBefore == hpBefore && hp.Change.Amount == 1;
            return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery, f) &&
                recovery.Return.Continuation == PostEventContinuation.Program && recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == benefit.RecipientSeat &&
                recovery.Attempt.Amount == 1 && recovery.Attempt.HpBefore == hpBefore && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program &&
                recovery.Attempt.Completion.InstructionIndex == f.InstructionIndex;
        }
        else return false;
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == f.Id && (moved.Batch.AwaitingProgramFrameId is null || moved.Batch.AwaitingProgramFrameId == f.Id) &&
                moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > before && m.Sequence <= after &&
                    (m.Reason.Value == reason && (paid is not null ? m.CardId == paid.CardId && m.From == paid.From &&
                        (m.To == CardLocation.Hand(recipient) || m.To == CardLocation.OutsideGame && paid.From.Zone == CardZoneKind.Equipment)
                        : m.To == CardLocation.Hand(recipient) && (f.ShownPairGift?.Stage == ProgramShownPairGiftStage.AwaitingGift
                            ? m.CardId == f.ShownPairGift.CardId && m.From == CardLocation.Hand(f.OwnerSeat) : m.From == CardLocation.DrawPile)) ||
                     paid is not null && _cardMovements.Any(ox => ox.CardId == paid.CardId && ox.CardKind == CardKind.WoodenOx && ox.From == paid.From &&
                        ox.Sequence > before && ox.Sequence <= after) && m.From == CardLocation.WoodenOxGrain(paid.From.OwnerSeat!.Value) &&
                        m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard));
        if (paid is null || paid.From.Zone != CardZoneKind.Equipment || !_cardMovements.Any(m => m.CardId == paid.CardId && m.CardKind == CardKind.SilverLion &&
            m.From == paid.From && m.Sequence > before && m.Sequence <= after && m.Reason.Value == reason)) return false;
        if (child is HpChangedTriggerWindowFrame lionHp)
            return lionHp.ResumeFrameId == f.Id && lionHp.Continuation == PostEventContinuation.AwaitedProgramMovement && lionHp.Change.ParentFrameId == f.Id &&
                lionHp.Change.Kind == HpChangeKind.Recovery && lionHp.Change.SourceSeat == paid.From.OwnerSeat && lionHp.Change.TargetSeat == paid.From.OwnerSeat && lionHp.Change.Amount == 1;
        return child is RecoveryReplacementFrame lionRecovery && RecoveryReplacementFrameRidesOn(lionRecovery, f) &&
            lionRecovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && lionRecovery.Attempt.SourceSeat == paid.From.OwnerSeat &&
            lionRecovery.Attempt.TargetSeat == paid.From.OwnerSeat && lionRecovery.Attempt.Amount == 1 && lionRecovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            lionRecovery.Attempt.Completion.MoveReason?.Value == reason;
    }
    private ProgramSkillFrame? PairBenefitObserverRoot()
    {
        for (var i = 0; i + 1 < _resolutionStack.Count; i++)
        {
            if (_resolutionStack[i] is not ProgramSkillFrame f || !PairBenefitFirstChild(f, _resolutionStack[i + 1])) continue;
            var valid = true;
            for (var n = i + 1; n < _resolutionStack.Count; n++)
            {
                if (!PaidObserverDamageDyingFaceEdge(n) && !EquipmentDonationDamageObserverEdge(n) &&
                    !(_resolutionStack[n - 1] is ProgramSkillFrame p && _resolutionStack[n] is DyingFrame { Continuation: DyingContinuationKind.AttackHpLoss } loss && PaidObserverAttackHpLossDyingMatches(p, loss)) &&
                    !(_resolutionStack[n - 1] is DyingFrame dying && _resolutionStack[n] is ProgramSkillFrame response && PaidObserverDamageDyingProgramMatches(response, dying)))
                { valid = false; break; }
                if (_resolutionStack[n] is DyingFrame d && (IsPaidHandRepaymentProgramAlcoholRide(n, d) || IsPaidHandRepaymentRescueRide(n, d) ||
                    PolicyCounterspellVirtualAlcoholRide(n, d) || PaidObserverDamageVirtualAlcoholRide(n, d))) break;
            }
            if (valid) return f;
        }
        return null;
    }
    private bool IsPairBenefitProgramDying() => ActiveDying is { } dying && PairBenefitObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.FrameId) > _resolutionStack.FindIndex(f => f.Id == root.Id);

    private bool HasPairBenefitDamageObserver(long damageWindowId)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == damageWindowId && f is DamageTriggerWindowFrame);
        return index >= 2 && _resolutionStack[index] is DamageTriggerWindowFrame window &&
            _resolutionStack[index - 1] is DamageFrame damage && damage.Id == window.ParentFrameId &&
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == damage.ParentFrameId) is { AttackAttempt: not null, AttackReturn: not null } attack &&
            CurrentDamageAttempt?.ResolutionId == attack.Id && PairBenefitObserverRoot() is { } root &&
            _resolutionStack.FindIndex(f => f.Id == root.Id) < _resolutionStack.FindIndex(f => f.Id == attack.Id);
    }
}
