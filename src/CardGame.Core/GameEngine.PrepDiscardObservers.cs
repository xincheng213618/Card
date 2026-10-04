namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ProgramSkillFrame? PrepDiscardPaidObserverRoot(long? exactDamageWindow = null)
    {
        for (var rootIndex = 1; rootIndex < _resolutionStack.Count; rootIndex++)
        {
            if (_resolutionStack[rootIndex] is not ProgramSkillFrame root || root.PendingMovementContinuation is not
                { BeforeCount: 0, CoverageResultBind: null } pending) continue;
            long before, after; PrepDiscardPayment? paid = null; string reason; int payer;
            if (root.PrepDiscard is { Stage: PrepDiscardStage.TargetChildren or PrepDiscardStage.OwnerChildren } d && ValidPrepDiscard(root))
            {
                var ownerCost = d.Stage == PrepDiscardStage.OwnerChildren; paid = ownerCost ? d.OwnerPayment : d.TargetPayment;
                if (paid is null) continue;
                before = paid.SequenceBefore; after = paid.SequenceAfter; payer = paid.PayerSeat;
                reason = ownerCost ? PrepDiscardOwnerReason : PrepDiscardTargetReason;
            }
            else if (root.PrepDiscardEndingDraw is { ChildrenCompleted: false } draw && ValidPrepDiscardEndingDraw(root))
            { before = draw.SequenceBefore; after = draw.SequenceAfter; payer = draw.Promise.TargetSeat; reason = PrepDiscardDrawReason; }
            else continue;
            if (pending.SubjectSeat != payer || after <= before || exactDamageWindow is { } id &&
                !_resolutionStack.Skip(rootIndex + 1).Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)) continue;
            if (rootIndex == _resolutionStack.Count - 1) return root;
            var first = _resolutionStack[rootIndex + 1];
            if (first is CardsMovedTriggerWindowFrame movement)
            {
                if (movement.Batch.ParentFrameId != root.Id || movement.Batch.AwaitingProgramFrameId is { } awaiting && awaiting != root.Id ||
                    movement.Batch.OriginSkillId != root.SkillId || movement.Batch.OriginSkillInstanceId != root.SkillInstanceId ||
                    movement.Batch.OriginOwnerSeat != root.OwnerSeat || movement.Batch.Movements.Count == 0 ||
                    movement.Batch.Movements.Any(m => m.Sequence <= before || m.Sequence > after || !_cardMovements.Contains(m))) continue;
            }
            else
            {
                if (paid is null || !_cardMovements.Any(m => m.Sequence > before && m.Sequence <= after && paid.CardIds.Contains(m.CardId) &&
                    m.From == CardLocation.Equipment(payer) && m.To == CardLocation.DiscardPile && m.CardKind == CardKind.SilverLion &&
                    m.Reason.Value == reason)) continue;
                if (first is HpChangedTriggerWindowFrame hp)
                {
                    if (hp.Change.ParentFrameId != root.Id || hp.ResumeFrameId != root.Id || hp.Continuation != PostEventContinuation.AwaitedProgramMovement ||
                        hp.Change.Kind != HpChangeKind.Recovery || hp.Change.SourceSeat != payer || hp.Change.TargetSeat != payer || hp.Change.Amount != 1) continue;
                }
                else if (first is RecoveryReplacementFrame recovery)
                {
                    if (!RecoveryReplacementFrameRidesOn(recovery, root) || recovery.Return.Continuation != PostEventContinuation.AwaitedProgramMovement ||
                        recovery.Attempt.SourceSeat != payer || recovery.Attempt.TargetSeat != payer || recovery.Attempt.Amount != 1 ||
                        recovery.Attempt.Completion.Producer != RecoveryAttemptProducer.SilverLion || recovery.Attempt.Completion.MoveReason?.Value != reason) continue;
                }
                else continue;
            }
            var valid = true;
            for (var index = rootIndex + 1; index < _resolutionStack.Count; index++)
            {
                // First exact paid edge precedes any complete rescue suffix.
                if (!PaidColorDamageClaimObserverEdge(index)) { valid = false; break; }
                if (_resolutionStack[index] is DyingFrame dying && (IsPaidHandRepaymentRescueRide(index, dying) ||
                    IsPaidHandRepaymentProgramAlcoholRide(index, dying) || PolicyCounterspellVirtualAlcoholRide(index, dying) ||
                    PaidObserverDamageVirtualAlcoholRide(index, dying))) break;
            }
            if (valid) return root;
        }
        return null;
    }
    private bool IsPrepDiscardProgramDying() => ActiveDying is { } dying && PrepDiscardPaidObserverRoot(ActiveDamageTrigger?.Id) is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasPrepDiscardDamageObserver(long id) => PrepDiscardPaidObserverRoot(id) is not null;
}
