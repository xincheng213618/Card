using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class JijiuChecks
{



    private static (GameEngine Game, PendingDecision Prompt, Card ConvertedCard) FindConvertedPrompt(
        ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = GameEngine.CreateStandard(
                new GameOptions
                {
                    Seed = seed,
                    PlayerCount = 5,
                    ModeId = "identity:active-skills-5",
                    HumanSeat = 0,
                    HumanRole = Role.Lord,
                    UseInteractiveDiscard = false,
                    MaxTurns = 180
                },
                registry);
            var result = game.Submit(new StartGameCommand());
            if (!result.Accepted)
            {
                throw new InvalidOperationException(result.Error?.Message ?? "The Jijiu fixture failed to start.");
            }

            for (var step = 0; result.Status != EngineStatus.Completed && step < 3_000; step++)
            {
                if (result.Status == EngineStatus.AwaitingHumanDying &&
                    game.PendingDecision is { } prompt &&
                    game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Skills?
                        .Any(skill => skill.ContentId == "standard:jijiu") == true)
                {
                    var hand = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hand;
                    var converted = prompt.Choices
                        .Where(choice => choice.Parameters.GetValueOrDefault("response") == "peach")
                        .Select(choice => choice.Cards.SingleOrDefault(cardId =>
                            hand.Any(card => card.Id == cardId &&
                                card.Kind != CardKind.Peach &&
                                card.Suit is Suit.Heart or Suit.Diamond)))
                        .Where(cardId => cardId != 0)
                        .Select(cardId => hand.Single(card => card.Id == cardId))
                        .FirstOrDefault();
                    if (converted is not null)
                    {
                        return (game, prompt, new Card(
                            converted.Id,
                            converted.Kind,
                            converted.Suit,
                            converted.Rank));
                    }
                }

                result = Continue(game, result);
            }
        }

        throw new InvalidOperationException("No deterministic Jijiu dying prompt was found.");
    }

    private static CommandResult Continue(GameEngine game, CommandResult current)
    {
        if (current.Status == EngineStatus.AwaitingHumanPlay)
        {
            var prompt = game.PendingDecision ?? throw new InvalidOperationException("Missing play prompt.");
            return game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId));
        }

        if (current.Status == EngineStatus.AwaitingHumanResponse ||
            current.Status == EngineStatus.AwaitingHumanDying ||
            current.Status == EngineStatus.AwaitingHumanCardSelection)
        {
            var prompt = game.PendingDecision ?? throw new InvalidOperationException("Missing human prompt.");
            var choice = prompt.Choices.FirstOrDefault(item =>
                             item.Parameters.GetValueOrDefault("response") == "take-damage" ||
                             item.Parameters.GetValueOrDefault("response") == "let-die") ??
                         prompt.Choices.FirstOrDefault() ??
                         throw new InvalidOperationException("The human prompt has no choices.");
            return game.Submit(new AnswerPromptCommand(
                prompt.PlayerSeat,
                prompt.PromptId,
                choice.Id,
                game.Revision));
        }

        if (current.Status == EngineStatus.AwaitingHumanDiscard)
        {
            var prompt = game.PendingDecision ?? throw new InvalidOperationException("Missing discard prompt.");
            return game.Submit(new DiscardCardsCommand(
                prompt.PlayerSeat,
                prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                prompt.PromptId,
                game.Revision));
        }

        return game.Submit(new AdvanceOneStepCommand(game.Revision));
    }


    private static string EventSignature(EventEnvelope eventItem) =>
        JsonSerializer.Serialize(new
        {
            eventItem.Id,
            eventItem.ParentId,
            eventItem.Sequence,
            eventItem.Revision,
            eventItem.CorrelationId,
            Payload = JsonSerializer.Serialize(eventItem.Payload, eventItem.Payload.GetType())
        });

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
