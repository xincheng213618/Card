namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static readonly CardMoveReason PrepDiscardImmediateDrawReason = new("program.preparation-discard.immediate-draw");

    private void IssuePrepDiscardImmediateDraw(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id); var draft = frame.PrepDiscard!;
        if (!IsImmediatePrepDiscard(frame) || !ValidPrepDiscard(frame) || draft.Stage != PrepDiscardStage.ChoosingBenefit ||
            draft.TargetPayment is not { NonEquipmentCount: > 0 } paid || draft.TargetSeat is not { } target ||
            draft.ImmediateDraw is not null || !PrepDiscardCanContinue(frame) || !_players[target].IsAlive ||
            CompleteProgramEventHistory().OfType<PrepDiscardImmediateBenefitChosenEvent>().Count(e => e.ProgramFrameId == frame.Id &&
                e.DrawTarget && e.TargetSeat == target && e.Count == paid.NonEquipmentCount) != 1)
            throw new InvalidOperationException("An immediate preparation draw requires its exact paid target and chosen frozen amount.");
        ReplaceRuntimeTop(frame = frame with { PrepDiscard = draft with { Stage = PrepDiscardStage.ImmediateDrawAttempt,
            ImmediateDraw = new(target, paid.NonEquipmentCount) }, ReexecuteParticipantInstruction = true });
        AdvanceEventRulesAndQueueFact(new PrepDiscardImmediateDrawRequestedEvent(frame.Id, target, paid.NonEquipmentCount));
        BeginOperationProgramDraw(frame, target, paid.NonEquipmentCount, PrepDiscardImmediateDrawReason);
    }

    // Loss of the skill after the original discard does not revoke this issued
    // obligation. Death before this separate draw pays still cancels new cards.
    private bool CanApplyPrepDiscardImmediateDraw(ProgramSkillFrame frame)
    {
        if (frame.PrepDiscard is not { Stage: PrepDiscardStage.ImmediateDrawAttempt, ImmediateDraw: not null } ||
            !IsImmediatePrepDiscard(frame)) return true;
        if (!ValidPrepDiscard(frame)) throw new InvalidOperationException("An immediate preparation draw lost its paid owning instruction before payment.");
        return _players[frame.OwnerSeat].IsAlive && _winner == Winner.None;
    }

    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolvePrepDiscardOrImmediateDraw)]
    private void ReturnPrepDiscardImmediateDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var frame = GetActiveProgramFrame(supplied.Id); var draft = frame.PrepDiscard!;
        if (!ValidPrepDiscard(frame) || frame.NativeDraw is not null || draft.Stage != PrepDiscardStage.ImmediateDrawAttempt ||
            draft.ImmediateDraw is not { Outcome: null } draw || outcome.RecipientSeat != draw.TargetSeat || outcome.RequestedCount != draw.RequestedCount ||
            outcome.SequenceAfter != PrepDiscardSequence || outcome.SequenceBefore > outcome.SequenceAfter ||
            outcome.ActualDrawIds.Count > draw.RequestedCount || outcome.ActualDrawIds.Distinct().Count() != outcome.ActualDrawIds.Count ||
            outcome.Replacement is not null && outcome.ActualDrawIds.Count != 0 ||
            !_cardMovements.Where(m => m.Sequence > outcome.SequenceBefore && m.Sequence <= outcome.SequenceAfter &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(draw.TargetSeat) && m.Reason == PrepDiscardImmediateDrawReason)
                .OrderBy(m => m.Sequence).Select(m => m.CardId).SequenceEqual(outcome.ActualDrawIds))
            throw new InvalidOperationException("An immediate preparation draw lost its exact native request and actual material return.");
        var cancelled = !_players[frame.OwnerSeat].IsAlive || !_players[draw.TargetSeat].IsAlive || _winner != Winner.None;
        if (cancelled && outcome.ActualDrawIds.Count != 0)
            throw new InvalidOperationException("A cancelled preparation draw cannot manufacture paid physical cards.");
        ReplaceRuntimeTop(frame = frame with { PrepDiscard = draft with { Stage = PrepDiscardStage.ImmediateDrawChildren,
            ImmediateDraw = draw with { Outcome = outcome, Cancelled = cancelled } },
            PendingMovementContinuation = new(draw.TargetSeat, 0, null), ReexecuteParticipantInstruction = true });
        AdvanceEventRulesAndQueueFact(new PrepDiscardImmediateDrawPaidEvent(frame.Id, draw.TargetSeat, draw.RequestedCount,
            outcome.ActualDrawIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId, outcome.Replacement is not null, cancelled));
        if (ContinuePrepDiscardImmediateDraw(frame) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
    }

    private SkillProgramStepOutcome ContinuePrepDiscardImmediateDraw(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (!ValidPrepDiscard(frame) || frame.PrepDiscard is not { Stage: PrepDiscardStage.ImmediateDrawChildren, ImmediateDraw.Outcome: not null })
            throw new InvalidOperationException("An immediate preparation draw cannot bypass its exact native draw return.");
        if (TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(frame.Id) || TryBeginAdvancedSkillsChanged(frame.Id)) return SkillProgramStepOutcome.AwaitChild;
        frame = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(frame = frame with { PendingMovementContinuation = null });
        return FinishPrepDiscard(frame, !frame.PrepDiscard!.ImmediateDraw!.Cancelled);
    }

    private bool ValidPrepDiscardImmediateDraw(ProgramSkillFrame frame)
    {
        var draft = frame.PrepDiscard!;
        if (draft.ImmediateDraw is not { } draw)
            return draft.Stage is not (PrepDiscardStage.ImmediateDrawAttempt or PrepDiscardStage.ImmediateDrawChildren);
        if (!IsImmediatePrepDiscard(frame) || draft.Stage is not (PrepDiscardStage.ImmediateDrawAttempt or PrepDiscardStage.ImmediateDrawChildren or PrepDiscardStage.Complete) ||
            draft.TargetSeat != draw.TargetSeat || draft.TargetPayment is not { } paid || draw.RequestedCount <= 0 ||
            draw.RequestedCount != paid.NonEquipmentCount || draft.OwnerPayment is not null || draft.Deferred is not null ||
            CompleteProgramEventHistory().OfType<PrepDiscardImmediateBenefitChosenEvent>().Count(e => e.ProgramFrameId == frame.Id && e.DrawTarget &&
                e.TargetSeat == draw.TargetSeat && e.Count == draw.RequestedCount) != 1 ||
            CompleteProgramEventHistory().OfType<PrepDiscardImmediateDrawRequestedEvent>().Count(e => e.ProgramFrameId == frame.Id &&
                e.TargetSeat == draw.TargetSeat && e.RequestedCount == draw.RequestedCount) != 1) return false;
        if (draft.Stage == PrepDiscardStage.ImmediateDrawAttempt)
        {
            if (draw.Outcome is not null || draw.Cancelled || frame.PendingMovementContinuation is not null ||
                CompleteProgramEventHistory().OfType<PrepDiscardImmediateDrawPaidEvent>().Any(e => e.ProgramFrameId == frame.Id)) return false;
            return frame.NativeDraw is null || frame.NativeDraw is { ReturnKind: NativeDrawAttemptReturnKind.ProgramOperation,
                Operation: SkillProgramEffectOp.ResolvePrepDiscardOrImmediateDraw, ResultBind: null } native &&
                native.InstructionIndex == frame.InstructionIndex && native.Recipients.SequenceEqual([draw.TargetSeat]) &&
                native.RequestedCount == draw.RequestedCount && native.Reason == PrepDiscardImmediateDrawReason;
        }
        if (frame.NativeDraw is not null || draw.Outcome is not { } outcome || outcome.RecipientSeat != draw.TargetSeat ||
            outcome.RequestedCount != draw.RequestedCount || outcome.SequenceBefore < paid.SequenceAfter || outcome.SequenceAfter < outcome.SequenceBefore ||
            outcome.SequenceAfter > PrepDiscardSequence || outcome.ActualDrawIds.Count > draw.RequestedCount ||
            outcome.ActualDrawIds.Distinct().Count() != outcome.ActualDrawIds.Count ||
            (outcome.Replacement is not null || draw.Cancelled) && outcome.ActualDrawIds.Count != 0 ||
            !_cardMovements.Where(m => m.Sequence > outcome.SequenceBefore && m.Sequence <= outcome.SequenceAfter &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(draw.TargetSeat) && m.Reason == PrepDiscardImmediateDrawReason)
                .OrderBy(m => m.Sequence).Select(m => m.CardId).SequenceEqual(outcome.ActualDrawIds) ||
            CompleteProgramEventHistory().OfType<PrepDiscardImmediateDrawPaidEvent>().Count(e => e.ProgramFrameId == frame.Id &&
                e.TargetSeat == draw.TargetSeat && e.RequestedCount == draw.RequestedCount && e.ActualCount == outcome.ActualDrawIds.Count &&
                e.SequenceBefore == outcome.SequenceBefore && e.SequenceAfter == outcome.SequenceAfter && e.AttemptFrameId == outcome.AttemptFrameId &&
                e.Replaced == (outcome.Replacement is not null) && e.Cancelled == draw.Cancelled) != 1) return false;
        return outcome.AttemptFrameId is not { } attempt || CompleteProgramEventHistory().OfType<NativeDrawAttemptDecidedEvent>().Count(e =>
            e.AttemptFrameId == attempt && e.ParentFrameId == frame.Id && e.RecipientSeat == draw.TargetSeat &&
            e.RequestedCount == draw.RequestedCount && e.Replaced == (outcome.Replacement is not null)) == 1;
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 1246)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 1246)]
    private bool ContinuePaidImmediatePrepDiscard(ProgramSkillFrame frame) =>
        frame.PrepDiscard is { TargetPayment: not null } && IsImmediatePrepDiscard(frame) && ValidPrepDiscard(frame);
}
