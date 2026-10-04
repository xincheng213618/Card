namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidPhaseHandSeizureReceipt(ProgramSkillFrame frame)
    {
        if (frame.PhaseHandSeizure is not { } receipt || frame.PhaseHandDebtReturn is not null || frame.InstructionIndex != 1 ||
            receipt.InstructionIndex != 1 || frame.TriggerId is not null || frame.WindowContext is not null ||
            frame.SelectedCardIds is not [var cost] || cost != receipt.CostCardId || frame.SelectedTargetSeats is not [var target] || target != receipt.TargetSeat ||
            receipt.ActualTurnNumber != _turnNumber || frame.OwnerSeat != _currentSeat || _phase != TurnPhase.Play && !PhaseHandSeizureTerminalState ||
            receipt.PhaseInstanceId != _cardUseDebitPhaseInstanceId || receipt.CostFrom.OwnerSeat != frame.OwnerSeat ||
            receipt.CostFrom.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || receipt.CostBefore < 0 || receipt.CostAfter <= receipt.CostBefore ||
            ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).Instructions is not
                [{ Op: SkillProgramEffectOp.DiscardTurnOverAndTakeHand, StateId: var continuation }] || continuation != receipt.ContinuationId)
            return false;
        var paid = CompleteProgramEventHistory().OfType<PhaseHandSeizurePaidEvent>().Where(e => e.FrameId == frame.Id).ToArray();
        if (paid is not [var fact] || fact != new PhaseHandSeizurePaidEvent(frame.Id, PhaseHandSeizureSource(frame), frame.GameplayHash,
                target, receipt.ActualTurnNumber, receipt.PhaseInstanceId, cost, receipt.CostFrom, receipt.CostBefore, receipt.CostAfter) ||
            CompleteProgramEventHistory().OfType<PhaseHandSeizurePaidEvent>().Count(e => e.Source.OwnerSeat == frame.OwnerSeat && e.Source.SkillId == frame.SkillId &&
                e.ActualTurnNumber == receipt.ActualTurnNumber && e.PhaseInstanceId == receipt.PhaseInstanceId) != 1)
            return false;
        var costs = _cardMovements.Where(m => m.Sequence > receipt.CostBefore && m.Sequence <= receipt.CostAfter &&
            m.Reason.Value == PhaseHandSeizureReason(frame, "payment")).ToArray();
        if (costs is not [var move] || move.CardId != cost || move.From != receipt.CostFrom ||
            !(move.To == CardLocation.DiscardPile || move.To == CardLocation.OutsideGame && GetAdvancedCard(cost).IsGeneralWeapon)) return false;
        var flipped = CompleteProgramEventHistory().OfType<PhaseHandSeizureTurnedEvent>().Where(e => e.FrameId == frame.Id).ToArray();
        var issued = CompleteProgramEventHistory().OfType<PhaseHandSeizureIssuedEvent>().Where(e => e.FrameId == frame.Id).ToArray();
        if (receipt.Stage == PhaseHandSeizureStage.PaymentChildren)
            return flipped.Length == 0 && issued.Length == 0 && receipt.TakenCardIds.Count == 0 && receipt.TakeBefore == 0 && receipt.TakeAfter == 0;
        if (flipped is not [var turn] || turn != new PhaseHandSeizureTurnedEvent(frame.Id, frame.OwnerSeat, receipt.WasFaceDown, !receipt.WasFaceDown)) return false;
        if (receipt.Stage == PhaseHandSeizureStage.FlipChildren)
            return issued.Length == 0 && receipt.TakenCardIds.Count == 0 && receipt.TakeBefore == 0 && receipt.TakeAfter == 0;
        if (receipt.Stage != PhaseHandSeizureStage.TakeChildren || receipt.TakeBefore < receipt.CostAfter || receipt.TakeAfter < receipt.TakeBefore ||
            receipt.TakenCardIds.Distinct().Count() != receipt.TakenCardIds.Count) return false;
        var same = target == frame.OwnerSeat;
        if (issued is not [var debt] || debt != new PhaseHandSeizureIssuedEvent(frame.Id, PhaseHandSeizureSource(frame), frame.GameplayHash,
                receipt.ContinuationId, target, receipt.ActualTurnNumber, receipt.PhaseInstanceId, same ? 0 : receipt.TakenCardIds.Count,
                same, receipt.TakeBefore, receipt.TakeAfter)) return false;
        var taken = _cardMovements.Where(m => m.Sequence > receipt.TakeBefore && m.Sequence <= receipt.TakeAfter &&
            m.Reason.Value == PhaseHandSeizureReason(frame, "take")).ToArray();
        if (same) return receipt.TakeAfter == receipt.TakeBefore && taken.Length == 0;
        return taken.Length == receipt.TakenCardIds.Count && receipt.TakenCardIds.All(id => taken.Count(m => m.CardId == id &&
            m.From == CardLocation.Hand(target) && m.To == CardLocation.Hand(frame.OwnerSeat)) == 1);
    }
    private bool ValidPhaseHandDebtReturnReceipt(ProgramSkillFrame frame)
    {
        if (frame.PhaseHandDebtReturn is not { } receipt || frame.PhaseHandSeizure is not null || frame.InstructionIndex != 1 || receipt.InstructionIndex != 1 ||
            GetPausedPrivateOfferEffect(frame)?.Op != SkillProgramEffectOp.ReturnIssuedPhaseHandDebt || receipt.RequiredCount < 0 ||
            receipt.FrozenHp < 0 || receipt.RequiredCount > receipt.FrozenHp || receipt.CardIds.Count != receipt.Locations.Count ||
            receipt.CardIds.Distinct().Count() != receipt.CardIds.Count || receipt.Locations.Any(l => l.OwnerSeat != frame.OwnerSeat ||
                l.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))) return false;
        var due = RequirePhaseHandDebtParent(frame);
        if (receipt.SeizureFrameId != due.FrameId || receipt.TargetSeat != due.TargetSeat) return false;
        var started = CompleteProgramEventHistory().OfType<PhaseHandDebtReturnStartedEvent>().Where(e => e.FrameId == frame.Id).ToArray();
        if (started is not [var begin] || begin != new PhaseHandDebtReturnStartedEvent(frame.Id, due.FrameId, frame.OwnerSeat, due.TargetSeat,
                due.ActualTurnNumber, due.PhaseInstanceId, receipt.FrozenHp, receipt.RequiredCount) ||
            CompleteProgramEventHistory().OfType<PhaseHandDebtReturnStartedEvent>().Count(e => e.SeizureFrameId == due.FrameId) != 1) return false;
        var settled = CompleteProgramEventHistory().OfType<PhaseHandDebtSettledEvent>().Where(e => e.SeizureFrameId == due.FrameId).ToArray();
        if (receipt.Stage == PhaseHandDebtReturnStage.Choosing)
        {
            if (receipt.RequiredCount <= 0 || receipt.CardIds.Count >= receipt.RequiredCount || receipt.SequenceBefore != 0 || receipt.SequenceAfter != 0) return false;
            if (PhaseHandSeizureTerminalState)
                return settled is [var closed] && closed == new PhaseHandDebtSettledEvent(due.FrameId, 0, frame.OwnerSeat, due.TargetSeat, "game-end", 0, 0, 0, 0) &&
                    !_cardMovements.Any(m => m.Sequence > due.After && m.Reason.Value == PhaseHandDebtReturnReason(frame) && m.From.OwnerSeat == frame.OwnerSeat);
            return settled.Length == 0 && receipt.CardIds.Select((id, index) => (id, index)).All(p =>
                _cardZones.GetLocation(p.id) == receipt.Locations[p.index] && PhaseHandDebtCards(frame).Any(c => c.Id == p.id));
        }
        if (receipt.Stage != PhaseHandDebtReturnStage.MovementChildren || receipt.CardIds.Count != receipt.RequiredCount ||
            receipt.SequenceBefore < 0 || receipt.SequenceAfter < receipt.SequenceBefore || settled is not [var end]) return false;
        var moving = receipt.CardIds.Select((id, index) => (id, from: receipt.Locations[index]))
            .Where(p => p.from != CardLocation.Hand(receipt.TargetSeat)).ToArray();
        if (end != new PhaseHandDebtSettledEvent(due.FrameId, frame.Id, frame.OwnerSeat, due.TargetSeat, moving.Length == 0 ? "same-hand" : "returned",
                receipt.RequiredCount, moving.Length, receipt.SequenceBefore, receipt.SequenceAfter)) return false;
        var moved = _cardMovements.Where(m => m.Sequence > receipt.SequenceBefore && m.Sequence <= receipt.SequenceAfter && m.Reason.Value == PhaseHandDebtReturnReason(frame)).ToArray();
        return moving.Length == 0 ? receipt.SequenceBefore == receipt.SequenceAfter && moved.Length == 0 : moved.Length == moving.Length &&
            moving.All(p => moved.Count(m => m.CardId == p.id && m.From == p.from && m.To == CardLocation.Hand(receipt.TargetSeat)) == 1);
    }
    private bool IsPhaseHandSeizureMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        pending.SubjectSeat == frame.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        (effect?.Op == SkillProgramEffectOp.DiscardTurnOverAndTakeHand && frame.PhaseHandSeizure?.Stage is
            PhaseHandSeizureStage.PaymentChildren or PhaseHandSeizureStage.TakeChildren && ValidPhaseHandSeizureReceipt(frame) ||
         effect?.Op == SkillProgramEffectOp.ReturnIssuedPhaseHandDebt && frame.PhaseHandDebtReturn?.Stage == PhaseHandDebtReturnStage.MovementChildren &&
            ValidPhaseHandDebtReturnReceipt(frame));
    private bool CanContinuePhaseHandSeizure(ProgramSkillFrame frame)
    {
        if (frame.PhaseHandSeizure is not null) return ValidPhaseHandSeizureReceipt(frame);
        if (frame.PhaseHandDebtReturn is not null) return ValidPhaseHandDebtReturnReceipt(frame);
        if (frame.WindowContext?.Window != SkillProgramTriggerWindow.PlayEnding || frame.TriggerId is null) return false;
        var candidate = new ProgramTriggerCandidate(frame.OwnerSeat, frame.SkillId, frame.TriggerId, frame.SkillInstanceId, frame.GameplayHash, 0, 0);
        if (!CanRunIssuedPhaseHandDebt(candidate, frame.WindowContext)) return false;
        _ = RequirePhaseHandDebtParent(frame); return true;
    }
    private void AssertPhaseHandSeizure(ProgramSkillFrame frame)
    {
        if (frame.PhaseHandSeizure is not null && !ValidPhaseHandSeizureReceipt(frame) ||
            frame.PhaseHandDebtReturn is not null && !ValidPhaseHandDebtReturnReceipt(frame))
            throw new InvalidOperationException("A phase-hand paid receipt lost its exact source, original phase, entity movement or typed return.");
    }
    private void AssertPhaseHandSeizurePrograms()
    {
        foreach (var frame in _resolutionStack.OfType<ProgramSkillFrame>()) AssertPhaseHandSeizure(frame);
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<PhaseHandSeizureIssuedEvent>().GroupBy(e => e.FrameId).Any(g => g.Count() != 1) ||
            history.OfType<PhaseHandDebtSettledEvent>().GroupBy(e => e.SeizureFrameId).Any(g => g.Count() != 1))
            throw new InvalidOperationException("A phase-hand issuance or settlement was duplicated.");
        foreach (var due in UnsettledPhaseHandSeizures())
            if (!_players[due.Source.OwnerSeat].IsAlive || !_players[due.TargetSeat].IsAlive ||
                due.ActualTurnNumber != _turnNumber || due.Source.OwnerSeat != _currentSeat || _phase != TurnPhase.Play ||
                due.PhaseInstanceId != _cardUseDebitPhaseInstanceId)
                throw new InvalidOperationException("An issued phase-hand obligation escaped its actual living Play termination.");
    }
    private bool IsPhaseHandDebtChoiceLegal(PromptChoice choice) => _resolutionStack.LastOrDefault() is ProgramSkillFrame frame &&
        frame.PhaseHandDebtReturn is { Stage: PhaseHandDebtReturnStage.Choosing } receipt && ValidPhaseHandDebtReturnReceipt(frame) &&
        _players[frame.OwnerSeat].IsAlive && _players[receipt.TargetSeat].IsAlive &&
        AssistedChoicesEqual([choice], PhaseHandDebtChoices(frame).Where(c => c.Id == choice.Id).ToArray());
}
