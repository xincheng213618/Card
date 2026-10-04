namespace CardGame.Core;

public sealed partial class GameEngine
{
    private long HalfHandDebtSequence => _cardMovements.Count == 0 ? 0 : _cardMovements[^1].Sequence;
    private CardConversionSource HalfHandDebtSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool HalfHandDebtSourceCurrent(ProgramSkillFrame f) => _winner == Winner.None && _players[f.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) &&
        EnabledSkillPrograms(_players[f.OwnerSeat]).Any(p => p.Id == f.SkillId && p.GameplayHash == f.GameplayHash);

    private bool HalfHandDebtLifecycleMatches(ProgramSkillFrame f, SkillProgramTriggerWindow window)
    {
        if (f.WindowContext is not { } context || context.Window != window || f.OwnerSeat != _currentSeat ||
            _resolutionStack.FirstOrDefault(frame => frame.Id == context.ParentFrameId) is not ProgramLifecycleTriggerWindowFrame parent ||
            parent.OwnerSeat != f.OwnerSeat || parent.Window != window || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex])) return false;
        return _turnNumber > 0;
    }
    private HalfHandDrawIssuedEvent? CurrentHalfHandExtraDraw(int owner, string skill, string instance, string hash, string state)
    {
        if (owner != _currentSeat) return null;
        return CompleteProgramEventHistory().OfType<HalfHandDrawIssuedEvent>().LastOrDefault(e => e.Source.OwnerSeat == owner &&
            e.Source.SkillId == skill && e.Source.SkillInstanceId == instance && e.GameplayHash == hash && e.StateId == state &&
            e.ActualTurnNumber == _turnNumber && e.TurnOwnerSeat == owner);
    }
    private HalfHandTargetSupportIssuedEvent? CurrentHalfHandSupport(int owner, string skill, string instance, string hash, string state)
    {
        var history = CompleteProgramEventHistory().ToArray();
        var issued = history.OfType<HalfHandTargetSupportIssuedEvent>().LastOrDefault(e => e.OwnerSeat == owner &&
            e.Source.SkillId == skill && e.Source.SkillInstanceId == instance && e.GameplayHash == hash && e.StateId == state && e.ActualDeliveredCount > 0);
        return issued is not null && !history.OfType<TurnStartedEvent>().Any(e => e.ActorSeat == owner && e.TurnNumber > issued.ActualTurnNumber) &&
            _players[owner].IsAlive && _players[issued.RecipientSeat].IsAlive && HasRuntimeSkillInstance(_players[owner], skill, instance) &&
            EnabledSkillPrograms(_players[owner]).Any(p => p.Id == skill && p.GameplayHash == hash) ? issued : null;
    }
    private PhaseHandExchangeDebtIssuedEvent? CurrentPhaseHandDebt(int owner, string skill, string instance, string hash, string state)
    {
        if (_phase != TurnPhase.Play || owner != _currentSeat) return null;
        var history = CompleteProgramEventHistory().ToArray();
        var issued = history.OfType<PhaseHandExchangeDebtIssuedEvent>().LastOrDefault(e => e.Source.OwnerSeat == owner &&
            e.Source.SkillId == skill && e.Source.SkillInstanceId == instance && e.GameplayHash == hash && e.StateId == state &&
            e.ActualTurnNumber == _turnNumber && e.TurnOwnerSeat == owner && e.PhaseInstanceId == _cardUseDebitPhaseInstanceId);
        return issued is not null && !history.OfType<PhaseHandExchangeDebtPaymentStartedEvent>().Any(e => e.ExchangeProgramFrameId == issued.ProgramFrameId) ? issued : null;
    }
    private bool CanRunHalfHandPhaseDebt(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects.FirstOrDefault(e => e.Op == SkillProgramEffectOp.GiveHalfHandAndIssueTargetSupport) is { } gift)
        {
            var draw = CurrentHalfHandExtraDraw(candidate.OwnerSeat, candidate.SkillId, candidate.SkillInstanceId, candidate.GameplayHash, gift.StateId!);
            return context.Window == SkillProgramTriggerWindow.AfterNormalDraw && draw is not null && GetHand(_players[candidate.OwnerSeat]).Count > 5 &&
                !CompleteProgramEventHistory().OfType<HalfHandTargetSupportIssuedEvent>().Any(e => e.DrawProgramFrameId == draw.ProgramFrameId);
        }
        if (trigger.Effects.FirstOrDefault(e => e.Op == SkillProgramEffectOp.SelectFrozenHandExchangeDebtPayment) is { } payment)
            return context.Window == SkillProgramTriggerWindow.PlayEnding &&
                CurrentPhaseHandDebt(candidate.OwnerSeat, candidate.SkillId, candidate.SkillInstanceId, candidate.GameplayHash, payment.StateId!) is not null;
        if (trigger.Effects.FirstOrDefault(e => e.Op == SkillProgramEffectOp.OfferHalfHandRecipientSupport) is { } aid)
            return context.Window == SkillProgramTriggerWindow.OtherActualUseTargeted &&
                CurrentHalfHandSupport(candidate.OwnerSeat, candidate.SkillId, candidate.SkillInstanceId, candidate.GameplayHash, aid.StateId!) is { } support &&
                GetHand(_players[support.RecipientSeat]).Count > 0;
        return true;
    }
    private SkillProgramStepOutcome DrawExtraHalfHandSupport(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        frame = GetActiveProgramFrame(frame.Id);
        if (frame.HalfHandDraw is not null || frame.InstructionIndex != 1 || !HalfHandDebtLifecycleMatches(frame, SkillProgramTriggerWindow.DrawPhaseStarting))
            throw new InvalidOperationException("Half-hand extra draw lost its real actual draw candidate.");
        var before = HalfHandDebtSequence;
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}");
        DrawProgramCards(frame.Id, frame.OwnerSeat, effect.Amount, null, null, SkillProgramCardSetVisibility.Private, reason);
        var after = HalfHandDebtSequence;
        var actual = _cardMovements.Count(m => m.Sequence > before && m.Sequence <= after && m.From == CardLocation.DrawPile &&
            m.To == CardLocation.Hand(frame.OwnerSeat) && m.Reason == reason);
        frame = GetActiveProgramFrame(frame.Id);
        var receipt = new ProgramHalfHandDrawReceipt(0, frame.WindowContext!.ParentFrameId, HalfHandDebtSource(frame), frame.GameplayHash,
            effect.StateId!, _turnNumber, _currentSeat, effect.Amount, actual, before, after);
        ReplaceRuntimeTop(frame with { HalfHandDraw = receipt });
        AdvanceEventRulesAndQueueFact(new HalfHandDrawIssuedEvent(frame.Id, receipt.DrawWindowFrameId, receipt.Source, receipt.GameplayHash,
            receipt.StateId, _turnNumber, _currentSeat, effect.Amount, actual, before, after));
        return AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat);
    }
    private SkillProgramStepOutcome GiveHalfHandSupport(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        frame = GetActiveProgramFrame(frame.Id);
        if (!HalfHandDebtLifecycleMatches(frame, SkillProgramTriggerWindow.AfterNormalDraw) || frame.InstructionIndex != 3 || frame.HalfHandGift is not null ||
            frame.SelectedTargetSeats is not [var recipient] || recipient == frame.OwnerSeat ||
            frame.CardSetBindings.SingleOrDefault(b => b.Name == effect.SourceBind) is not { } binding || binding.CardIds.Count == 0 ||
            binding.SourceLocations.Count != binding.CardIds.Count || binding.SourceLocations.Any(l => l != CardLocation.Hand(frame.OwnerSeat)) ||
            CurrentHalfHandExtraDraw(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, frame.GameplayHash, effect.StateId!) is not { } drawn)
            throw new InvalidOperationException("Half-hand support lost its exact real draw and selected owned gift.");
        if (!HalfHandDebtSourceCurrent(frame) || !_players[recipient].IsAlive) return SkillProgramStepOutcome.Continue;
        if (CompleteProgramEventHistory().OfType<HalfHandTargetSupportIssuedEvent>().Any(e => e.DrawProgramFrameId == drawn.ProgramFrameId))
            throw new InvalidOperationException("One original draw cannot issue a second half-hand recipient.");
        var handCount = GetHand(_players[frame.OwnerSeat]).Count;
        if (handCount <= 5 || binding.CardIds.Count != handCount / 2 || binding.CardIds.Any(id => _cardZones.GetLocation(id) != CardLocation.Hand(frame.OwnerSeat)))
            throw new InvalidOperationException("The original half-hand selection no longer owns its full real Hand payment.");
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}");
        var payment = new ProgramHalfHandGiftPayment(2, drawn.ProgramFrameId, HalfHandDebtSource(frame), frame.GameplayHash,
            effect.StateId!, _turnNumber, frame.OwnerSeat, recipient, handCount, binding.CardIds, HalfHandDebtSequence, HalfHandDebtSequence);
        ReplaceRuntimeTop(frame with { HalfHandGift = payment, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources(binding.CardIds, CardLocation.Hand(recipient), reason, (_, records) =>
        {
            if (records.Count != payment.CardIds.Count || records.Any(m => m.From != CardLocation.Hand(frame.OwnerSeat) || m.To != CardLocation.Hand(recipient)))
                throw new InvalidOperationException("Only a full actual half-hand payment may issue its recipient qualification.");
            var current = GetActiveProgramFrame(frame.Id); var after = records.Max(m => m.Sequence);
            ReplaceRuntimeTop(current with { HalfHandGift = payment with { SequenceAfter = after } });
            AdvanceEventRulesAndQueueFact(new HalfHandTargetSupportIssuedEvent(frame.Id, drawn.ProgramFrameId, payment.Source, frame.GameplayHash,
                effect.StateId!, _turnNumber, frame.OwnerSeat, recipient, records.Count, payment.SequenceBefore, after));
        });
        if (!TryBeginCardsMovedProgramWindow(frame.Id)) ReturnRuntimeProgramMovement(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private bool IsPayableDeferredHandPair(int owner, int first, int second) => IsValidPlayerSeat(first) && IsValidPlayerSeat(second) &&
        first != second && first != owner && second != owner && _players[first].IsAlive && _players[second].IsAlive &&
        Math.Abs(GetHand(_players[first]).Count - GetHand(_players[second]).Count) <= GetHand(_players[owner]).Count + GetEquipment(owner).Count;
    private bool HasPayableDeferredHandPair(int owner) => _players.Where(p => p.IsAlive && p.Seat != owner).Any(a =>
        _players.Any(b => IsPayableDeferredHandPair(owner, a.Seat, b.Seat)));
    private SkillProgramStepOutcome ExchangeAndArmPhaseHandDebt(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        frame = GetActiveProgramFrame(frame.Id);
        if (frame.PhaseHandExchange is not null || frame.WindowContext is not null || frame.InstructionIndex != 1 ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats is not [var a, var b] ||
            !HalfHandDebtSourceCurrent(frame) || frame.OwnerSeat != _currentSeat || _phase != TurnPhase.Play || !IsPayableDeferredHandPair(frame.OwnerSeat, a, b))
            throw new InvalidOperationException("Deferred hand exchange requires one original payable living pair in the actual Play phase.");
        var first = GetHand(_players[a]).Select(c => c.Id).Order().ToArray(); var second = GetHand(_players[b]).Select(c => c.Id).Order().ToArray();
        var receipt = new ProgramPhaseHandExchangeReceipt(0, HalfHandDebtSource(frame), frame.GameplayHash, effect.StateId!, _turnNumber,
            _currentSeat, _cardUseDebitPhaseInstanceId, a, b, Math.Abs(first.Length - second.Length), first, second,
            HalfHandDebtSequence, HalfHandDebtSequence, false);
        ReplaceRuntimeTop(frame = frame with { PhaseHandExchange = receipt });
        return ExchangeProgramSelectedTargetHands(frame);
    }
    // The old exchange producer calls this only before it opens any movement child.
    private void FinalizeDeferredHandExchange(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId); if (frame.PhaseHandExchange is not { Issued: false } receipt) return;
        var after = HalfHandDebtSequence; var reason = $"skill-program.{frame.SkillId}.exchange";
        bool Paid(int id, int from, int to) => _cardMovements.Count(m => m.Sequence > receipt.SequenceBefore && m.Sequence <= after &&
            m.CardId == id && m.From == CardLocation.Hand(from) && m.To == CardLocation.Processing && m.Reason.Value == reason) == 1 &&
            _cardMovements.Count(m => m.Sequence > receipt.SequenceBefore && m.Sequence <= after && m.CardId == id &&
                m.From == CardLocation.Processing && m.To == CardLocation.Hand(to) && m.Reason.Value == reason) == 1;
        if (receipt.FirstCardIds.Any(id => !Paid(id, receipt.FirstSeat, receipt.SecondSeat)) ||
            receipt.SecondCardIds.Any(id => !Paid(id, receipt.SecondSeat, receipt.FirstSeat)))
            throw new InvalidOperationException("The exchange debt cannot issue before all original hand entities transfer.");
        ReplaceRuntimeTop(frame with { PhaseHandExchange = receipt with { SequenceAfter = after, Issued = true } });
        AdvanceEventRulesAndQueueFact(new PhaseHandExchangeDebtIssuedEvent(frame.Id, receipt.Source, receipt.GameplayHash, receipt.StateId,
            receipt.ActualTurnNumber, receipt.TurnOwnerSeat, receipt.PhaseInstanceId, receipt.FirstSeat, receipt.SecondSeat,
            receipt.FrozenDifference, receipt.FirstCardIds.Count, receipt.SecondCardIds.Count, receipt.SequenceBefore, after));
    }
    private bool MatchesDeferredHandDebtCost(ProgramSkillFrame frame, string bind, Card card, CardLocation location) =>
        frame.PhaseHandDebtPayment is not { } paid || paid.ResultBind != bind ||
        location.OwnerSeat == frame.OwnerSeat && location.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
        !IsForeignEquipmentDiscardPrevented(frame.OwnerSeat, card, location, OwnedCardMoveIntent.Discard) &&
        !(location.Zone == CardZoneKind.Equipment && IsActiveProgramSourceEquipmentCard(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, card));
    private SkillProgramStepOutcome SelectPhaseHandDebtPayment(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        frame = GetActiveProgramFrame(frame.Id);
        if (frame.PhaseHandDebtPayment is not null || frame.InstructionIndex != 1 || !HalfHandDebtLifecycleMatches(frame, SkillProgramTriggerWindow.PlayEnding))
            throw new InvalidOperationException("Phase hand debt lost its exact current PlayEnding candidate.");
        var due = CurrentPhaseHandDebt(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, frame.GameplayHash, effect.StateId!);
        if (due is null) { SetProgramCardSet(frame.Id, effect.ResultBind!, [], SkillProgramCardSetVisibility.Private, []); return SkillProgramStepOutcome.Continue; }
        var available = GetHand(_players[frame.OwnerSeat]).Select(c => (Card: c, Location: CardLocation.Hand(frame.OwnerSeat)))
            .Concat(GetEquipment(frame.OwnerSeat).Select(c => (Card: c, Location: CardLocation.Equipment(frame.OwnerSeat))))
            .Count(item => !IsForeignEquipmentDiscardPrevented(frame.OwnerSeat, item.Card, item.Location, OwnedCardMoveIntent.Discard) &&
                !(item.Location.Zone == CardZoneKind.Equipment && IsActiveProgramSourceEquipmentCard(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, item.Card)));
        var count = Math.Min(due.FrozenDifference, available);
        ReplaceRuntimeTop(frame = frame with { PhaseHandDebtPayment = new(0, effect.StateId!, effect.ResultBind!, due.ProgramFrameId,
            _turnNumber, _cardUseDebitPhaseInstanceId, due.FrozenDifference, count, HalfHandDebtSequence) });
        AdvanceEventRulesAndQueueFact(new PhaseHandExchangeDebtPaymentStartedEvent(frame.Id, due.ProgramFrameId, HalfHandDebtSource(frame), frame.GameplayHash,
            effect.StateId!, _turnNumber, frame.OwnerSeat, _cardUseDebitPhaseInstanceId, due.FrozenDifference, count));
        return SelectProgramOwnedCards(frame, frame.OwnerSeat, count, null, [CardZoneKind.Hand, CardZoneKind.Equipment], effect.ResultBind!, 0, 0, [], []);
    }
    private SkillProgramEffect PhaseHandDebtOwnedSelectionEffect(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        if (effect.Op != SkillProgramEffectOp.SelectFrozenHandExchangeDebtPayment) return effect;
        var paid = frame.PhaseHandDebtPayment ?? throw new InvalidOperationException("Missing frozen phase debt selection.");
        if (paid.InstructionIndex != frame.InstructionIndex - 1 || paid.StateId != effect.StateId || paid.ResultBind != effect.ResultBind)
            throw new InvalidOperationException("The phase debt selection outlived its original frozen instruction.");
        return new(SkillProgramEffectOp.SelectOwnedCards, SkillProgramEffectTarget.Owner, paid.RequiredPaymentCount, effect.Condition,
            zones: [CardZoneKind.Hand, CardZoneKind.Equipment], resultBind: effect.ResultBind);
    }
    private sealed partial class ProgramSkillHost : IHalfHandPhaseDebtProgramHost
    {
        public SkillProgramStepOutcome DrawExtraAndArmHalfHandSupport(ProgramSkillFrame f, SkillProgramEffect e) => engine.DrawExtraHalfHandSupport(f, e);
        public SkillProgramStepOutcome GiveHalfHandAndIssueTargetSupport(ProgramSkillFrame f, SkillProgramEffect e) => engine.GiveHalfHandSupport(f, e);
        public SkillProgramStepOutcome ExchangeHandsAndArmPhaseDebt(ProgramSkillFrame f, SkillProgramEffect e) => engine.ExchangeAndArmPhaseHandDebt(f, e);
        public SkillProgramStepOutcome SelectFrozenHandExchangeDebtPayment(ProgramSkillFrame f, SkillProgramEffect e) => engine.SelectPhaseHandDebtPayment(f, e);
        public SkillProgramStepOutcome OfferHalfHandRecipientSupport(ProgramSkillFrame f, SkillProgramEffect e) => engine.OfferHalfHandSupport(f, e);
    }
}
