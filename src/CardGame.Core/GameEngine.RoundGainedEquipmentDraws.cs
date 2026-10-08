namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string RoundGainedEquipmentDrawReason = "skill-program.round-gained.equipment-draw";

    private bool CanOfferRoundGainedEquipmentDraw(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawForRoundGainedEquipmentUse) ||
        ExactIssuedRoundGainedUseCandidate(candidate, context) is { } q &&
        !CompleteProgramEventHistory().OfType<RoundGainedEquipmentDrawIssuedEvent>().Any(e => SameRoundGainedQualification(e.Qualification, q));

    private RoundGainedUseQualification? ExactRoundGainedEquipmentDrawParent(ProgramSkillFrame frame)
    {
        if (frame.InstructionIndex != 1 || frame.TriggerId is null || frame.ActivationId != frame.TriggerId || frame.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCommitted } context ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0) return null;
        var q = ExactIssuedRoundGainedUseCandidate(new(frame.OwnerSeat, frame.SkillId, frame.TriggerId, frame.SkillInstanceId, frame.GameplayHash, 0), context,
            frame.RoundGainedEquipmentDrawReceipt is not null);
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        return q is not null && index > 0 && _resolutionStack[index - 1].Id == context.ParentFrameId &&
            CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == frame.Id && e.OwnerSeat == frame.OwnerSeat && e.SkillId == frame.SkillId &&
                e.BindingId == frame.TriggerId && e.SkillInstanceId == frame.SkillInstanceId && e.Window == context.Window) == 1 ? q : null;
    }

    private SkillProgramStepOutcome BeginRoundGainedEquipmentDraw(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        AssertRoundGainedEquipmentDraw(frame);
        var q = ExactRoundGainedEquipmentDrawParent(frame) ?? throw new InvalidOperationException("A round-gained equipment draw requires its exact qualified committed Use.");
        if (frame.RoundGainedEquipmentDrawReceipt is not null || frame.PendingMovementContinuation is not null ||
            CompleteProgramEventHistory().OfType<RoundGainedEquipmentDrawIssuedEvent>().Any(e => SameRoundGainedQualification(e.Qualification, q)) ||
            RoundGainedCurrentBoundary() is not { } boundary)
            throw new InvalidOperationException("A round-gained equipment Use cannot issue its draw twice or outside a real round.");
        var before = RoundGainedMovementSequence;
        ReplaceRuntimeTop(frame = frame with { RoundGainedEquipmentDrawReceipt = new(1, q, 0, before, before) { DrawRoundNumber = boundary.RoundNumber },
            PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        var drawn = DrawCards(_players[frame.OwnerSeat], 1, true, new(RoundGainedEquipmentDrawReason));
        frame = GetActiveProgramFrame(frame.Id);
        var receipt = frame.RoundGainedEquipmentDrawReceipt! with { DrawActual = drawn.Count, DrawnCardIds = drawn, MovementSequenceAfter = RoundGainedMovementSequence };
        ReplaceRuntimeTop(frame with { RoundGainedEquipmentDrawReceipt = receipt });
        // Exclusion is durable before a native gain observer can use, give away,
        // or return this actual drawn entity. It is keyed by policy, not instance.
        AdvanceEventRulesAndQueueFact(new RoundGainedEquipmentDrawIssuedEvent(frame.Id,
            new(frame.SkillId, frame.TriggerId!, frame.OwnerSeat, frame.SkillInstanceId), q, receipt.DrawActual,
            before, receipt.MovementSequenceAfter, receipt.DrawnCardIds) { DrawRoundNumber = receipt.DrawRoundNumber });
        AdvanceRuntimeProgram(frame.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    private bool ResumeRoundGainedEquipmentDraw(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != id || frame.RoundGainedEquipmentDrawReceipt is null) return false;
        AssertRoundGainedEquipmentDraw(frame);
        if (PreparationGameEnded()) return true;
        if (TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(frame.Id) || TryBeginAdvancedSkillsChanged(frame.Id)) return true;
        frame = GetActiveProgramFrame(id);
        var receipt = frame.RoundGainedEquipmentDrawReceipt!;
        AdvanceEventRulesAndQueueFact(new RoundGainedEquipmentDrawResolvedEvent(id,
            new(frame.SkillId, frame.TriggerId!, frame.OwnerSeat, frame.SkillInstanceId), receipt.Qualification, receipt.DrawActual));
        ReplaceRuntimeTop(frame = frame with { RoundGainedEquipmentDrawReceipt = null, PendingMovementContinuation = null });
        FinishProgramSkill(frame, true); return true;
    }

    private bool ReturnRoundGainedEquipmentDrawMovement(ProgramSkillFrame frame)
    {
        if (frame.RoundGainedEquipmentDrawReceipt is null || frame.PendingMovementContinuation is null) return false;
        AssertRoundGainedEquipmentDraw(frame); AdvanceRuntimeProgram(frame.Id); return true;
    }

    private bool ValidRoundGainedEquipmentDraw(ProgramSkillFrame frame)
    {
        if (frame.RoundGainedEquipmentDrawReceipt is not { } receipt || ExactRoundGainedEquipmentDrawParent(frame) is not { } q ||
            !SameRoundGainedQualification(q, receipt.Qualification) || receipt.InstructionIndex != frame.InstructionIndex || receipt.InstructionIndex != 1 ||
            receipt.DrawActual is < 0 or > 1 || receipt.DrawRoundNumber <= 0 || receipt.MovementSequenceBefore < 0 ||
            receipt.MovementSequenceAfter < receipt.MovementSequenceBefore || receipt.MovementSequenceAfter > RoundGainedMovementSequence ||
            receipt.DrawnCardIds.Count != receipt.DrawActual || receipt.DrawnCardIds.Distinct().Count() != receipt.DrawActual ||
            frame.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != frame.OwnerSeat) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var source = new CardConversionSource(frame.SkillId, frame.TriggerId!, frame.OwnerSeat, frame.SkillInstanceId);
        if (history.OfType<RoundGainedEquipmentDrawIssuedEvent>().Where(e => SameRoundGainedQualification(e.Qualification, q)).ToArray() is not [var issued] ||
            issued.ProgramFrameId != frame.Id || issued.Source != source || issued.DrawActual != receipt.DrawActual || issued.DrawRoundNumber != receipt.DrawRoundNumber ||
            issued.MovementSequenceBefore != receipt.MovementSequenceBefore || issued.MovementSequenceAfter != receipt.MovementSequenceAfter ||
            !issued.DrawnCardIds.SequenceEqual(receipt.DrawnCardIds) || history.OfType<RoundGainedEquipmentDrawResolvedEvent>().Any(e => SameRoundGainedQualification(e.Qualification, q)) ||
            history.OfType<RoundGainedRoundBoundaryEvent>().Count(e => e.RoundNumber == receipt.DrawRoundNumber && e.MovementSequence <= receipt.MovementSequenceBefore) != 1) return false;
        var invoice = _cardMovements.Where(m => m.Sequence > receipt.MovementSequenceBefore && m.Sequence <= receipt.MovementSequenceAfter &&
            m.Reason.Value == RoundGainedEquipmentDrawReason).ToArray();
        return invoice.Select(m => m.CardId).SequenceEqual(receipt.DrawnCardIds) &&
            invoice.All(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(frame.OwnerSeat)) &&
            (receipt.DrawActual != 0 || receipt.MovementSequenceBefore == receipt.MovementSequenceAfter);
    }

    private void AssertRoundGainedEquipmentDraw(ProgramSkillFrame frame)
    {
        if (frame.RoundGainedEquipmentDrawReceipt is null && (frame.TriggerId is null ||
            _contentRegistry.GetSkill(frame.SkillId).Program?.Triggers.SingleOrDefault(t => t.Id == frame.TriggerId)?.Effects
                .Any(e => e.Op == SkillProgramEffectOp.DrawForRoundGainedEquipmentUse) != true)) return;
        if (frame.RoundGainedEquipmentDrawReceipt is null)
        {
            if (CompleteProgramEventHistory().OfType<RoundGainedEquipmentDrawIssuedEvent>().Any(e => e.ProgramFrameId == frame.Id) &&
                !CompleteProgramEventHistory().OfType<RoundGainedEquipmentDrawResolvedEvent>().Any(e => e.ProgramFrameId == frame.Id))
                throw new InvalidOperationException("A paid round-gained equipment draw lost its owning invoice.");
            return;
        }
        if (!ValidRoundGainedEquipmentDraw(frame)) throw new InvalidOperationException("A round-gained equipment draw lost its qualified Use, exact source or once-paid actual entities.");
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        if (index + 1 < _resolutionStack.Count && !RoundGainedEquipmentDrawFirstChild(frame, _resolutionStack[index + 1]))
            throw new InvalidOperationException("A round-gained equipment draw retained an unrelated child.");
    }

    private bool IsRoundGainedEquipmentDrawAwaitedMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        frame.PendingMovementContinuation == pending && effect?.Op == SkillProgramEffectOp.DrawForRoundGainedEquipmentUse && ValidRoundGainedEquipmentDraw(frame);
}
