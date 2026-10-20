namespace CardGame.Core;

public sealed partial class GameEngine
{

    private bool TryResolveMouZhitianFireDiscard(CardUseFrame pending, int? selectedCardId)
    {
        if (selectedCardId is not { } id || _cardZones.GetLocation(id) != CardLocation.DrawPile ||
            !HasMouOperation(_players[pending.SourceSeat], SkillProgramEffectOp.MouZhitianView)) return false;
        var revealed = GetFireAttackRevealedCard(pending);
        var chosen = MouZhitianTop(pending.SourceSeat).SingleOrDefault(card => card.Id == id && card.Suit == revealed.Suit) ??
            throw new InvalidOperationException("Zhitian requires one matching-suit card from the current visible top cards.");
        ReplaceRuntimeTop(pending with { MouZhitianPayment = new(id, revealed.Suit, _cardMovements.Count) });
        MoveCard(chosen, CardLocation.DrawPile, CardLocation.DiscardPile, CardMoveReasons.FireAttackDiscard);
        ContinueMouZhitianFirePayment(pending.Id); return true;
    }
    private void ContinueMouZhitianFirePayment(long id)
    {
        if (_resolutionStack.LastOrDefault() is not CardUseFrame { MouZhitianPayment: { } receipt } use || use.Id != id ||
            use.FireAttackSelection?.RevealedCardId is not { } revealedId ||
            !_cardMovements.Any(move => move.Sequence > receipt.MovementSequence && move.CardId == receipt.CardId &&
                move.From == CardLocation.DrawPile && move.To == CardLocation.DiscardPile && move.Reason == CardMoveReasons.FireAttackDiscard))
            throw new InvalidOperationException("Zhitian lost its once-paid deck replacement and owning FireAttack.");
        if (TryBeginCardsMovedProgramWindow(id)) return;
        var target = GetFireAttackTargetSeat(use); var allowed = _players[use.SourceSeat].IsAlive && _players[target].IsAlive && _winner == Winner.None;
        AdvanceEventRulesAndQueueFact(new FireAttackResolvedEvent(id, use.SourceSeat, target, revealedId,
            receipt.RevealedSuit, receipt.CardId, allowed));
        UpdateLifecycleCardUse(id, current => current with { MouZhitianPayment = null, FireAttackSelection = null });
        var card = GetTrickRepresentation(id, use.CardId, true);
        if (!allowed) { MoveFinishedTrickCard(id, card); FinishCardUse(id, card, use.CardKind); return; }
        var attack = new CardAttackHandle(this, id, use.SourceSeat, target,
            IsMouZhijueZeroUse(id) || IsTieredRoundZeroUse(id) ? null : card,
            damageAmount: 1, playedCardKind: CardKind.FireAttack, physicalCards: GetCardUsePhysicalCards(id));
        ActiveCardAttack = attack;
        if (!ApplyAttackDamage(attack)) CompleteAttack(attack);
    }
}
