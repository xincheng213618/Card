namespace CardGame.Core;

public sealed partial class GameEngine
{
    private readonly Dictionary<int, (int Turn, HashSet<CardKind> Kinds)> _usedProgramBasicCardNames = [];

    private static CardKind ProgramBasicCardName(CardKind kind) => IsSlashCard(kind) ? CardKind.Slash : kind;

    private void RecordProgramUsedBasicCard(int actorSeat, CardKind effectiveKind)
    {
        if (CardCatalog.Get(effectiveKind).CategoryName != "基本牌") return;
        if (!_usedProgramBasicCardNames.TryGetValue(actorSeat, out var row) || row.Turn != _turnNumber)
            _usedProgramBasicCardNames[actorSeat] = row = (_turnNumber, []);
        row.Kinds.Add(ProgramBasicCardName(effectiveKind));
    }

    private bool HasProgramUsedBasicCardThisTurn(int actorSeat, CardKind kind) =>
        _usedProgramBasicCardNames.TryGetValue(actorSeat, out var row) && row.Turn == _turnNumber &&
        row.Kinds.Contains(ProgramBasicCardName(kind));

    private bool IsProgramAlternativeCost(CardActionContext? action, int cardId) =>
        action is not null && (action.Type == CardActionType.Use ||
            action.Type == CardActionType.Response && action.EffectiveKind == CardKind.Dodge &&
            action.ActorSeat == action.ProviderSeat && action.RequesterSeat is null) &&
        action.PhysicalCards.Any(cost => cost.CardId == cardId) &&
        action.ConversionChain.Any(source => ViewAsRule(source)?.CostDestination == SkillProgramCardDestination.DrawPileTop);

    private bool IsProgramResponseCardUse(CharacterState responder, CardKind effectiveKind) =>
        effectiveKind == CardKind.Dodge && ActiveFactionDefense is null &&
        ActiveCardAttack is { EffectiveCardKind: { } incomingKind } attack &&
        attack.TargetSeat == responder.Seat && IsSlashCard(incomingKind);

    private bool IsProgramTopDeckDodgeSource(CardConversionSource? source) =>
        source is not null && ViewAsRule(source) is
            { UseOnly: true, OutputKind: CardKind.Dodge, CostDestination: SkillProgramCardDestination.DrawPileTop };

    private void PaySingleCardResponse(Card card, CharacterState provider, CardKind effectiveKind,
        CardConversionSource? source)
    {
        if (source is not null && ViewAsRule(source) is { } usageRule &&
            (usageRule.TieredRoundConversion is not null || usageRule.UnusedOutputNameThisGame || usageRule.ConversionStateId is not null && usageRule.UsesPerPhase is not null)) ConsumeProgramViewAsUsage([source]);
        var alternative = IsProgramTopDeckDodgeSource(source);
        if (alternative && (source!.OwnerSeat != provider.Seat || !IsProgramResponseCardUse(provider, effectiveKind)))
            throw new InvalidOperationException("A top-deck response cost requires its own enabled Slash-defense Dodge use.");
        MoveCard(card, FindOwnedCardLocation(provider, card),
            alternative ? CardLocation.DrawPile : CardLocation.Processing, CardMoveReasons.Respond);
    }

    private void FinishSingleCardResponse(Card card, CardConversionSource? source)
    {
        if (IsProgramTopDeckDodgeSource(source))
        {
            if (_cardZones.GetLocation(card.Id) == CardLocation.Processing)
                throw new InvalidOperationException("A top-deck Dodge cost was incorrectly retained in Processing.");
            return;
        }
        MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.ResponseFinished);
    }

    private void FinishSingleBasicCardUseCost(CardUseFrame use, Card card)
    {
        if (TryFinishRoundDistinctBasicAlcoholCost(use, card)) return;
        if (SkipTieredRoundZeroFinishedMovement(use.Id, card)) return;
        if (SkipChainedStateBasicFinishedMovement(use.Id, card)) return;
        if (SkipDrawFundedDistinctBasicFinishedMovement(use.Id, card)) return;
        if (IsProgramAlternativeCost(use.Action, card.Id))
        {
            if (_cardZones.GetLocation(card.Id) == CardLocation.Processing)
                throw new InvalidOperationException("A top-deck basic-card cost was incorrectly retained in Processing.");
            return;
        }
        MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.UseFinished);
    }

    private CardLocation NormalizeProgramViewAsCostDestination(Card card, CardLocation from, CardLocation to, CardMoveReason reason)
    {
        if (to != CardLocation.Processing || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) return to;
        var use = _resolutionStack.OfType<CardUseFrame>().LastOrDefault(frame => frame.Action?.PhysicalCards.Any(cost => cost.CardId == card.Id) == true);
        return IsProgramAlternativeCost(use?.Action, card.Id) && use!.Action!.PhysicalCards.Single(cost => cost.CardId == card.Id).From == from
            ? CardLocation.DrawPile : to;
    }

    private bool TryValidateProgramAlternativeCostAttack(CardAttackHandle attack, IReadOnlyList<Card> processing, out bool valid)
    {
        valid = false;
        var use = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(frame => frame.Id == attack.ResolutionId);
        if (!attack.PhysicalCards.Any(card => IsProgramAlternativeCost(use?.Action, card.Id))) return false;
        var costs = attack.PhysicalCards.Where(card => IsProgramAlternativeCost(use!.Action, card.Id)).Select(card => card.Id).ToHashSet();
        // The spent cost can already have been drawn by a nested skill. Its
        // declared provenance remains attached to the original use regardless.
        if (costs.Any(id => _cardZones.GetLocation(id) == CardLocation.Processing)) return true;
        var held = _resolutionStack.OfType<CardUseFrame>().Where(frame => frame.Id != attack.ResolutionId)
            .SelectMany(frame => frame.Action?.PhysicalCards ?? []).Select(cost => cost.CardId).ToHashSet();
        var expected = attack.PhysicalCards.Where(card => !costs.Contains(card.Id)).Select(card => card.Id).ToHashSet();
        var actual = processing.Where(card => !held.Contains(card.Id)).Select(card => card.Id).ToHashSet();
        valid = actual.SetEquals(expected);
        return true;
    }
}
