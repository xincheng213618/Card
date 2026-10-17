namespace CardGame.Core;

public sealed record DingShangWanOpeningDrawReturnedEvent(long FrameId, int RecipientSeat, int RequestedCount,
    long? AttemptFrameId, long SequenceBefore, long SequenceAfter, int ActualCount, int ReplacementGainCount) : IGameEvent;

public sealed partial class GameEngine
{
    private void BeginDingShangWanOpeningDraw(ProgramSkillFrame frame, int seat, int count)
    {
        if (frame.DingShangWan is not { Operation: SkillProgramEffectOp.InitializeFudaoHandRecord,
                Stage: DingShangWanStage.OpeningDraw, OpeningDrawOutcome: null } receipt ||
            seat != frame.OwnerSeat || count != 3 || frame.NativeDraw is not null || frame.PendingMovementContinuation is not null)
            throw new InvalidOperationException("Fudao requires one exact new opening draw request.");
        ReplaceRuntimeTop(frame = frame with { DingShangWan = receipt with
            { Payer = seat, Before = OutsidePhaseMovementSequence, After = OutsidePhaseMovementSequence,
                ActualCount = 0, Candidates = [], Selected = [], WoodenOxGrainIds = [], Reshuffles = [] } });
        BeginOperationProgramDraw(frame, seat, count, new(DingShangWanDrawReason));
    }

