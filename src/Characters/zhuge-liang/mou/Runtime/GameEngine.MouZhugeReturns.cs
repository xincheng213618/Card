namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Both producers prove their own issuance before sharing native zero-entity
    // processing boundaries. The tiered producer's receipt is not fabricated.
    private bool IsZeroEntityUse(long id) => IsTieredRoundZeroUse(id) || IsMouZhijueZeroUse(id) || IsChosenAssaultUse(id);
    private bool IsZeroEntityProcessingParent(long id, int cardId) => cardId == 0 && IsZeroEntityUse(id);
    private bool MatchesMouZhijueUseAction(long id, CardActionContext action, int cardId) =>
        cardId == 0 && IsMouZhijueZeroUse(id) && LifecycleCardUse(id)?.Action is { } issued &&
        action.ActionId == issued.ActionId && action.Type == CardActionType.Use &&
        action.EffectiveKind == issued.EffectiveKind && action.PhysicalCards.Count == 0 &&
        action.ConversionChain.Count == 0 && action.ActorSeat == issued.ActorSeat && action.ProviderSeat == issued.ProviderSeat;

    private bool IsMouZhijueZeroAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (!IsMouZhijueZeroUse(attack.ResolutionId) || attack.Card is not null || attack.PhysicalCards.Count != 0 ||
            LifecycleCardUse(attack.ResolutionId) is not { } use || attack.EffectiveCardKind != use.CardKind) return false;
        if (IsSlashCard(use.CardKind) || use.CardKind is CardKind.Duel or CardKind.FireAttack)
            return processing.Count == 0 && attack.SourceSeat == use.SourceSeat &&
                (use.CardKind != CardKind.FireAttack || CompleteProgramEventHistory().OfType<FireAttackResolvedEvent>().Any(fact =>
                    fact.ResolutionId == use.Id && fact.CausedDamage && fact.MatchingDiscardCardId > 0));
        return processing.Count == 0 && use.CardKind is CardKind.BarbarianAssault or CardKind.ArrowBarrage &&
            ActiveGroupCard is { } group && group.ResolutionId == use.Id && group.DamageSourceSeat == attack.SourceSeat &&
            group.CurrentAttack?.ResolutionId == use.Id;
    }
    private bool IsMouZhijueBorrowedAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing) =>
        ActiveBorrowedSword is { ActiveAttack: { } child } borrowed && SameAttackOwner(child, attack) &&
        IsMouZhijueZeroUse(borrowed.ResolutionId) && attack.Card is not null &&
        processing.Select(c => c.Id).Order().SequenceEqual(attack.PhysicalCards.Where(c =>
            _cardZones.GetLocation(c.Id) == CardLocation.Processing).Select(c => c.Id).Order());

    private bool FinishMouZhijueZeroAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false;
        if (!IsMouZhijueZeroUse(attack.ResolutionId) || attack.Card is not null ||
            attack.EffectiveCardKind is not { } kind || !IsSlashCard(kind) && kind is not (CardKind.Duel or CardKind.FireAttack)) return false;
        var use = LifecycleCardUse(attack.ResolutionId)!;
        SetCardUseStep(use.Id, ResolutionFrameStep.Completed);
        AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted,
            use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); }
        return true;
    }

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
