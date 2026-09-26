using CardGame.Content.Standard;
using CardGame.Core;

internal static class BorrowedSwordScenario
{
    public static GameEngine FindHumanSourcePlay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        for (var seed = 1; seed <= 4_096; seed++)
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
            Require(started.Accepted, "Borrowed Sword source fixture failed to start.");
            var firstGeneral = game.PendingDecision?.Choices.FirstOrDefault();
            if (firstGeneral?.ContentIds.Count != 1)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                firstGeneral.ContentIds[0],
                game.Revision,
                game.PendingDecision!.PromptId));
            Require(selected.Accepted, "Borrowed Sword source fixture could not select a general.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, "Borrowed Sword source fixture did not reach play.");
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } play ||
                game.CreateSnapshot(0).Players[0].Hand.All(card => card.Kind != CardKind.BorrowedSword))
            {
                continue;
            }

            var ended = game.Submit(new EndPlayPhaseCommand(0, game.Revision, play.PromptId));
            Require(ended.Accepted, "Borrowed Sword source fixture could not preserve the trick for another round.");
            for (var step = 0; step < 1_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
                    game.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.BorrowedSword))
                {
                    return game;
                }

                var human = game.CreateSnapshot(0, revealAll: true).Players[0];
                if (!human.IsAlive || human.Hand.All(card => card.Kind != CardKind.BorrowedSword))
                {
                    break;
                }

                Step(game);
            }
        }

        throw new InvalidOperationException(
            "No bounded classic Borrowed Sword source fixture with an equipped target was found.");
    }

    public static GameEngine FindHumanOwnerResponse(bool requireJijiang = false)
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
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
            Require(started.Accepted, "Borrowed Sword fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:liu-bei"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:liu-bei",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, "Borrowed Sword fixture could not select Liu Bei.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, "Borrowed Sword fixture did not reach Liu Bei's play phase.");
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } play)
            {
                continue;
            }

            var owner = game.CreateSnapshot(0, revealAll: true).Players[0];
            var weapon = owner.Hand.FirstOrDefault(card =>
                card.Kind is CardKind.Crossbow or CardKind.QinggangSword);
            if (weapon is null)
            {
                continue;
            }

            var equipped = game.Submit(new PlayCardCommand(
                0,
                weapon.Id,
                [],
                game.Revision,
                play.PromptId));
            Require(equipped.Accepted, "Borrowed Sword fixture could not equip Liu Bei's weapon.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                var resumed = game.Submit(new AdvanceCommand(game.Revision));
                Require(resumed.Accepted, "Borrowed Sword fixture did not return to play.");
            }

            var nextPlay = game.PendingDecision ??
                throw new InvalidOperationException("Borrowed Sword fixture lost its play prompt.");
            var ended = game.Submit(new EndPlayPhaseCommand(0, game.Revision, nextPlay.PromptId));
            Require(ended.Accepted, "Borrowed Sword fixture could not end Liu Bei's play phase.");

            for (var step = 0; step < 4_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is
                    {
                        Kind: DecisionKind.RespondSlash,
                        PlayerSeat: 0,
                        IncomingCard: CardKind.BorrowedSword
                    } response &&
                    response.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "borrowed-sword-give-weapon") &&
                    response.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "borrowed-sword-slash") &&
                    (!requireJijiang || response.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "jijiang-request") &&
                        HasHelpingShuProvider(game, registry)))
                {
                    var slashTargetSeat = response.TargetSeat ??
                        throw new InvalidOperationException("Borrowed Sword prompt omitted its Slash target.");
                    var slashTarget = game.CreateSnapshot(0, revealAll: true).Players
                        .Single(player => player.Seat == slashTargetSeat);
                    var canPauseForDodge = slashTarget.Hand.Any(card =>
                                                card.Kind == CardKind.Dodge ||
                                                slashTarget.Skills?.Any(skill =>
                                                    skill.Kind == SkillKind.Longdan &&
                                                    card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) == true ||
                                                slashTarget.Skills?.Any(skill =>
                                                    skill.Kind == SkillKind.Qingguo &&
                                                    card.Suit is Suit.Spade or Suit.Club) == true) ||
                                            slashTarget.Equipment.Any(card =>
                                                card.Kind == CardKind.BaguaFormation);
                    if (canPauseForDodge)
                    {
                        return game;
                    }
                }

                var currentOwner = game.CreateSnapshot(0, revealAll: true).Players[0];
                if (!currentOwner.IsAlive || currentOwner.Equipment.All(card => card.Id != weapon.Id))
                {
                    break;
                }

                Step(game);
            }
        }

        throw new InvalidOperationException(
            "No bounded classic Borrowed Sword response fixture with a real Slash and Dodge continuation was found.");
    }

    public static void Step(GameEngine game)
    {
        GameCommand command;
        if (game.PendingDecision is { PlayerSeat: 0 } prompt)
        {
            if (prompt.Kind == DecisionKind.PlayCard)
            {
                command = new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId);
            }
            else if (prompt.Kind == DecisionKind.DiscardCards)
            {
                command = new DiscardCardsCommand(
                    0,
                    prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                    prompt.PromptId,
                    game.Revision);
            }
            else
            {
                var choice = prompt.Choices.FirstOrDefault(candidate =>
                                 candidate.Parameters.GetValueOrDefault("response") is
                                     "dodge" or "slash" or "peach" or "bagua")
                             ?? prompt.Choices.FirstOrDefault(candidate =>
                                 candidate.Parameters.Values.Any(value =>
                                     value.StartsWith("skip", StringComparison.Ordinal) ||
                                     value is "take-damage" or "pass" or "no-nullification"))
                             ?? prompt.Choices.Last();
                command = new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision);
            }
        }
        else
        {
            command = new AdvanceOneStepCommand(game.Revision);
        }

        var result = game.Submit(command);
        Require(result.Accepted, $"Borrowed Sword fixture could not continue: {result.Error?.Message}");
    }

    private static bool HasHelpingShuProvider(GameEngine game, ContentRegistry registry) =>
        game.CreateSnapshot(0, revealAll: true).Players.Any(player =>
            player.Seat != 0 &&
            player.Role is Role.Loyalist or Role.Renegade &&
            string.Equals(registry.Generals[player.GeneralId].FactionId, "shu", StringComparison.Ordinal) &&
            player.Hand.Any(card => card.Kind is
                CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash));

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
