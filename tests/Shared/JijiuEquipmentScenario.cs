using CardGame.Content.Standard;
using CardGame.Core;

internal static class JijiuEquipmentScenario
{
    public static GameEngine Find(Version? classicPackageVersion = null)
    {
        var registry = classicPackageVersion is null
            ? StandardContentRegistry.CreateWithClassicGenerals()
            : StandardContentRegistry.CreateWithClassicGenerals(classicPackageVersion);
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                PlayerCount = 5,
                ModeId = "identity:classic-5",
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 220,
                AiPolicyVersion = 2
            }, registry);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, "Classic Jijiu equipment fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:hua-tuo"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:hua-tuo",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, "Classic Jijiu equipment fixture could not select Hua Tuo.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, "Classic Jijiu equipment fixture did not reach play.");
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } play)
            {
                continue;
            }

            var redEquipment = game.CreateSnapshot(0, revealAll: true).Players[0].Hand.FirstOrDefault(card =>
                EquipmentCatalog.IsEquipment(card.Kind) &&
                card.Suit is Suit.Heart or Suit.Diamond);
            if (redEquipment is null)
            {
                continue;
            }

            var equipped = game.Submit(new PlayCardCommand(
                0,
                redEquipment.Id,
                [],
                game.Revision,
                play.PromptId));
            Require(equipped.Accepted, "Classic Jijiu equipment fixture could not equip the red card.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                var resumed = game.Submit(new AdvanceCommand(game.Revision));
                Require(resumed.Accepted, "Classic Jijiu equipment fixture did not return to play.");
            }

            var nextPlay = game.PendingDecision ??
                throw new InvalidOperationException("Classic Jijiu equipment fixture lost its play prompt.");
            var ended = game.Submit(new EndPlayPhaseCommand(0, game.Revision, nextPlay.PromptId));
            Require(ended.Accepted, "Classic Jijiu equipment fixture could not end play.");

            for (var step = 0; step < 4_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.Events.Any(item => item.Payload is StoneAxeResolvedEvent))
                {
                    break;
                }

                if (game.PendingDecision is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 } response &&
                    response.Choices.Any(choice =>
                        choice.Cards.SequenceEqual([redEquipment.Id]) &&
                        choice.Parameters.GetValueOrDefault("response") == "peach"))
                {
                    try
                    {
                        _ = GameReplay.Restore(game.CreateCheckpoint(), registry);
                        return game;
                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }
                }

                var owner = game.CreateSnapshot(0, revealAll: true).Players[0];
                if (!owner.IsAlive || owner.Equipment.All(card => card.Id != redEquipment.Id))
                {
                    break;
                }

                Step(game);
            }
        }

        throw new InvalidOperationException("No bounded classic Jijiu equipment rescue fixture was found.");
    }

    public static void Step(GameEngine game)
    {
        GameCommand command = game.PendingDecision is { PlayerSeat: 0 } prompt
            ? prompt.Kind == DecisionKind.PlayCard
                ? new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)
                : new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.Last().Id, game.Revision)
            : new AdvanceOneStepCommand(game.Revision);
        var result = game.Submit(command);
        Require(result.Accepted, $"Jijiu equipment fixture could not continue: {result.Error?.Message}");
    }

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