    [ProgramNativeDrawReturn(SkillProgramEffectOp.InitializeFudaoHandRecord)]
    private void ReturnDingShangWanOpeningDraw(ProgramSkillFrame frame, NativeDrawOutcome outcome)
    {
        if (frame.DingShangWan is not { Operation: SkillProgramEffectOp.InitializeFudaoHandRecord,
                Stage: DingShangWanStage.OpeningDraw, OpeningDrawOutcome: null } receipt ||
            frame.NativeDraw is not null || frame.PendingMovementContinuation is not null || outcome.RecipientSeat != frame.OwnerSeat ||
            outcome.RequestedCount != 3 || outcome.SequenceBefore < receipt.Before || outcome.SequenceAfter != OutsidePhaseMovementSequence ||
            ProgramEventHistory<DingShangWanOpeningDrawReturnedEvent>().Any(fact => fact.FrameId == frame.Id))
            throw new InvalidOperationException("Fudao cannot substitute or repeat its native opening draw return.");
        ReplaceRuntimeTop(frame = frame with { DingShangWan = receipt with
            { OpeningDrawOutcome = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceBefore },
            PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new DingShangWanOpeningDrawReturnedEvent(frame.Id, outcome.RecipientSeat, outcome.RequestedCount,
            outcome.AttemptFrameId, outcome.SequenceBefore, outcome.SequenceAfter, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count));
        RecordDingShangWanMovement(frame.Id);
        ContinueDingShangWan(frame.Id);
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26706)]
    private void AssertDingShangWanOpeningDraw(ProgramSkillFrame frame)
    {
        if (frame.DingShangWan is not { } receipt)
        {
            if (frame.NativeDraw?.Operation == SkillProgramEffectOp.InitializeFudaoHandRecord)
                throw new InvalidOperationException("Fudao's native request lost its owning opening receipt.");
            return;
        }
        if (receipt.Operation != SkillProgramEffectOp.InitializeFudaoHandRecord)
        {
            if (receipt.OpeningDrawOutcome is not null || frame.NativeDraw?.Operation == SkillProgramEffectOp.InitializeFudaoHandRecord)
                throw new InvalidOperationException("A later Fudao or Fengyan producer cannot borrow the opening draw outcome.");
            return;
        }
        var returned = ProgramEventHistory<DingShangWanOpeningDrawReturnedEvent>().Where(fact => fact.FrameId == frame.Id).ToArray();
        if (frame.NativeDraw is { } paid)
        {
            if (receipt.Stage != DingShangWanStage.OpeningDraw || receipt.OpeningDrawOutcome is not null || returned.Length != 0 ||
                frame.PendingMovementContinuation is not null || paid.Operation != receipt.Operation ||
                paid.ReturnKind != NativeDrawAttemptReturnKind.ProgramOperation || paid.InstructionIndex != frame.InstructionIndex ||
                paid.RequestedCount != 3 || !paid.Recipients.SequenceEqual([frame.OwnerSeat]) || paid.RecipientIndex != 0 ||
                receipt.Payer != frame.OwnerSeat || receipt.ActualCount != 0 || receipt.Before != receipt.After ||
                receipt.Selected.Count != 0 || receipt.Reshuffles.Count != 0)
                throw new InvalidOperationException("Fudao lost its exact unpaid opening request and native child owner.");
            return;
        }
        if (receipt.OpeningDrawOutcome is not { } outcome || outcome.RecipientSeat != frame.OwnerSeat || outcome.RequestedCount != 3 ||
            outcome.SequenceBefore < 0 || outcome.SequenceAfter < outcome.SequenceBefore || outcome.SequenceAfter > OutsidePhaseMovementSequence ||
            outcome.ActualDrawIds.Count > 3 || outcome.ActualDrawIds.Distinct().Count() != outcome.ActualDrawIds.Count ||
            outcome.Replacement is not null && outcome.ActualDrawIds.Count != 0 ||
            !_cardMovements.Where(move => move.Sequence > outcome.SequenceBefore && move.Sequence <= outcome.SequenceAfter &&
                move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(frame.OwnerSeat) && move.Reason.Value == DingShangWanDrawReason)
                .OrderBy(move => move.Sequence).Select(move => move.CardId).SequenceEqual(outcome.ActualDrawIds) ||
            returned is not [var actual] || actual != new DingShangWanOpeningDrawReturnedEvent(frame.Id, frame.OwnerSeat, 3,
                outcome.AttemptFrameId, outcome.SequenceBefore, outcome.SequenceAfter, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count) ||
            receipt.Stage == DingShangWanStage.OpeningDraw && (frame.PendingMovementContinuation is null ||
                receipt.Before != outcome.SequenceBefore || receipt.After != outcome.SequenceAfter || receipt.ActualCount != outcome.ActualDrawIds.Count))
            throw new InvalidOperationException("Fudao lost its once-returned physical opening outcome.");
        if (outcome.AttemptFrameId is { } attempt)
        {
            if (ProgramEventHistory<NativeDrawAttemptStartedEvent>().Where(fact => fact.Context.AttemptFrameId == attempt).ToArray() is not [var start] ||
                start.Context.ParentFrameId != frame.Id || start.Context.RecipientSeat != frame.OwnerSeat || start.Context.RequestedCount != 3 ||
                start.Context.Reason.Value != DingShangWanDrawReason || start.Context.ActualTurnNumber != receipt.Turn ||
                start.Context.ActualTurnOwnerSeat != receipt.TurnOwner || start.Context.SequenceBefore > outcome.SequenceBefore ||
                start.Return != new NativeDrawAttemptReturn(NativeDrawAttemptReturnKind.ProgramOperation, frame.InstructionIndex, 0) ||
                ProgramEventHistory<NativeDrawAttemptDecidedEvent>().Count(fact => fact.AttemptFrameId == attempt && fact.ParentFrameId == frame.Id &&
                    fact.RecipientSeat == frame.OwnerSeat && fact.RequestedCount == 3 && fact.Replaced == (outcome.Replacement is not null)) != 1)
                throw new InvalidOperationException("Fudao retained an opening outcome from a foreign native request.");
        }
        else if (outcome.Replacement is not null || ProgramEventHistory<NativeDrawAttemptStartedEvent>().Any(fact => fact.Context.ParentFrameId == frame.Id))
            throw new InvalidOperationException("Fudao cannot invent or omit a paid opening draw request.");
    }
}
