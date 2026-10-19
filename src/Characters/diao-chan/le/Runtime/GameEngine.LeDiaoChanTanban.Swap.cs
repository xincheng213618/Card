namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsExactTanbanDrawEndCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (!HasLeDiaoChanTanbanCapability || candidate.SkillId != "le:tanban" ||
            candidate.BindingId != "swap-draw-end-hand-marks" || !IsValidPlayerSeat(candidate.OwnerSeat) ||
            _contentRegistry.GetSkill(candidate.SkillId).Program?.GameplayHash != candidate.GameplayHash ||
            GetProgramTrigger(candidate).Effects is not [{ Op: SkillProgramEffectOp.SwapTanbanHandMarks }] ||
            context.Window != SkillProgramTriggerWindow.DrawPhaseEnded || context.OwnerSeat != candidate.OwnerSeat ||
            context.SourceSeat != candidate.OwnerSeat || context.TargetSeat != candidate.OwnerSeat ||
            _phase != TurnPhase.Draw || _currentSeat != candidate.OwnerSeat ||
            _turnProgression.OwnerSeat != candidate.OwnerSeat || _turnProgression.TurnNumber != _turnNumber ||
            _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is not ProgramLifecycleTriggerWindowFrame
                { Window: SkillProgramTriggerWindow.DrawPhaseEnded,
                  Continuation: ProgramLifecycleContinuation.CompleteDrawPhaseEnded } parent ||
            parent.OwnerSeat != candidate.OwnerSeat || parent.DrawPhaseEndedDelayedEffects is not { } effects ||
            (effects & (int)DelayedTurnEffects.SkipDrawPhase) != 0 ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            parent.Candidates[parent.CandidateIndex] != candidate) return false;
        return true;
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29041,
        SkillProgramEffectOp.SwapTanbanHandMarks)]
    private bool CanOfferTanbanSwap(ProgramTriggerLegalityContext gate) =>
        IsExactTanbanDrawEndCandidate(gate.Candidate, gate.Context) &&
        _players[gate.Owner.Seat].IsAlive && GetHand(gate.Owner).Count > 0;

    private bool ExactTanbanDrawEndFrame(ProgramSkillFrame frame)
    {
        if (frame.SkillId != "le:tanban" || frame.TriggerId != "swap-draw-end-hand-marks" ||
            frame.ActivationId != frame.TriggerId || frame.InstructionIndex != 1 ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 ||
            frame.WindowContext is not { } context ||
            _contentRegistry.GetSkill(frame.SkillId).Program?.GameplayHash != frame.GameplayHash) return false;
        var candidate = new ProgramTriggerCandidate(frame.OwnerSeat, frame.SkillId, frame.TriggerId,
            frame.SkillInstanceId, frame.GameplayHash, GetProgramTrigger(frame).Priority, context.OccurrenceIndex);
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        return index > 0 && _resolutionStack[index - 1].Id == context.ParentFrameId &&
            IsExactTanbanDrawEndCandidate(candidate, context) &&
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == frame.Id &&
                e.OwnerSeat == frame.OwnerSeat && e.SkillId == frame.SkillId && e.BindingId == frame.TriggerId &&
                e.SkillInstanceId == frame.SkillInstanceId && e.Window == context.Window) == 1;
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29041)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29041)]
    private bool PermitCompletedTanbanSwap(ProgramSkillFrame frame) => ExactTanbanDrawEndFrame(frame) &&
        ProgramEventHistory<TanbanHandMarksSwappedEvent>().Where(e => e.FrameId == frame.Id).ToArray() is [var paid] &&
        paid.Source == TanbanSource(frame) && paid.GameplayHash == frame.GameplayHash &&
        paid.WindowId == frame.WindowContext!.ParentFrameId && paid.TurnNumber == _turnNumber && paid.HolderSeat == frame.OwnerSeat;

    private SkillProgramStepOutcome SwapTanbanHandMarks(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (!ExactTanbanDrawEndFrame(frame) ||
            ProgramEventHistory<TanbanHandMarksSwappedEvent>().Any(e => e.FrameId == frame.Id) ||
            ProgramEventHistory<TanbanEntityMarkChangedEvent>().Any(e => e.FrameId == frame.Id))
            throw new InvalidOperationException("Tanban inversion requires its once-accepted exact actual draw-end mother.");
        var owner = _players[frame.OwnerSeat];
        if (!owner.IsAlive || _winner != Winner.None || !HasRuntimeSkillInstance(owner, frame.SkillId, frame.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var source = TanbanSource(frame);
        // Snapshot the whole actual Hand once, so every entity inverts the same
        // pre-swap state. Equipment, judgments and Wooden Ox grain are absent.
        var hand = GetHand(owner).Select(card =>
        {
            var entry = _cardMovements.LastOrDefault(m => m.CardId == card.Id && m.To == CardLocation.Hand(owner.Seat));
            if (entry is null || entry.CardKind != card.Kind)
                throw new InvalidOperationException("Tanban inversion requires a genuine currently owned Hand entity.");
            return new TanbanEntityMarkChangedEvent(frame.Id, source, frame.GameplayHash, owner.Seat,
                card.Id, card.Kind, entry.Sequence, HasTanbanEntityMark(card.Id), !HasTanbanEntityMark(card.Id));
        }).ToArray();
        foreach (var change in hand) AdvanceEventRulesAndQueueFact(change);
        AdvanceEventRulesAndQueueFact(new TanbanHandMarksSwappedEvent(frame.Id, source, frame.GameplayHash,
            frame.WindowContext!.ParentFrameId, _turnNumber, owner.Seat, hand.Length));
        return SkillProgramStepOutcome.Continue;
    }
}
