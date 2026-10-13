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
        _pendingDecision = new PendingDecision(DecisionKind.OpeningHand, _options.HumanSeat,
            "确认起手牌后进入牌局；也可以将这些牌放回牌堆，重新随机同样数量的手牌。", [], [])
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            Choices = new[] { "reroll", "confirm" }.Select(action => new PromptChoice(
                new ChoiceId($"setup.opening.{action}"), action == "reroll" ? "重新随机手牌" : "进入牌局", [], [],
                new Dictionary<string, string> { ["action"] = action })).ToArray()
        };
        _status = EngineStatus.AwaitingHumanOpeningHand;
    }

    private void ResolveOpeningHandChoice(PromptChoice selected)
    {
        if (_setupComplete || _turnNumber != 0 || !_options.ConfirmOpeningHand ||
            _pendingDecision?.Kind != DecisionKind.OpeningHand || _resolutionStack.Count != 0)
            throw new InvalidOperationException("Opening hand changes require their actual pre-game prompt.");
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
