namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ProgramSkillFrame? InspectedHandPaidObserverRoot(long? damageWindow = null)
    {
        for (var index = 0; index < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root || root.InspectedHandSlash is not { } d ||
                d.Stage is not (InspectedHandSlashStage.HpChildren or InspectedHandSlashStage.DiscardChildren) || !ValidInspectedHandSlash(root)) continue;
            if (damageWindow is { } window && !_resolutionStack.Skip(index + 1).Any(f => f.Id == window &&
                f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)) continue;
            if (index + 1 == _resolutionStack.Count) return damageWindow is null ? root : null;
            var first = _resolutionStack[index + 1];
            bool firstMatches;
            if (d.Stage == InspectedHandSlashStage.DiscardChildren)
                firstMatches = root.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } pending &&
                    pending.SubjectSeat == root.OwnerSeat && first is CardsMovedTriggerWindowFrame movement &&
                    movement.Batch.ParentFrameId == root.Id && (movement.Batch.AwaitingProgramFrameId is null || movement.Batch.AwaitingProgramFrameId == root.Id) &&
                    movement.Batch.OriginOwnerSeat == root.OwnerSeat && movement.Batch.OriginSkillId == root.SkillId &&
                    movement.Batch.OriginSkillInstanceId == root.SkillInstanceId && movement.Batch.Movements is [var actual] &&
                    _cardMovements.Contains(actual) && actual.Sequence == d.SequenceAfter && actual.CardId == d.DiscardedCardId &&
                    actual.From == CardLocation.Hand(d.TargetSeat) && actual.To == CardLocation.DiscardPile && actual.Reason.Value == InspectedHandDiscardReason;
            else
                firstMatches = first switch
                {
                    DyingFrame dying => dying.ParentFrameId == root.Id && dying.VictimSeat == root.OwnerSeat &&
                        dying.Continuation == DyingContinuationKind.ProgramSkill && dying.KillerSeat is null && d.HpAfter == 0,
                    HpChangedTriggerWindowFrame hp => hp.ResumeFrameId == root.Id && hp.Change.ParentFrameId == root.Id &&
                        hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Loss &&
                        hp.Change.SourceSeat is null && hp.Change.TargetSeat == root.OwnerSeat && hp.Change.Amount == 1,
                    _ => false
                };
            if (!firstMatches) continue;
            var valid = true;
            // The first typed edge is proven by the real HP cost or discard ledger above.
            for (var child = index + 2; child < _resolutionStack.Count; child++)
            {
                var preceding = _resolutionStack[child - 1];
                if (preceding is DyingFrame rescued && (IsPaidHandRepaymentRescueRide(child - 1, rescued) ||
                    IsPaidHandRepaymentProgramAlcoholRide(child - 1, rescued) || PolicyCounterspellVirtualAlcoholRide(child - 1, rescued) ||
                    PaidObserverDamageVirtualAlcoholRide(child - 1, rescued))) break;
                if (!PaidColorDamageClaimObserverEdge(child)) { valid = false; break; }
            }
            if (valid) return root;
        }
        return null;
    }
    private bool IsInspectedHandProgramDying() => ActiveDying is { } dying && InspectedHandPaidObserverRoot(ActiveDamageTrigger?.Id) is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsInspectedHandMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.PayHpInspectHandThenDiscardOrSlash && pending.SubjectSeat == f.OwnerSeat &&
        pending.BeforeCount == 0 && pending.CoverageResultBind is null && f.InspectedHandSlash is { Stage: InspectedHandSlashStage.DiscardChildren } &&
        ValidInspectedHandSlash(f);
    private bool HasInspectedHandDamageObserver(long window) => InspectedHandPaidObserverRoot(window) is not null;
}
