namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CommandResult SubmitGeneralRoster(RequestGeneralRosterCommand command)
    {
        var validation = ValidateHumanPrompt(command.ActorSeat, DecisionKind.SelectGeneral,
            command.PromptId, CommandErrorCode.InvalidGeneral);
        if (validation is not null) return Reject(validation.Code, validation.Message);
        return Accept(() =>
        {
            var player = _players[command.ActorSeat];
            var offers = GetAvailableGeneralOffers(player, command.FullRoster);
            var candidates = offers.Select(offer => offer.General).ToArray();
            AdvanceEventRulesAndQueueFact(new GeneralSelectionRequestedEvent(player.Seat,
                candidates.Select(general => general.Id).ToArray()));
            RequestHumanGeneralSelection(player, candidates,
                offers.ToDictionary(offer => offer.General.Id, offer => offer.CandidateId), command.FullRoster);
        });
    }

    private void RequestOpeningHandConfirmation()
    {
        var hand = GetHand(_players[_options.HumanSeat]);
        // Publish kinds and the human's own payment card only. The source entity
        // and its hidden owner remain trusted setup details.
        var kinds = OpeningReplacementCards().Select(card => card.Kind).Distinct().Order().ToArray();
        var choices = new[] { "reroll", "confirm" }.Select(action => new PromptChoice(
            new ChoiceId($"setup.opening.{action}"), action == "reroll" ? "随机" : "确定", [], [],
            new Dictionary<string, string> { ["action"] = action })).ToList();
        foreach (var card in hand)
        foreach (var kind in kinds.Where(kind => kind != card.Kind))
            choices.Add(new(new($"setup.opening.replace.{card.Id}.{kind}"), CardCatalog.Get(kind).DisplayName,
                [card.Id], [], new Dictionary<string, string> { ["action"] = "replace", ["kind"] = kind.ToString() }));
        _pendingDecision = new PendingDecision(DecisionKind.OpeningHand, _options.HumanSeat,
            "在手牌区查看起手牌，可随机或选中一张牌换牌，点击确定进入牌局。", [], [])
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            Choices = choices.ToArray()
        };
        _status = EngineStatus.AwaitingHumanOpeningHand;
    }

    private IEnumerable<Card> OpeningReplacementCards() => _cardZones.CardsAt(CardLocation.DrawPile)
        .Concat(_players.Where(player => player.Seat != _options.HumanSeat).SelectMany(GetHand));

    private void ReplaceOpeningCard(PromptChoice selected)
    {
        var hand = CardLocation.Hand(_options.HumanSeat);
        var outgoing = _cardZones.CardsAt(hand).Single(card => card.Id == selected.Cards.Single());
        var kind = Enum.Parse<CardKind>(selected.Parameters["kind"]);
        var incoming = OpeningReplacementCards().First(card => card.Kind == kind);
        var source = _cardZones.GetLocation(incoming.Id);
        var reason = new CardMoveReason("setup.opening-replace");
        var batch = BeginCardMovementBatch([hand, source], [source, hand]);
        var movements = new List<CardMovementRecord>(2);
        var committed = false;
        try
        {
            _cardZones.MoveBatch([new(outgoing.Id, hand, source), new(incoming.Id, source, hand)]);
            movements.Add(RecordMovement(outgoing, hand, source, reason));
            movements.Add(RecordMovement(incoming, source, hand, reason));
            committed = true;
        }
        finally { CompleteCardMovementBatch(batch, movements, committed); }
    }

    private void ResolveOpeningHandChoice(PromptChoice selected)
    {
        if (_setupComplete || _turnNumber != 0 || !_options.ConfirmOpeningHand ||
            _pendingDecision?.Kind != DecisionKind.OpeningHand || _resolutionStack.Count != 0)
            throw new InvalidOperationException("Opening hand changes require their actual pre-game prompt.");
        if (selected.Parameters["action"] == "replace")
        {
            ReplaceOpeningCard(selected);
            RequestOpeningHandConfirmation();
            AdvanceRulesAndPublishState();
            return;
        }
        if (selected.Parameters["action"] == "reroll")
        {
            var player = _players[_options.HumanSeat];
            var cards = GetHand(player).ToArray();
            MoveCards(cards, CardLocation.Hand(player.Seat), CardLocation.DrawPile, new("setup.opening-reroll"));
            _cardZones.Shuffle(CardLocation.DrawPile, _random);
            DrawCards(player, cards.Length, log: false, CardMoveReasons.InitialDeal);
            RequestOpeningHandConfirmation();
            AdvanceRulesAndPublishState();
            return;
        }
        ClearPendingDecision();
        FinalizeSetup();
        if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary();
    }
}
