namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidHalfHandDraw(ProgramSkillFrame f)
    {
        if (f.HalfHandDraw is not { } paid || f.InstructionIndex != 1 || paid.InstructionIndex != 0 ||
            paid.Source != HalfHandDebtSource(f) || paid.GameplayHash != f.GameplayHash || paid.ActualTurnNumber != _turnNumber ||
            paid.TurnOwnerSeat != _currentSeat || paid.TurnOwnerSeat != f.OwnerSeat || paid.DrawWindowFrameId != f.WindowContext?.ParentFrameId ||
            paid.RequestedCount != 2 || paid.ActualCount < 0 || paid.ActualCount > paid.RequestedCount || paid.SequenceBefore < 0 || paid.SequenceAfter < paid.SequenceBefore ||
            !HalfHandDebtLifecycleMatches(f, SkillProgramTriggerWindow.DrawPhaseStarting) ||
            ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).Instructions is not
                [{ Op: SkillProgramEffectOp.DrawExtraAndArmHalfHandSupport, StateId: { } state }] || paid.StateId != state) return false;
        var reason = $"skill-program.{f.SkillId}.{SkillProgramEffectOp.DrawExtraAndArmHalfHandSupport}";
        return _cardMovements.Count(m => m.Sequence > paid.SequenceBefore && m.Sequence <= paid.SequenceAfter && m.From == CardLocation.DrawPile &&
            m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == reason) == paid.ActualCount &&
            CompleteProgramEventHistory().OfType<HalfHandDrawIssuedEvent>().Count(e => e.ProgramFrameId == f.Id && e.DrawWindowFrameId == paid.DrawWindowFrameId &&
                e.Source == paid.Source && e.GameplayHash == paid.GameplayHash && e.StateId == paid.StateId && e.ActualTurnNumber == paid.ActualTurnNumber &&
                e.TurnOwnerSeat == paid.TurnOwnerSeat && e.RequestedCount == 2 && e.ActualCount == paid.ActualCount && e.SequenceBefore == paid.SequenceBefore && e.SequenceAfter == paid.SequenceAfter) == 1;
    }
    private bool ValidHalfHandGift(ProgramSkillFrame f)
    {
        if (f.HalfHandGift is not { } paid || f.InstructionIndex != 3 || paid.InstructionIndex != 2 ||
            !HalfHandDebtLifecycleMatches(f, SkillProgramTriggerWindow.AfterNormalDraw) || paid.Source != HalfHandDebtSource(f) || paid.GameplayHash != f.GameplayHash ||
            paid.ActualTurnNumber != _turnNumber || paid.TurnOwnerSeat != _currentSeat || paid.TurnOwnerSeat != f.OwnerSeat ||
            f.SelectedTargetSeats is not [var recipient] || recipient != paid.RecipientSeat || recipient == f.OwnerSeat || !IsValidPlayerSeat(recipient) ||
            paid.HandCountBefore <= 5 || paid.CardIds.Count != paid.HandCountBefore / 2 || paid.CardIds.Distinct().Count() != paid.CardIds.Count ||
            paid.SequenceBefore < 0 || paid.SequenceAfter < paid.SequenceBefore ||
            CompleteProgramEventHistory().OfType<HalfHandDrawIssuedEvent>().SingleOrDefault(e => e.ProgramFrameId == paid.DrawProgramFrameId) is not { } drawn ||
            drawn.Source.OwnerSeat != f.OwnerSeat || drawn.Source.SkillId != f.SkillId || drawn.Source.SkillInstanceId != f.SkillInstanceId ||
            drawn.GameplayHash != f.GameplayHash || drawn.StateId != paid.StateId || drawn.ActualTurnNumber != _turnNumber) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!);
        if (plan.Instructions.Count != 3 || plan.Instructions[2].Op != SkillProgramEffectOp.GiveHalfHandAndIssueTargetSupport ||
            plan.Instructions[2].StateId != paid.StateId || f.CardSetBindings.SingleOrDefault(b => b.Name == plan.Instructions[2].SourceBind) is not { } bound ||
            !bound.CardIds.SequenceEqual(paid.CardIds) || bound.SourceLocations.Any(l => l != CardLocation.Hand(f.OwnerSeat))) return false;
        var reason = $"skill-program.{f.SkillId}.{SkillProgramEffectOp.GiveHalfHandAndIssueTargetSupport}";
        return paid.CardIds.All(id => _cardMovements.Count(m => m.Sequence > paid.SequenceBefore && m.Sequence <= paid.SequenceAfter && m.CardId == id &&
            m.From == CardLocation.Hand(f.OwnerSeat) && m.To == CardLocation.Hand(recipient) && m.Reason.Value == reason) == 1) &&
            CompleteProgramEventHistory().OfType<HalfHandTargetSupportIssuedEvent>().Count(e => e.ProgramFrameId == f.Id && e.DrawProgramFrameId == paid.DrawProgramFrameId &&
                e.Source == paid.Source && e.GameplayHash == paid.GameplayHash && e.StateId == paid.StateId && e.ActualTurnNumber == _turnNumber &&
                e.OwnerSeat == f.OwnerSeat && e.RecipientSeat == recipient && e.ActualDeliveredCount == paid.CardIds.Count &&
                e.SequenceBefore == paid.SequenceBefore && e.SequenceAfter == paid.SequenceAfter) == 1;
    }
    private bool ValidPhaseHandExchange(ProgramSkillFrame f)
    {
        if (f.PhaseHandExchange is not { } paid || f.WindowContext is not null || f.InstructionIndex != 1 || paid.InstructionIndex != 0 ||
            paid.Source != HalfHandDebtSource(f) || paid.GameplayHash != f.GameplayHash || paid.ActualTurnNumber != _turnNumber ||
            paid.TurnOwnerSeat != _currentSeat || paid.TurnOwnerSeat != f.OwnerSeat || paid.PhaseInstanceId != _cardUseDebitPhaseInstanceId || _phase != TurnPhase.Play ||
            f.SelectedTargetSeats is not [var a, var b] || a != paid.FirstSeat || b != paid.SecondSeat || a == b || a == f.OwnerSeat || b == f.OwnerSeat ||
            !IsValidPlayerSeat(a) || !IsValidPlayerSeat(b) || paid.FrozenDifference != Math.Abs(paid.FirstCardIds.Count - paid.SecondCardIds.Count) ||
            paid.FirstCardIds.Concat(paid.SecondCardIds).Distinct().Count() != paid.FirstCardIds.Count + paid.SecondCardIds.Count ||
            paid.SequenceBefore < 0 || paid.SequenceAfter < paid.SequenceBefore ||
            ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).Instructions is not
                [{ Op: SkillProgramEffectOp.ExchangeHandsAndArmPhaseDebt, StateId: { } state }] || paid.StateId != state) return false;
        if (!paid.Issued) return true;
        var reason = $"skill-program.{f.SkillId}.exchange";
        bool Moved(int id, int from, int to) => _cardMovements.Count(m => m.Sequence > paid.SequenceBefore && m.Sequence <= paid.SequenceAfter && m.CardId == id &&
            m.From == CardLocation.Hand(from) && m.To == CardLocation.Processing && m.Reason.Value == reason) == 1 &&
            _cardMovements.Count(m => m.Sequence > paid.SequenceBefore && m.Sequence <= paid.SequenceAfter && m.CardId == id &&
                m.From == CardLocation.Processing && m.To == CardLocation.Hand(to) && m.Reason.Value == reason) == 1;
        return paid.FirstCardIds.All(id => Moved(id, a, b)) && paid.SecondCardIds.All(id => Moved(id, b, a)) &&
            CompleteProgramEventHistory().OfType<PhaseHandExchangeDebtIssuedEvent>().Count(e => e.ProgramFrameId == f.Id &&
                e.Source == paid.Source && e.GameplayHash == f.GameplayHash && e.StateId == state && e.ActualTurnNumber == _turnNumber &&
                e.TurnOwnerSeat == f.OwnerSeat && e.PhaseInstanceId == paid.PhaseInstanceId && e.FirstSeat == a && e.SecondSeat == b &&
                e.FrozenDifference == paid.FrozenDifference && e.FirstCount == paid.FirstCardIds.Count && e.SecondCount == paid.SecondCardIds.Count &&
                e.SequenceBefore == paid.SequenceBefore && e.SequenceAfter == paid.SequenceAfter) == 1;
    }
    private bool ValidPhaseHandDebtPayment(ProgramSkillFrame f)
    {
        if (f.PhaseHandDebtPayment is not { } paid || f.InstructionIndex is < 1 or > 3 || paid.InstructionIndex != 0 ||
            !HalfHandDebtLifecycleMatches(f, SkillProgramTriggerWindow.PlayEnding) || paid.ActualTurnNumber != _turnNumber ||
            paid.PhaseInstanceId != _cardUseDebitPhaseInstanceId || paid.RequiredPaymentCount < 0 || paid.RequiredPaymentCount > paid.FrozenDifference ||
            paid.SequenceBefore < 0 || CompleteProgramEventHistory().OfType<PhaseHandExchangeDebtIssuedEvent>().SingleOrDefault(e => e.ProgramFrameId == paid.ExchangeProgramFrameId) is not { } due ||
            due.Source.OwnerSeat != f.OwnerSeat || due.Source.SkillId != f.SkillId || due.Source.SkillInstanceId != f.SkillInstanceId ||
            due.GameplayHash != f.GameplayHash || due.StateId != paid.StateId || due.ActualTurnNumber != _turnNumber ||
            due.PhaseInstanceId != paid.PhaseInstanceId || due.FrozenDifference != paid.FrozenDifference) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!);
        return plan.Instructions.Count == 3 && plan.Instructions[0].Op == SkillProgramEffectOp.SelectFrozenHandExchangeDebtPayment &&
            plan.Instructions[0].StateId == paid.StateId && plan.Instructions[0].ResultBind == paid.ResultBind &&
            CompleteProgramEventHistory().OfType<PhaseHandExchangeDebtPaymentStartedEvent>().Count(e => e.ProgramFrameId == f.Id &&
                e.ExchangeProgramFrameId == paid.ExchangeProgramFrameId && e.Source == HalfHandDebtSource(f) && e.GameplayHash == f.GameplayHash &&
                e.StateId == paid.StateId && e.ActualTurnNumber == _turnNumber && e.OwnerSeat == f.OwnerSeat &&
                e.PhaseInstanceId == paid.PhaseInstanceId && e.FrozenDifference == paid.FrozenDifference && e.RequiredPaymentCount == paid.RequiredPaymentCount) == 1;
    }
    private void AssertHalfHandPhaseDebt(ProgramSkillFrame f)
    {
        if (f.HalfHandDraw is not null && !ValidHalfHandDraw(f) || f.HalfHandGift is not null && !ValidHalfHandGift(f) ||
            f.PhaseHandExchange is not null && !ValidPhaseHandExchange(f) || f.PhaseHandDebtPayment is not null && !ValidPhaseHandDebtPayment(f))
            throw new InvalidOperationException("A half-hand/phase-debt receipt lost its real producer, source or exact ledger.");
        if (f.HalfHandSupport is { } aid)
        {
            if (_resolutionStack.FirstOrDefault(frame => frame.Id == f.WindowContext?.ParentFrameId) is not ActualUseTargetWindowFrame parent ||
                !HalfHandSupportPaymentMatches(f, parent, aid.Paid) || aid.Paid && aid.Declined ||
                !aid.Paid && aid.CardId is not null || !aid.Paid && (aid.SequenceBefore != 0 || aid.SequenceAfter != 0))
                throw new InvalidOperationException("The recipient support lost its actual target owning return.");
        }
    }
    private bool IsHalfHandPhaseMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation movement) =>
        movement.SubjectSeat == f.OwnerSeat && movement.BeforeCount == 0 && movement.CoverageResultBind is null && (effect?.Op switch
        {
            SkillProgramEffectOp.DrawExtraAndArmHalfHandSupport => ValidHalfHandDraw(f),
            SkillProgramEffectOp.GiveHalfHandAndIssueTargetSupport => ValidHalfHandGift(f),
            SkillProgramEffectOp.ExchangeHandsAndArmPhaseDebt => f.PhaseHandExchange is { Issued: true } && ValidPhaseHandExchange(f),
            SkillProgramEffectOp.OfferHalfHandRecipientSupport => f.HalfHandSupport is { Paid: true } &&
                _resolutionStack.FirstOrDefault(frame => frame.Id == f.WindowContext?.ParentFrameId) is ActualUseTargetWindowFrame window && HalfHandSupportPaymentMatches(f, window, true),
            _ => false
        });
}
