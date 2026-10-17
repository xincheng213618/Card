namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsExactGameStartingColorGainDraw(ProgramSkillFrame program, CardsMovedTriggerWindowFrame parent,
        NativeDrawInvocationProof incoming, int recipient, int requested, CardMoveReason reason,
        CardConversionSource? directProducer, long before)
    {
        if (program.TangTang is not { Paid: true, Op: SkillProgramEffectOp.ResolveZhiyiGainColorBenefit,
                Stage: TangTangStage.DrawChildren, Step: 1, ActualTurn: 0, ActualCount: 0,
                PlayPhase: null, Shown: null } receipt ||
            receipt.Source != new CardConversionSource(program.SkillId, GetProgramBindingId(program), program.OwnerSeat, program.SkillInstanceId) ||
            receipt.GameplayHash != program.GameplayHash || receipt.WindowId != parent.Id ||
            receipt.TurnOwner != incoming.ActualTurnOwnerSeat || receipt.Recipient != recipient ||
            receipt.Before != before || receipt.After != before || requested != 1 || directProducer is not null ||
            reason.Value != ZhiyiDrawReason || program.PendingMovementContinuation != new ProgramMovementContinuation(recipient, 0, null) ||
            program.WindowContext is not { } context ||
            CurrentTangTangGains(parent.Candidates[parent.CandidateIndex], context) is not { Count: > 0 } gains ||
            !receipt.Gains.SequenceEqual(gains) || gains.Any(gain => gain.NativeDrawId != incoming.InvocationId ||
                gain.BatchId != parent.Batch.Id || gain.OwnerSeat != program.OwnerSeat || gain.RecipientSeat != program.OwnerSeat || gain.Foreign))
            return false;
        return true;
    }

    private bool GameStartingColorGainDrawStartedMatches(GameStartingOutsideDrawOrigin child, IReadOnlyList<IGameEvent> history)
    {
        if (history.OfType<NativeDrawInvocationRecordedEvent>().Where(invoice => invoice.InvocationId == child.ParentInvocationId).ToArray()
                is not [var parent] || parent.OwnerSeat != child.Source.OwnerSeat || parent.LastBatchId != child.OriginalBatchId ||
            history.OfType<TangTangStartedEvent>().Where(started => started.FrameId == child.ProgramFrameId).ToArray() is not [var start] ||
            start.Source != child.Source || start.GameplayHash != child.GameplayHash ||
            start.Op != SkillProgramEffectOp.ResolveZhiyiGainColorBenefit || start.ActualTurn != 0 ||
            start.TurnOwner != parent.ActualTurnOwnerSeat || start.WindowId != child.WindowFrameId || start.PlayToken != 0 ||
            history.OfType<ZhiyiOpportunityPaidEvent>().Where(paid => paid.FrameId == child.ProgramFrameId).ToArray() is not [var opportunity] ||
            opportunity.OwnerSeat != child.Source.OwnerSeat || opportunity.ActualTurn != 0 ||
            opportunity.TurnOwner != start.TurnOwner || opportunity.BatchId != child.WindowFrameId ||
            history.OfType<ZhiyiRecipientChosenEvent>().Where(chosen => chosen.FrameId == child.ProgramFrameId).ToArray() is not [var recipient] ||
            recipient.RecipientSeat != child.ParticipantSeat) return false;
        return history.OfType<ZhiyiGainFrozenEvent>().Any(frozen => frozen.Gain is { Foreign: false, From: var from } gain &&
            from == CardLocation.DrawPile && gain.NativeDrawId == parent.InvocationId && gain.BatchId == child.OriginalBatchId &&
            gain.OwnerSeat == child.Source.OwnerSeat && gain.RecipientSeat == child.Source.OwnerSeat &&
            gain.GainSequence > parent.SequenceBefore && gain.GainSequence <= parent.SequenceAfter && gain.EntrySequence == gain.GainSequence &&
            _cardMovements.Any(move => move.Sequence == gain.GainSequence && move.CardId == gain.CardId &&
                move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(gain.OwnerSeat) && move.Reason == parent.Reason));
    }

    private bool GameStartingColorGainDrawPaymentMatches(NativeDrawInvocationRecordedEvent invoice,
        GameStartingOutsideDrawOrigin child, IReadOnlyList<IGameEvent> history)
    {
        if (invoice.Reason.Value != ZhiyiDrawReason) return false;
        var issued = history.OfType<TangTangMovementIssuedEvent>().Where(payment => payment.FrameId == child.ProgramFrameId).ToArray();
        if (issued is [var payment]) return payment == new TangTangMovementIssuedEvent(child.ProgramFrameId, 1,
            TangTangStage.DrawChildren, invoice.SequenceBefore, invoice.SequenceAfter, invoice.ActualCount);
        // DrawCards qualifies its actual materials before the native producer can
        // publish MovementIssued. Only its exact already-paid request may bridge that call.
        return issued.Length == 0 && _resolutionStack.OfType<ProgramSkillFrame>().Any(program => program.Id == child.ProgramFrameId &&
            program.InstructionIndex == child.InstructionIndex && program.GameplayHash == child.GameplayHash &&
            program.TangTang is { Paid: true, Op: SkillProgramEffectOp.ResolveZhiyiGainColorBenefit,
                Stage: TangTangStage.DrawChildren, Step: 1, ActualCount: 0, PlayPhase: null, Shown: null } receipt &&
            receipt.Source == child.Source && receipt.Recipient == invoice.OwnerSeat && receipt.WindowId == child.WindowFrameId &&
            receipt.Before == invoice.SequenceBefore && receipt.After == invoice.SequenceBefore &&
            program.PendingMovementContinuation == new ProgramMovementContinuation(invoice.OwnerSeat, 0, null));
    }
}
