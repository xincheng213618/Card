namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CommandResult SubmitRecast(RecastCardCommand command)
    {
        var validation = ValidateHumanPrompt(command.ActorSeat, DecisionKind.PlayCard,
            command.PromptId, CommandErrorCode.IllegalAction);
        if (validation is not null) return Reject(validation.Code, validation.Message);
        var actor = _players[command.ActorSeat];
        if (!GetHand(actor).Any(card => card.Id == command.CardId))
            return Reject(CommandErrorCode.InvalidCard, "The recast card is not in the actor's hand.");
        var action = BuildLegalActions(actor).SingleOrDefault(action => action.Kind == LegalActionKind.Recast && action.CardId == command.CardId);
        if (action is null) return Reject(CommandErrorCode.IllegalAction, "This card cannot be recast under the current rules.");
        return Accept(() =>
        {
            ClearPendingDecision();
            ExecuteAction(actor, action);
            PublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
    }

    private void ResolveRecast(PlayerRuntime actor, Card card)
    {
        if (_rulesVersion < 6 || card.Kind != CardKind.IronChain)
            throw new InvalidOperationException("Only Iron Chain may be recast under rules version 6.");
        MoveCards([card], CardLocation.Hand(actor.Seat), CardLocation.DiscardPile, CardMoveReasons.RecastDiscard);
        var drawn = DrawCards(actor, 1, log: false, reason: CardMoveReasons.RecastDraw);
        if (_aiBrains.TryGetValue(actor.Seat, out var brain)) brain.ObserveRecast(_turnNumber, card.Id);
        QueueGameEvent(new CardRecastEvent(actor.Seat, card.Id, card.Kind, drawn.Count));
        AddLog("Recast", $"{actor.Name} 重铸【{CardCatalog.Get(card.Kind).DisplayName}】，摸 {drawn.Count} 张牌。", actor.Seat);
    }
}
