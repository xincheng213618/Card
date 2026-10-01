namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void RequestHumanDiscard(CharacterState player)
    {
        var hand = GetDiscardEligibleHand(player);
        var handLimit = GetHandLimit(player);
        var count = hand.Count - handLimit;
        _pendingDecision = new PendingDecision(
            DecisionKind.DiscardCards,
            player.Seat,
            $"手牌上限为 {handLimit}，请选择 {count} 张手牌弃置。",
            hand.Select(card => card.Id).ToArray(),
            [])
        {
            PromptId = CreatePromptId(),
            RequiredCardCount = count
        };
        _status = EngineStatus.AwaitingHumanDiscard;
        AdvanceRulesAndPublishState();
    }

    private CommandResult SubmitDiscardCards(DiscardCardsCommand command)
    {
        var validation = ValidateHumanPrompt(command.ActorSeat, DecisionKind.DiscardCards,
            command.PromptId, CommandErrorCode.IllegalAction);
        if (validation is not null) return Reject(validation.Code, validation.Message);

        var prompt = _pendingDecision!;
        var actor = _players[command.ActorSeat];
        if (_phase != TurnPhase.Discard || _currentSeat != actor.Seat || !actor.IsAlive)
            return Reject(CommandErrorCode.IllegalAction, "The actor is not in their discard phase.");

        var ids = command.CardIds.ToArray();
        if (ids.Length != prompt.RequiredCardCount || ids.Distinct().Count() != ids.Length)
            return Reject(CommandErrorCode.InvalidChoice, $"Select exactly {prompt.RequiredCardCount} distinct cards.");

        var hand = GetDiscardEligibleHand(actor);
        var handIds = hand.Select(card => card.Id).ToHashSet();
        if (ids.Any(id => !prompt.ValidCardIds.Contains(id) || !handIds.Contains(id)))
            return Reject(CommandErrorCode.InvalidCard, "Every selected card must be in the published hand.");

        if (hand.Count - GetHandLimit(actor) != prompt.RequiredCardCount)
            return Reject(CommandErrorCode.IllegalAction, "The hand limit changed; refresh the discard prompt.");

        var selected = ids.ToHashSet();
        var cards = hand.Where(card => selected.Contains(card.Id)).OrderBy(card => card.Id).ToArray();
        return Accept(() =>
        {
            MoveCards(cards, CardLocation.Hand(actor.Seat), CardLocation.DiscardPile,
                CardMoveReasons.HandLimitDiscard);
            ClearPendingDecision();
            AddLog("CardsDiscarded", $"{actor.Name} 弃置 {cards.Length} 张手牌。", actor.Seat);
            AdvanceEventRulesAndQueueFact(new HandLimitDiscardedEvent(actor.Seat,
                Array.AsReadOnly(cards.Select(card => card.Id).ToArray())));
            if (TryBeginDiscardPhaseEndedProgramWindow(actor)) return BuildResult();
            EndTurn();
            return BuildResult();
        });
    }

    private void AssertDiscardPromptInvariant()
    {
        if (_pendingDecision is { Kind: DecisionKind.DiscardCards } prompt)
        {
            var owner = _players[prompt.PlayerSeat];
            var hand = GetDiscardEligibleHand(owner);
            if (!owner.IsHuman || !owner.IsAlive || _phase != TurnPhase.Discard ||
                _currentSeat != owner.Seat || _status != EngineStatus.AwaitingHumanDiscard ||
                prompt.RequiredCardCount <= 0 || prompt.RequiredCardCount != hand.Count - GetHandLimit(owner) ||
                !prompt.ValidCardIds.Order().SequenceEqual(hand.Select(card => card.Id).Order()) ||
                prompt.Choices.Count != 0 || prompt.ValidTargetSeats.Count != 0 || !prompt.IsPrivate)
                throw new InvalidOperationException("The private discard prompt must match its current owner's hand limit.");
        }
        else if (_status == EngineStatus.AwaitingHumanDiscard)
        {
            throw new InvalidOperationException("Discard status requires a private discard prompt.");
        }
    }
}
