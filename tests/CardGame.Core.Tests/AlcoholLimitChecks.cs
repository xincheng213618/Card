using CardGame.Content.Standard;
using CardGame.Core;

internal static class AlcoholLimitChecks
{
    private const string DeckId = "alcohol-limit:deck";

    public static void OncePerTurnAndReplay()
    {
        Require(GameCheckpoint.CurrentRulesVersion >= 20,
            "The formal play-phase Alcohol limit requires rules version 20 or newer.");
        var registry = CreateRegistry();
        var fixture = FindFixture(registry);
        var formal = fixture.Formal;
        var secondAlcoholId = fixture.SecondAlcoholId;

        Require(formal.Log.Any(entry =>
                entry.Type == "Rules" &&
                entry.Message.Contains("出牌阶段每回合限使用一次酒", StringComparison.Ordinal)),
            "The current rules log omitted the formal Alcohol limit.");
        Require(!formal.GetHumanLegalActions().Any(action =>
                action.Kind == LegalActionKind.Alcohol && action.CardId == secondAlcoholId),
            "Current rules exposed a second play-phase Alcohol in the same turn.");

        var before = GameCheckpointJson.Serialize(formal.CreateCheckpoint());
        var forged = formal.Submit(new PlayCardCommand(
            ActorSeat: 0,
            CardId: secondAlcoholId,
            TargetSeats: [],
            ExpectedRevision: formal.Revision,
            PromptId: formal.PendingDecision!.PromptId));
        Require(!forged.Accepted &&
                GameCheckpointJson.Serialize(formal.CreateCheckpoint()) == before,
            "A forged second Alcohol command was accepted or mutated the formal game.");
        formal = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(formal.CreateCheckpoint())),
            registry);
        Require(!formal.GetHumanLegalActions().Any(action =>
                action.Kind == LegalActionKind.Alcohol && action.CardId == secondAlcoholId),
            "The formal once-per-turn Alcohol marker was lost during checkpoint replay.");

        AdvanceToNextHumanTurn(formal, fixture.OpeningTurn);
        Require(formal.GetHumanLegalActions().Any(action =>
                action.Kind == LegalActionKind.Alcohol && action.CardId == secondAlcoholId),
            "The formal Alcohol limit did not reset at the owner's next turn.");
        Require(formal.CreateCardZoneDiagnostics().Count == 40,
            "The Alcohol limit scenario lost a physical card.");
        AssertReplay(formal, registry);
    }

    private static (GameEngine Formal, int SecondAlcoholId, int OpeningTurn) FindFixture(
        ContentRegistry registry)
    {
        for (var seed = 1; seed <= 8_192; seed++)
        {
            var formal = CreateStartedGame(seed, registry, GameCheckpoint.CurrentRulesVersion);
            var full = formal.CreateSnapshot(0, revealAll: true);
            var human = full.Players.Single(player => player.Seat == 0);
            if (human.Skill != SkillKind.None)
                continue;
            var alcoholIds = human.Hand.Where(card => card.Kind == CardKind.Alcohol)
                .Select(card => card.Id)
                .ToArray();
            if (alcoholIds.Length < 2)
                continue;
            var firstAlcohol = formal.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Alcohol && action.CardId == alcoholIds[0]);
            var slash = formal.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash && action.TargetSeat is not null);
            if (firstAlcohol is null || slash is null)
                continue;

            var openingTurn = formal.State.TurnNumber;
            if (!ResolveAlcoholThenSlash(formal, firstAlcohol.CardId!.Value, slash.CardId!.Value, slash.TargetSeat!.Value))
            {
                continue;
            }

            return (formal, alcoholIds[1], openingTurn);
        }

        throw new InvalidOperationException("Could not find a deterministic two-Alcohol opening fixture.");
    }

    private static bool ResolveAlcoholThenSlash(
        GameEngine game,
        int alcoholId,
        int slashId,
        int targetSeat)
    {
        var alcohol = game.GetHumanLegalActions().SingleOrDefault(action =>
            action.Kind == LegalActionKind.Alcohol && action.CardId == alcoholId);
        if (alcohol is null || !Play(game, alcohol).Accepted)
            return false;
        if (!AdvanceToHumanPlay(game, game.State.TurnNumber))
            return false;
        var slash = game.GetHumanLegalActions().SingleOrDefault(action =>
            action.Kind == LegalActionKind.Slash &&
            action.CardId == slashId &&
            action.TargetSeat == targetSeat);
        if (slash is null || !Play(game, slash).Accepted)
            return false;
        return AdvanceToHumanPlay(game, game.State.TurnNumber);
    }

    private static bool AdvanceToHumanPlay(GameEngine game, int turnNumber)
    {
        for (var step = 0; step < 160; step++)
        {
            if (game.State.Status == EngineStatus.AwaitingHumanPlay &&
                game.State.CurrentSeat == 0 &&
                game.State.TurnNumber == turnNumber)
            {
                return true;
            }

            if (game.PendingDecision is not null)
                return false;
            var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
            if (!result.Accepted || result.State.Status == EngineStatus.Completed)
                return false;
        }

        return false;
    }

    private static void AdvanceToNextHumanTurn(GameEngine game, int openingTurn)
    {
        var endPlay = game.Submit(new EndPlayPhaseCommand(
            0,
            game.Revision,
            game.PendingDecision!.PromptId));
        Require(endPlay.Accepted, "Ending the formal opening play phase was rejected.");

        for (var step = 0; step < 2_000; step++)
        {
            if (game.State.Status == EngineStatus.AwaitingHumanPlay &&
                game.State.CurrentSeat == 0 &&
                game.State.TurnNumber > openingTurn)
            {
                return;
            }

            GameCommand command;
            if (game.PendingDecision is { } prompt)
            {
                Require(prompt.PlayerSeat == 0,
                    "Only the human seat may expose a prompt while advancing the Alcohol fixture.");
                var choice = ChooseSafeContinuation(prompt);
                command = new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision);
            }
            else
            {
                command = new AdvanceOneStepCommand(game.Revision);
            }

            var result = game.Submit(command);
            Require(result.Accepted, "The Alcohol next-turn continuation was rejected.");
            Require(result.State.Status != EngineStatus.Completed,
                "The Alcohol fixture ended before the human's next turn.");
        }

        throw new InvalidOperationException("The Alcohol fixture did not reach the human's next turn.");
    }

    private static PromptChoice ChooseSafeContinuation(PendingDecision prompt)
    {
        static string? Response(PromptChoice choice) =>
            choice.Parameters.GetValueOrDefault("response");

        return prompt.Kind switch
        {
            DecisionKind.RespondDodge => prompt.Choices.FirstOrDefault(choice => Response(choice) == "dodge") ??
                prompt.Choices.Single(choice => Response(choice) == "take-damage"),
            DecisionKind.RespondSlash => prompt.Choices.FirstOrDefault(choice => Response(choice) == "slash") ??
                prompt.Choices.Single(choice => Response(choice) == "take-damage"),
            DecisionKind.Nullification => prompt.Choices.Single(choice => Response(choice) == "pass"),
            DecisionKind.DiscardCards => prompt.Choices.First(),
            _ => prompt.Choices.FirstOrDefault(choice =>
                    Response(choice)?.Contains("skip", StringComparison.Ordinal) == true) ??
                throw new InvalidOperationException($"Unexpected Alcohol fixture prompt {prompt.Kind}.")
        };
    }

    private static ContentRegistry CreateRegistry()
    {
        var standard = StandardContentRegistry.Create();
        var ids = standard.Cards.Values
            .Where(card => card.LegacyKind is not null)
            .ToDictionary(card => card.LegacyKind!.Value, card => card.Id);
        return ContentRegistry.Build(
            new StandardContentPackage(),
            new SyntheticPackage(
                "alcohol-limit",
                builder => builder.AddDeck(new ContentDeckRecipe(
                    DeckId,
                    "酒每回合限次测试牌堆",
                    InitialHandSize: 4,
                    DrawPerTurn: 2,
                    [
                        new ContentDeckCardCount(ids[CardKind.Alcohol], 8),
                        new ContentDeckCardCount(ids[CardKind.Slash], 8),
                        new ContentDeckCardCount(ids[CardKind.Dodge], 24)
                    ]))));
    }

    private static GameEngine CreateStartedGame(
        int seed,
        ContentRegistry registry,
        int rulesVersion)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                DeckId = DeckId,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2
            },
            registry);
        if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
            game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "The Alcohol fixture failed to start.");
        return game;
    }

    private static CommandResult Play(GameEngine game, LegalAction action) => game.Submit(new PlayCardCommand(
        ActorSeat: 0,
        CardId: action.CardId!.Value,
        TargetSeats: action.TargetSeats,
        ExpectedRevision: game.Revision,
        PromptId: game.PendingDecision!.PromptId));

    private static void AssertReplay(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(
            SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
            SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            $"Rules v{game.RulesVersion} Alcohol checkpoint did not replay exactly.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
