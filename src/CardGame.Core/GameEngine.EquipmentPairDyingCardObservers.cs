namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool EquipmentPairDyingCardChildEdge(int index)
    {
        if (PreventionDrawObserverEdge(index)) return true;
        var child = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
        return child is ProgramSkillFrame response && parent is DyingFrame dying && PolicyCounterspellDyingProgramRidesOn(response, dying) ||
            PolicyCounterspellDyingFaceEdge(index);
    }
    private bool EquipmentPairDyingCardSuffix(int rootIndex)
    {
        for (var index = rootIndex + 1; index < _resolutionStack.Count; index++)
        {
            // Prove the incoming Dying edge before using a complete rescue suffix proof.
            if (!EquipmentPairDyingCardChildEdge(index)) return false;
            if (_resolutionStack[index] is DyingFrame dying &&
                (IsPaidHandRepaymentRescueRide(index, dying) || IsPaidHandRepaymentProgramAlcoholRide(index, dying) ||
                 PolicyCounterspellVirtualAlcoholRide(index, dying))) break;
        }
        return true;
    }
    private bool PaymentRemovedSilverLion(ProgramSkillFrame root, string reason, long before, int? ownerSeat = null) =>
        _cardMovements.Any(m => m.Sequence > before && m.Reason.Value == reason && m.From.Zone == CardZoneKind.Equipment &&
            (ownerSeat is null || m.From.OwnerSeat == ownerSeat) &&
            _cardZones.CardsAt(_cardZones.GetLocation(m.CardId)).Any(c => c.Id == m.CardId && c.Kind == CardKind.SilverLion));

    private ProgramSkillFrame? EquipmentPairPaymentObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root || !EquipmentPairCostIsPaid(root) || root.EquipmentPairPayment is not { } p ||
                root.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != root.OwnerSeat ||
                root.InstructionIndex is not (2 or 3 or 4)) continue;
            var exchanging = root.InstructionIndex == 4;
            if (exchanging && p.ExchangeMovementSequenceBefore is null) continue;
            var before = exchanging ? p.ExchangeMovementSequenceBefore!.Value : p.MovementSequenceBefore;
            var reason = exchanging ? $"skill-program.{root.SkillId}.equipment-exchange" : $"skill-program.{root.SkillId}.{SkillProgramEffectOp.MoveBoundCards}";
            var first = _resolutionStack[index + 1];
            if (first is HpChangedTriggerWindowFrame hp)
            {
                if (hp.ResumeFrameId != root.Id || hp.Continuation != PostEventContinuation.AwaitedProgramMovement || hp.Change.ParentFrameId != root.Id ||
                    hp.Change.Kind != HpChangeKind.Recovery || hp.Change.Amount != 1 || hp.Change.SourceSeat != hp.Change.TargetSeat ||
                    (exchanging ? hp.Change.TargetSeat != p.FirstSeat && hp.Change.TargetSeat != p.SecondSeat : hp.Change.TargetSeat != root.OwnerSeat) ||
                    !PaymentRemovedSilverLion(root, reason, before, hp.Change.TargetSeat)) continue;
            }
            else if (first is RecoveryReplacementFrame recovery)
            {
                if (!RecoveryReplacementFrameRidesOn(recovery, root) || recovery.Return.Continuation != PostEventContinuation.AwaitedProgramMovement ||
                    recovery.Attempt.Completion.Producer != RecoveryAttemptProducer.SilverLion || recovery.Attempt.Completion.MoveReason?.Value != reason ||
                    recovery.Attempt.SourceSeat != recovery.Attempt.TargetSeat || recovery.Attempt.Amount != 1 ||
                    (exchanging ? recovery.Attempt.TargetSeat != p.FirstSeat && recovery.Attempt.TargetSeat != p.SecondSeat : recovery.Attempt.TargetSeat != root.OwnerSeat) ||
                    !PaymentRemovedSilverLion(root, reason, before, recovery.Attempt.TargetSeat)) continue;
            }
            else if (first is CardsMovedTriggerWindowFrame moved)
            {
                if (moved.Batch.ParentFrameId != root.Id || moved.Batch.AwaitingProgramFrameId is { } awaiting && awaiting != root.Id ||
                    moved.Batch.OriginOwnerSeat != root.OwnerSeat || moved.Batch.OriginSkillId != root.SkillId || moved.Batch.OriginSkillInstanceId != root.SkillInstanceId ||
                    moved.Batch.Movements.Count == 0 || moved.Batch.Movements.Any(m => !_cardMovements.Contains(m) || m.Sequence <= before || m.Reason.Value != reason)) continue;
            }
            else continue;
            if (EquipmentPairDyingCardSuffix(index)) return root;
        }
        return null;
    }
    private bool DyingOwnedCardCostIsPaid(ProgramSkillFrame root)
    {
        if (root.DyingOwnedCard is not { NonBasic: true, PaidCardId: { } cardId, SourceLocation: { } source } r) return false;
        AssertDyingOwnedCardReceipt(root);
        return _cardMovements.Count(m => m.Sequence > r.MovementSequenceBefore && m.CardId == cardId && m.From == source && m.To == OwnedPaymentDiscardDestination(cardId) &&
            m.Reason.Value == $"skill-program.{root.SkillId}.{SkillProgramEffectOp.MoveBoundCards}") == 1;
    }
    private ProgramSkillFrame? DyingOwnedCardObserverRoot(long? originalDyingId = null)
    {
        for (var index = 2; index < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root || root.DyingOwnedCard is not { } r ||
                originalDyingId is { } wanted && r.DyingFrameId != wanted || !ExactDyingOwnedCardEntry(root, out _, out _) ||
                root.InstructionIndex is < 1 or > 4) continue;
            AssertDyingOwnedCardReceipt(root);
            if (index + 1 == _resolutionStack.Count) return root;
            if (!DyingOwnedCardCostIsPaid(root)) continue;
            var recovering = root.InstructionIndex == 4;
            var first = _resolutionStack[index + 1];
            if (first is HpChangedTriggerWindowFrame hp)
            {
                if (hp.ResumeFrameId != root.Id || hp.Change.ParentFrameId != root.Id || hp.Change.TargetSeat != r.VictimSeat ||
                    hp.Change.Kind != HpChangeKind.Recovery || hp.Change.Amount != 1 ||
                    hp.Change.SourceSeat != (recovering ? root.OwnerSeat : r.VictimSeat) ||
                    hp.Continuation != (recovering ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement)) continue;
            }
            else if (first is RecoveryReplacementFrame recovery)
            {
                if (!RecoveryReplacementFrameRidesOn(recovery, root) || recovery.Attempt.TargetSeat != r.VictimSeat || recovery.Attempt.Amount != 1 ||
                    recovery.Return.Continuation != (recovering ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement) ||
                    (recovering ? recovery.Attempt.Completion.Producer != RecoveryAttemptProducer.Program || recovery.Attempt.Completion.InstructionIndex != 4 || recovery.Attempt.SourceSeat != root.OwnerSeat
                        : recovery.Attempt.Completion.Producer != RecoveryAttemptProducer.SilverLion || recovery.Attempt.SourceSeat != r.VictimSeat ||
                          recovery.Attempt.Completion.MoveReason?.Value != $"skill-program.{root.SkillId}.{SkillProgramEffectOp.MoveBoundCards}" ||
                          !PaymentRemovedSilverLion(root, $"skill-program.{root.SkillId}.{SkillProgramEffectOp.MoveBoundCards}", r.MovementSequenceBefore, r.VictimSeat))) continue;
            }
            else if (first is CardsMovedTriggerWindowFrame moved)
            {
                if (recovering || moved.Batch.ParentFrameId != root.Id || moved.Batch.AwaitingProgramFrameId is { } awaiting && awaiting != root.Id ||
                    moved.Batch.OriginOwnerSeat != root.OwnerSeat || moved.Batch.OriginSkillId != root.SkillId || moved.Batch.OriginSkillInstanceId != root.SkillInstanceId ||
                    moved.Batch.Movements.Count == 0 || moved.Batch.Movements.Any(m => !_cardMovements.Contains(m) || m.Sequence <= r.MovementSequenceBefore ||
                        m.CardId != r.PaidCardId || m.From != r.SourceLocation || m.To != OwnedPaymentDiscardDestination(m.CardId) ||
                        m.Reason.Value != $"skill-program.{root.SkillId}.{SkillProgramEffectOp.MoveBoundCards}")) continue;
            }
            else continue;
            if (EquipmentPairDyingCardSuffix(index)) return root;
        }
        return null;
    }
    private bool IsExactDyingOwnedCardRide(long dyingId) => DyingOwnedCardObserverRoot(dyingId) is not null;
    private bool IsEquipmentPairOrDyingCardProgramDying()
    {
        if (ActiveDying is not { ResumesProgramSkill: true } dying) return false;
        var root = EquipmentPairPaymentObserverRoot() ?? DyingOwnedCardObserverRoot();
        return root is not null && _resolutionStack.FindIndex(f => f.Id == dying.FrameId) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    }
}
