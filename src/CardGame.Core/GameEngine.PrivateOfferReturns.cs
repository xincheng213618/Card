namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidPrivateOfferReceipt(ProgramSkillFrame f)
    {
        if (f.PrivateOffer is not { } r || r.InstructionIndex != f.InstructionIndex ||
            !ValidPrivateOfferDeposit(r.Location, out var original) || original != r.CardId ||
            r.Location.OwnerSeat != f.OwnerSeat || r.Location.PrivateTurnHold is not { DeferredOffer: { } o } hold ||
            hold.SkillId != f.SkillId || hold.SkillInstanceId != f.SkillInstanceId || o.GameplayHash != f.GameplayHash ||
            GetPausedPrivateOfferEffect(f) is not { } effect) return false;
        if (r.Stage == PrivateOfferStage.DepositChildren)
            return effect.Op == SkillProgramEffectOp.DepositBoundPrivateCardOffer && f.Id == o.DepositFrameId &&
                GetProgramBindingId(f) == o.DepositBindingId && f.SelectedTargetSeats.SequenceEqual([o.TargetSeat]) &&
                r.SequenceAfter > r.SequenceBefore && r.PaymentCardId is null && r.PaymentFrom is null;
        if (effect.Op != SkillProgramEffectOp.ResolveDeferredPrivateCardOffer || DeferredPrivateOfferParent(f) != r.Location ||
            CompleteProgramEventHistory().OfType<PrivateCardOfferViewedEvent>().Count(e => e.FrameId == f.Id &&
                e.DepositFrameId == hold.HoldId && e.TargetSeat == o.TargetSeat && e.ActualTurn == _turnNumber) != 1) return false;
        if (r.Stage == PrivateOfferStage.ChoosingExchange)
            return r.PaymentCardId is null && r.PaymentFrom is null && r.SequenceBefore == 0 && r.SequenceAfter == 0 &&
                _cardZones.GetLocation(r.CardId) == r.Location;
        if (r.PaymentCardId is { } paid)
        {
            if (r.PaymentFrom != CardLocation.Hand(o.TargetSeat) ||
                GetProgramCardCategory(GetAdvancedCard(paid).Kind) != GetProgramCardCategory(GetAdvancedCard(r.CardId).Kind) ||
                !PrivateOfferSegmentMatches(f, PrivateOfferStage.ExchangeChildren, paid, r.PaymentFrom, CardLocation.Hand(f.OwnerSeat), "exchange")) return false;
        }
        else if (r.Stage is PrivateOfferStage.ExchangeChildren or PrivateOfferStage.ObtainChildren) return false;
        return r.Stage switch
        {
            PrivateOfferStage.ExchangeChildren => true,
            PrivateOfferStage.ObtainChildren => PrivateOfferSegmentMatches(f, r.Stage, r.CardId, r.Location, CardLocation.Hand(o.TargetSeat), "obtain"),
            PrivateOfferStage.RemovalChildren or PrivateOfferStage.HpLoss => r.PaymentCardId is null &&
                PrivateOfferSegmentMatches(f, PrivateOfferStage.RemovalChildren, r.CardId, r.Location, CardLocation.DiscardPile, "remove"),
            _ => false
        };
    }
    private SkillProgramEffect? GetPausedPrivateOfferEffect(ProgramSkillFrame f) => f.InstructionIndex < 1 ? null :
        ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
    private bool PrivateOfferSegmentMatches(ProgramSkillFrame f, PrivateOfferStage stage, int id, CardLocation? from, CardLocation to, string reason)
    {
        var events = CompleteProgramEventHistory().OfType<PrivateCardOfferSegmentIssuedEvent>().Where(e => e.FrameId == f.Id && e.Stage == stage).ToArray();
        if (events is not [var fact] || fact.DepositFrameId != f.PrivateOffer!.Location.PrivateTurnHold!.HoldId || fact.Count != 1 || fact.After <= fact.Before) return false;
        var moves = _cardMovements.Where(m => m.Sequence > fact.Before && m.Sequence <= fact.After).ToArray();
        return moves is [var actual] && actual.CardId == id && actual.From == from && actual.To == to && actual.Reason.Value == PrivateOfferReason(f, reason);
    }
    private bool ResumePrivateOffer(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { PrivateOffer: { } r } f || f.Id != id) return false;
        if (!ValidPrivateOfferReceipt(f)) throw new InvalidOperationException("A private offer lost its exact original deposit, due cursor or paid segment.");
        if (r.Stage == PrivateOfferStage.ChoosingExchange) return true;
        if (f.PendingMovementContinuation is not null)
        { if (!TryDrainFireTargetMovement(f)) ReturnPrivateOfferMovement(f); return true; }
        if (r.Stage == PrivateOfferStage.HpLoss)
        {
            if (TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.Program)) return true;
            ReplaceRuntimeTop(f with { PrivateOffer = null }); FinishProgramSkill(GetActiveProgramFrame(id), true); return true;
        }
        throw new InvalidOperationException("A private offer resumed without its precise movement/HP return.");
    }
    private bool IsPrivateOfferMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.PrivateOffer is { Stage: not PrivateOfferStage.ChoosingExchange and not PrivateOfferStage.HpLoss } r &&
        effect == GetPausedPrivateOfferEffect(f) && ValidPrivateOfferReceipt(f) && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        pending.SubjectSeat == (r.Stage == PrivateOfferStage.DepositChildren ? f.OwnerSeat : r.Location.PrivateTurnHold!.DeferredOffer!.TargetSeat);
    private bool ReturnPrivateOfferMovement(ProgramSkillFrame f)
    {
        if (f.PrivateOffer is not { } r) return false;
        if (f.PendingMovementContinuation is not { } pending || !IsPrivateOfferMovement(f, GetPausedPrivateOfferEffect(f), pending))
            throw new InvalidOperationException("A private offer cannot clear a different program movement return.");
        if (TryDrainFireTargetMovement(f)) return true;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage == PrivateOfferStage.DepositChildren || r.Stage == PrivateOfferStage.ObtainChildren)
        { ReplaceRuntimeTop(f with { PrivateOffer = null }); FinishProgramSkill(GetActiveProgramFrame(f.Id), true); return true; }
        var target = r.Location.PrivateTurnHold!.DeferredOffer!.TargetSeat;
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[target].IsAlive)
        { ReplaceRuntimeTop(f with { PrivateOffer = null }); FinishProgramSkill(GetActiveProgramFrame(f.Id), false); return true; }
        if (r.Stage == PrivateOfferStage.ExchangeChildren)
        {
            if (_cardZones.GetLocation(r.CardId) != r.Location) throw new InvalidOperationException("A paid private exchange lost its deposited reward entity.");
            var before = PrivateOfferSequence;
            ReplaceRuntimeTop(f = f with { PrivateOffer = r with { Stage = PrivateOfferStage.ObtainChildren, SequenceBefore = before, SequenceAfter = before },
                PendingMovementContinuation = new(target, 0, null) });
            MoveCard(_cardZones.CardsAt(r.Location).Single(c => c.Id == r.CardId), r.Location, CardLocation.Hand(target), new(PrivateOfferReason(f, "obtain")));
            CommitPrivateOfferSegment(f.Id); f = GetActiveProgramFrame(f.Id);
            if (!TryDrainFireTargetMovement(f)) ReturnPrivateOfferMovement(f); return true;
        }
        if (r.Stage != PrivateOfferStage.RemovalChildren) throw new InvalidOperationException("Unexpected private offer payment stage.");
        ReplaceRuntimeTop(f = f with { PrivateOffer = r with { Stage = PrivateOfferStage.HpLoss } });
        if (new ProgramSkillHost(this).LoseHp(f.Id, f.SkillId, target, 1) != SkillProgramStepOutcome.AwaitChild) ResumePrivateOffer(f.Id);
        return true;
    }
    private void AssertPrivateOfferFrames()
    {
        foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>())
            if (f.PrivateOffer is not null && !ValidPrivateOfferReceipt(f)) throw new InvalidOperationException("Invalid deferred private offer receipt.");
        foreach (var location in _cardZones.PrivateTurnHoldLocations.Where(l => l.PrivateTurnHold?.DeferredOffer is not null && _cardZones.CardsAt(l).Count != 0))
            if (!ValidPrivateOfferDeposit(location, out var id) || _cardZones.CardsAt(location) is not [var card] || card.Id != id)
                throw new InvalidOperationException("An issued private deposit lost its one real original entity.");
    }
}
