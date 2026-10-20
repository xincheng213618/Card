namespace CardGame.Core;
public sealed partial class GameEngine
{
    private WuZhugeLiangCopyCreatedEvent? WuZglCopyFact(int id) => !HasWuZhugeLiangCapability ? null : ProgramEventHistory<WuZhugeLiangCopyCreatedEvent>().SingleOrDefault(e => e.CardId == id &&
        _generatedPhysicalCardIds.Contains(id) && GetAdvancedCard(id).Kind == e.Kind && GetAdvancedCard(id).Suit == e.Suit && GetAdvancedCard(id).Rank == e.Rank);
    private bool IsWuZglCopyBlocked(int actor, int id) => HasWuZhugeLiangCapability && WuZglTurnOpen(_turnNumber, _currentSeat) &&
        IsValidPlayerSeat(actor) && ProgramEventHistory<WuZhugeLiangCopyReturnedEvent>().Any(e => e.CardId == id && e.ActualTurn == _turnNumber && e.TurnOwner == _currentSeat &&
            WuZglCopyFact(id)?.Source.OwnerSeat == e.OwnerSeat && _cardMovements.Any(m => m.Sequence == e.ReturnSequence && m.CardId == id && m.To == CardLocation.Hand(e.OwnerSeat) && m.Reason == e.Reason));
    private bool IsWuZglUseMaterialForbidden(int actor, IReadOnlyList<int> ids, long? existingUseId = null)
    {
        var accepted = existingUseId is { } useId && LifecycleCardUse(useId) is { Action: { } a } && a.ActorSeat == actor &&
            a.PhysicalCards.Select(c => c.CardId).Order().SequenceEqual(ids.Order());
        return !accepted && ids.Any(id => IsWuZglCopyBlocked(actor, id));
    }
    private static bool WuZglNativeInputReason(CardMoveReason r) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_zhuge_liang_wumiao_WuZhugeLiang_Copies.WuZglNativeInputReason(r);
    private CardMovementRecord? WuZglCopyReturnEntry(Card card, CardLocation from, CardMoveReason reason)
    {
        if (WuZglCopyFact(card.Id) is not { } copy || !_players[copy.Source.OwnerSeat].IsAlive) return null;
        var owner = copy.Source.OwnerSeat;
        if (from == CardLocation.Processing)
        {
            if (!(reason == CardMoveReasons.UseFinished || reason == CardMoveReasons.ResponseFinished || reason == CardMoveReasons.NullificationFinished || reason == CardMoveReasons.IronChainFinished ||
                reason == CardMoveReasons.FireAttackFinished || reason == CardMoveReasons.DismantlementFinished || reason == CardMoveReasons.SnatchFinished)) return null;
            var entered = _cardMovements.LastOrDefault(m => m.CardId == card.Id && m.To == CardLocation.Processing);
            if (entered is null || entered.From.OwnerSeat != owner || !WuZglNativeInputReason(entered.Reason)) return null;
            if (entered.Reason.Value == "conversion.declaration.pay" && !_resolutionStack.Any(f => f.AcceptedDeclarationPayment is { Claimed: true } p && p.ActorSeat == owner && p.ProviderSeat == owner && p.Cost.CardId == card.Id)) return null;
            return entered;
        }
        if (from.Zone == CardZoneKind.Judgment && reason == CardMoveReasons.DelayedCardFinish)
        {
            var placed = _cardMovements.LastOrDefault(m => m.CardId == card.Id && m.To.Zone == CardZoneKind.Judgment);
            if (placed is null) return null;
            var entered = _cardMovements.LastOrDefault(m => m.CardId == card.Id && m.Sequence <= placed.Sequence && m.To == CardLocation.Processing);
            return entered is not null && entered.From.OwnerSeat == owner && WuZglNativeInputReason(entered.Reason) ? entered : null;
        }
        return null;
    }
    private CardLocation NormalizeWuZglCopyDestination(Card card, CardLocation from, CardLocation to, CardMoveReason reason) =>
        to == CardLocation.DiscardPile && WuZglCopyReturnEntry(card, from, reason) is not null && WuZglCopyFact(card.Id) is { } copy
            ? CardLocation.Hand(copy.Source.OwnerSeat) : to;
    private void RecordWuZglCopyMovement(CardMovementRecord move)
    {
        if (move.To.Zone != CardZoneKind.Hand || WuZglCopyFact(move.CardId) is not { } copy || move.To.OwnerSeat != copy.Source.OwnerSeat ||
            WuZglCopyReturnEntry(GetAdvancedCard(move.CardId), move.From, move.Reason) is not { } entry) return;
        AdvanceEventRulesAndQueueFact(new WuZhugeLiangCopyReturnedEvent(copy.Source.OwnerSeat, move.CardId, entry.Sequence, move.Sequence, move.Reason, _turnNumber, _currentSeat));
    }
    private bool IsWuZglReturnedCopyCost(CardActionContext action, CardActionCost cost, CardMovementRecord? finish = null)
    {
        if (WuZglCopyFact(cost.CardId) is not { } copy || action.ProviderSeat != copy.Source.OwnerSeat ||
            cost.From.OwnerSeat != copy.Source.OwnerSeat || !action.PhysicalCards.Contains(cost)) return false;
        return ProgramEventHistory<WuZhugeLiangCopyReturnedEvent>().Any(e => e.OwnerSeat == copy.Source.OwnerSeat && e.CardId == cost.CardId &&
            _cardMovements.Any(m => m.Sequence == e.EntrySequence && m.CardId == cost.CardId && m.From == cost.From && m.To == CardLocation.Processing && WuZglNativeInputReason(m.Reason)) &&
            _cardMovements.Any(m => m.Sequence == e.ReturnSequence && m.CardId == cost.CardId && m.From == CardLocation.Processing && m.To == CardLocation.Hand(copy.Source.OwnerSeat) &&
                m.Reason == e.Reason && e.ReturnSequence > e.EntrySequence && (finish is null || m == finish)));
    }
    private string? WuZglCopyLabel(int id) => WuZglCopyFact(id) is not { } copy ? null : IsWuZglCopyBlocked(copy.Source.OwnerSeat, id) ? "智哲·本回合禁用" : "智哲";
}
