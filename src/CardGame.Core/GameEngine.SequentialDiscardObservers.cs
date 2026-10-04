namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ProgramSkillFrame? SequentialDiscardPaymentObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root || root.SequentialDiscard is not
                { Stage: ProgramSequentialDiscardStage.AwaitingMovement, Payment: { } paid } || !ValidSequentialDiscard(root) ||
                root.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != paid.ChooserSeat)
                continue;
            if (!SequentialDiscardFirstMovementChild(_resolutionStack[index + 1], root, paid)) continue;
            if (SequentialDiscardDescendantsRide(index)) return root;
        }
        return null;
    }

    private bool SequentialDiscardFirstMovementChild(ResolutionFrame child, ProgramSkillFrame root, ProgramSequentialDiscardPayment paid)
    {
        var reason = SequentialDiscardReason(root);
        bool HasSilverLion() => paid.CardIds.Where((id, n) => paid.SourceLocations[n] == CardLocation.Equipment(paid.ChooserSeat))
            .Any(id => _cardZones.CardsAt(_cardZones.GetLocation(id)).Any(c => c.Id == id && c.Kind == CardKind.SilverLion));
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.ResumeFrameId == root.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.ParentFrameId == root.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == paid.ChooserSeat &&
                hp.Change.TargetSeat == paid.ChooserSeat && hp.Change.Amount == 1 && HasSilverLion();
        if (child is RecoveryReplacementFrame recovery)
            return RecoveryReplacementFrameRidesOn(recovery, root) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                recovery.Attempt.SourceSeat == paid.ChooserSeat && recovery.Attempt.TargetSeat == paid.ChooserSeat && recovery.Attempt.Amount == 1 &&
                recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == reason && HasSilverLion();
        return child is CardsMovedTriggerWindowFrame moved && moved.Batch.ParentFrameId == root.Id &&
            (moved.Batch.AwaitingProgramFrameId is null || moved.Batch.AwaitingProgramFrameId == root.Id) &&
            moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
            moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.Movements.Count > 0 &&
            moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > paid.SequenceBefore && m.Sequence <= paid.SequenceAfter &&
                paid.CardIds.Contains(m.CardId) && m.Reason.Value == reason);
    }

    private bool SequentialDiscardDescendantsRide(int rootIndex)
    {
        for (var index = rootIndex + 1; index < _resolutionStack.Count; index++)
        {
            var child = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
            // Validate the incoming edge before accepting an already exact whole rescue suffix.
            if (child is ProgramSkillFrame response && parent is DyingFrame dying &&
                PolicyCounterspellDyingProgramRidesOn(response, dying))
            {
                if (IsPaidHandRepaymentProgramAlcoholRide(index - 1, dying) || PolicyCounterspellVirtualAlcoholRide(index - 1, dying)) return true;
                continue;
            }
            if (PolicyCounterspellDyingFaceEdge(index)) continue;
            if (!PreventionDrawObserverEdge(index)) return false;
            if (child is DyingFrame rescued && (IsPaidHandRepaymentProgramAlcoholRide(index, rescued) ||
                IsPaidHandRepaymentRescueRide(index, rescued) || PolicyCounterspellVirtualAlcoholRide(index, rescued))) return true;
        }
        return true;
    }

    private bool IsSequentialDiscardProgramDying() => ActiveDying is { ResumesProgramSkill: true } dying &&
        (SequentialDiscardPaymentObserverRoot() ?? SequentialDiscardTopPaymentObserverRoot()) is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.FrameId) > _resolutionStack.FindIndex(f => f.Id == root.Id);

    private ProgramSkillFrame? SequentialDiscardTopPaymentObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame { InstructionIndex: 2, SequentialDiscard: null } root ||
                root.SequentialDiscardTopPayment is not { } top || root.PendingMovementContinuation is not
                { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != root.OwnerSeat ||
                !HasSequentialDiscardTopPayment(root, top.ResultBind) || _resolutionStack[index + 1] is not CardsMovedTriggerWindowFrame moved ||
                moved.Batch.ParentFrameId != root.Id || moved.Batch.AwaitingProgramFrameId is { } awaiting && awaiting != root.Id ||
                moved.Batch.OriginOwnerSeat != root.OwnerSeat || moved.Batch.OriginSkillId != root.SkillId ||
                moved.Batch.OriginSkillInstanceId != root.SkillInstanceId || moved.Batch.Movements is not [var payment] ||
                !_cardMovements.Contains(payment) || payment.Sequence <= top.MovementSequenceBefore || payment.CardId != top.CardId ||
                payment.From != top.SourceLocation || payment.To != CardLocation.DrawPile ||
                payment.Reason.Value != $"skill-program.{root.SkillId}.{SkillProgramEffectOp.MoveBoundCards}") continue;
            if (SequentialDiscardDescendantsRide(index)) return root;
        }
        return null;
    }
}
