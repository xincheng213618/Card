using CardGame.Content.Standard;
using CardGame.Core;

internal sealed record MengjinBoundary(
    GameEngine Game,
    GameCheckpoint BeforeSlash,
    LegalAction SlashAction,
    int SourceSeat,
    int TargetSeat);

internal static class MengjinScenario
{
    public static MengjinBoundary FindHumanTrigger()
    {
        const int sourceSeat = 0;
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        for (var seed = 1; seed <= 32_768; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = sourceSeat,
                HumanRole = Role.Lord,
                PlayerCount = 5,
                ModeId = "identity:classic-5",
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 220,
                AiPolicyVersion = 2
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted, "Mengjin fixture failed to start.");
            var generalPrompt = game.PendingDecision;
            var pangDe = generalPrompt?.Choices.FirstOrDefault(choice =>
                choice.ContentIds.SequenceEqual(["classic:pang-de"]));
            if (generalPrompt is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: sourceSeat } ||
                pangDe is null)
            {
                continue;
            }

            Require(game.Submit(new SelectGeneralCommand(
                sourceSeat, "classic:pang-de", game.Revision, generalPrompt.PromptId)).Accepted,
                "Mengjin fixture could not select Pang De.");

            for (var step = 0; step < 4_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                var pending = game.PendingDecision;
                if (pending is { Kind: DecisionKind.PlayCard, PlayerSeat: sourceSeat })
                {
                    var full = game.CreateSnapshot(sourceSeat, revealAll: true);
                    var slash = FindSlash(game, full, sourceSeat);
                    if (slash is not null)
                    {
                        var beforeSlash = game.CreateCheckpoint();
                        var played = game.Submit(new PlayCardCommand(
                            sourceSeat,
                            slash.CardId!.Value,
                            slash.TargetSeats,
                            game.Revision,
                            pending.PromptId,
                            slash.PlayedCardKind));
                        Require(played.Accepted, played.Error?.Message ?? "Mengjin fixture could not use Slash.");
                        for (var responseStep = 0;
                             responseStep < 16 && game.State.Status != EngineStatus.Completed;
                             responseStep++)
                        {
                            if (game.PendingDecision is { Kind: DecisionKind.Mengjin, PlayerSeat: sourceSeat })
                            {
                                return new MengjinBoundary(
                                    game, beforeSlash, slash, sourceSeat, slash.TargetSeats.Single());
                            }

                            if (game.PendingDecision is { PlayerSeat: sourceSeat })
                            {
                                break;
                            }

                            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                                "Mengjin fixture could not advance through the Dodge response.");
                        }
                    }

                    if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: sourceSeat } nextPlay)
                    {
                        Require(game.Submit(new EndPlayPhaseCommand(
                            sourceSeat, game.Revision, nextPlay.PromptId)).Accepted,
                            "Mengjin fixture could not end the play phase.");
                    }
                    continue;
                }

                GameCommand command;
                if (pending is null || pending.PlayerSeat != sourceSeat)
                {
                    command = new AdvanceOneStepCommand(game.Revision);
                }
                else if (pending.Kind == DecisionKind.DiscardCards)
                {
                    command = new DiscardCardsCommand(
                        sourceSeat,
                        pending.ValidCardIds.Take(pending.RequiredCardCount).ToArray(),
                        pending.PromptId,
                        game.Revision);
                }
                else
                {
                    command = new AnswerPromptCommand(
                        sourceSeat, pending.PromptId, DeclineChoice(pending).Id, game.Revision);
                }

                var advanced = game.Submit(command);
                if (!advanced.Accepted)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException(
            "No bounded classic Pang De Slash canceled by Dodge was found for Mengjin.");
    }

    private static LegalAction? FindSlash(GameEngine game, GameSnapshot full, int sourceSeat) =>
        game.GetHumanLegalActions()
            .Where(action =>
                action.Kind == LegalActionKind.Slash &&
                action.CardId is not null &&
                action.TargetSeat is not null &&
                action.TargetSeats.Count == 1)
            .Where(action =>
            {
                var target = full.Players.Single(player => player.Seat == action.TargetSeat);
                return target.Hp > 1 &&
                       target.Hand.Count > 1 &&
                       target.Hand.Any(card => card.Kind == CardKind.Dodge) &&
                       target.Equipment.All(card => card.Kind != CardKind.RenwangShield);
            })
            .OrderBy(action => action.CardId)
            .ThenBy(action => action.TargetSeat)
            .FirstOrDefault();

    private static PromptChoice DeclineChoice(PendingDecision prompt) =>
        prompt.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("action") is "decline" or "skip" or "end") ??
        prompt.Choices.Last();

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
