namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool PrivateOfferFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        long before, after; int actor; bool draw;
        if (root.PrivateOffer is { } r && ValidPrivateOfferReceipt(root))
        {
            if (r.Stage == PrivateOfferStage.HpLoss)
            {
                if (child is DyingFrame dying) return dying.ParentFrameId == root.Id && dying.Continuation == DyingContinuationKind.ProgramSkill &&
                    dying.VictimSeat == r.Location.PrivateTurnHold!.DeferredOffer!.TargetSeat && ActiveDying?.FrameId == dying.Id &&
                    CompleteProgramEventHistory().OfType<ProgramSkillHpLostEvent>().Count(e => e.FrameId == root.Id && e.SkillId == root.SkillId && e.TargetSeat == dying.VictimSeat && e.RemainingHp == 0) == 1;
                return child is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == root.Id && hp.ResumeFrameId == root.Id &&
                    hp.Continuation == PostEventContinuation.Program && hp.Change.TargetSeat == r.Location.PrivateTurnHold!.DeferredOffer!.TargetSeat && hp.Change.Kind == HpChangeKind.Loss;
            }
            if (r.Stage == PrivateOfferStage.ChoosingExchange || root.PendingMovementContinuation is null) return false;
            before = r.SequenceBefore; after = r.SequenceAfter; actor = r.Stage == PrivateOfferStage.DepositChildren ? root.OwnerSeat : r.Location.PrivateTurnHold!.DeferredOffer!.TargetSeat; draw = false;
        }
        else if (root.GameTargetHandHp is { Stage: "children" } game && ValidGameHandHpReceipt(root) && root.PendingMovementContinuation is not null)
        { before = game.SequenceBefore; after = game.SequenceAfter; actor = game.TargetSeat; draw = game.Draw; }
        else return false;
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == root.Id && moved.ResumeProgramFrameId == root.Id &&
                (moved.Batch.AwaitingProgramFrameId is null || moved.Batch.AwaitingProgramFrameId == root.Id) && moved.Batch.OriginOwnerSeat == root.OwnerSeat &&
                moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) && (m.Sequence > before && m.Sequence <= after ||
                    root.GameTargetHandHp is { Draw: false } paid && m.Sequence > after && m.From == CardLocation.WoodenOxGrain(actor) && m.To == CardLocation.DiscardPile &&
                    m.Reason == CardMoveReasons.WoodenOxGrainDiscard && paid.CardIds.Any(id => _cardMovements.Any(cost => cost.Sequence > before && cost.Sequence <= after &&
                        cost.CardId == id && cost.CardKind == CardKind.WoodenOx && cost.From == CardLocation.Equipment(actor) && cost.To == CardLocation.DiscardPile && cost.Reason.Value == GameHandHpReason(root)))));
        if (draw || root.GameTargetHandHp is not { Draw: false } discard || !_cardMovements.Any(m => m.Sequence > before && m.Sequence <= after &&
            m.CardKind == CardKind.SilverLion && m.From == CardLocation.Equipment(actor) && m.To == CardLocation.DiscardPile && discard.CardIds.Contains(m.CardId) && m.Reason.Value == GameHandHpReason(root))) return false;
        if (child is HpChangedTriggerWindowFrame lion)
            return lion.Change.ParentFrameId == root.Id && lion.ResumeFrameId == root.Id && lion.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                lion.Change.Kind == HpChangeKind.Recovery && lion.Change.SourceSeat == actor && lion.Change.TargetSeat == actor && lion.Change.Amount == 1;
        return child is RecoveryReplacementFrame replacement && RecoveryReplacementFrameRidesOn(replacement, root) &&
            replacement.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && replacement.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            replacement.Attempt.SourceSeat == actor && replacement.Attempt.TargetSeat == actor && replacement.Attempt.Amount == 1 && replacement.Attempt.Completion.MoveReason?.Value == GameHandHpReason(root);
    }
    private ProgramSkillFrame? PrivateOfferObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root || !PrivateOfferFirstChild(root, _resolutionStack[index + 1])) continue;
            if (_resolutionStack[index + 1] is DyingFrame original && index + 2 < _resolutionStack.Count &&
                (IsPaidHandRepaymentProgramAlcoholRide(index + 1, original) || IsPaidHandRepaymentRescueRide(index + 1, original) ||
                 PolicyCounterspellVirtualAlcoholRide(index + 1, original) || TieredRoundZeroDyingRescueRide(index + 1, original))) return root;
            var exact = true;
            for (var child = index + 2; child < _resolutionStack.Count; child++)
            {
                if (!(HalfHandPaidDamageObserverEdge(child) || PaidTargetObserverEdge(child))) { exact = false; break; }
                if (_resolutionStack[child] is DyingFrame dying && child + 1 < _resolutionStack.Count &&
                    (IsPaidHandRepaymentProgramAlcoholRide(child, dying) || IsPaidHandRepaymentRescueRide(child, dying) ||
                     PolicyCounterspellVirtualAlcoholRide(child, dying) || PaidObserverDamageVirtualAlcoholRide(child, dying) || TieredRoundZeroDyingRescueRide(child, dying))) break;
            }
            if (exact) return root;
        }
        return null;
    }
    private bool IsPrivateOfferProgramDying() => ActiveDying is { } dying && PrivateOfferObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.FrameId) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasPrivateOfferDamageObserver(long windowId) => _resolutionStack.Any(f => f.Id == windowId && f is DamageTriggerWindowFrame) && PrivateOfferObserverRoot() is not null;
    private void AssertPrivateOfferPrograms()
    {
        AssertPrivateOfferFrames();
        foreach (var root in _resolutionStack.OfType<ProgramSkillFrame>())
            if (root.GameTargetHandHp is not null && !ValidGameHandHpReceipt(root)) throw new InvalidOperationException("A game-target invoice lost its real issuance/payment.");
    }
}
