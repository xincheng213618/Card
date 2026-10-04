namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsConditionalDuelAwaitedMovement(ProgramSkillFrame root, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.DiscardSlashThenOtherCardAndUseDuel && root.ConditionalDiscardDuel is
            { Stage: ConditionalDiscardDuelStage.OwnerChildren or ConditionalDiscardDuelStage.TargetChildren } d &&
        pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        pending.SubjectSeat == (d.Stage == ConditionalDiscardDuelStage.OwnerChildren ? root.OwnerSeat : d.TargetSeat) && ValidConditionalDuelPayments(root);
    private ProgramSkillFrame? ConditionalDuelPaidObserverRoot()
    {
        for (var rootIndex = 0; rootIndex + 1 < _resolutionStack.Count; rootIndex++)
        {
            if (_resolutionStack[rootIndex] is not ProgramSkillFrame root || root.ConditionalDiscardDuel is not
                { Stage: ConditionalDiscardDuelStage.OwnerChildren or ConditionalDiscardDuelStage.TargetChildren } d ||
                root.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || !ValidConditionalDuelPayments(root)) continue;
            var paid = d.Stage == ConditionalDiscardDuelStage.OwnerChildren ? d.OwnerPayment! : d.TargetPayment!;
            if (pending.SubjectSeat != paid.PayerSeat) continue;
            var first = _resolutionStack[rootIndex + 1]; var reason = d.Stage == ConditionalDiscardDuelStage.OwnerChildren ? ConditionalDuelOwnerReason : ConditionalDuelTargetReason;
            if (first is CardsMovedTriggerWindowFrame moved)
            {
                if (moved.Batch.ParentFrameId != root.Id || moved.Batch.AwaitingProgramFrameId is { } awaiting && awaiting != root.Id ||
                    moved.Batch.OriginOwnerSeat != root.OwnerSeat || moved.Batch.OriginSkillId != root.SkillId ||
                    moved.Batch.OriginSkillInstanceId != root.SkillInstanceId || moved.Batch.Movements is not [var m] ||
                    !_cardMovements.Contains(m) || m.CardId != paid.CardId || m.Sequence <= paid.SequenceBefore || m.Sequence > paid.SequenceAfter || m.Reason.Value != reason) continue;
            }
            else
            {
                if (paid.PrintedKind != CardKind.SilverLion || paid.From != CardLocation.Equipment(paid.PayerSeat)) continue;
                if (first is HpChangedTriggerWindowFrame hp)
                {
                    if (hp.ResumeFrameId != root.Id || hp.Continuation != PostEventContinuation.AwaitedProgramMovement || hp.Change.ParentFrameId != root.Id ||
                        hp.Change.Kind != HpChangeKind.Recovery || hp.Change.SourceSeat != paid.PayerSeat || hp.Change.TargetSeat != paid.PayerSeat || hp.Change.Amount != 1) continue;
                }
                else if (first is RecoveryReplacementFrame recovery)
                {
                    if (!RecoveryReplacementFrameRidesOn(recovery, root) || recovery.Return.Continuation != PostEventContinuation.AwaitedProgramMovement ||
                        recovery.Attempt.SourceSeat != paid.PayerSeat || recovery.Attempt.TargetSeat != paid.PayerSeat || recovery.Attempt.Amount != 1 ||
                        recovery.Attempt.Completion.Producer != RecoveryAttemptProducer.SilverLion || recovery.Attempt.Completion.MoveReason?.Value != reason) continue;
                }
                else continue;
            }
            var valid = true;
            for (var i = rootIndex + 1; i < _resolutionStack.Count; i++)
            {
                if (!PaidColorDamageClaimObserverEdge(i)) { valid = false; break; }
                if (_resolutionStack[i] is DyingFrame dying && (IsPaidHandRepaymentRescueRide(i, dying) || IsPaidHandRepaymentProgramAlcoholRide(i, dying) ||
                    PolicyCounterspellVirtualAlcoholRide(i, dying) || PaidObserverDamageVirtualAlcoholRide(i, dying))) break;
            }
            if (valid) return root;
        }
        return null;
    }
    private bool IsConditionalDiscardDuelProgramDying() => ActiveDying is { } dying && ConditionalDuelPaidObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
}
