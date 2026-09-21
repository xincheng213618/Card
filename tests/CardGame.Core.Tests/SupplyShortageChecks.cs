using CardGame.Content.Standard;
using CardGame.Core;

internal static class SupplyShortageChecks
{
    public static void TargetingAndResolution()
    {
        var standard = StandardContentRegistry.Create();
        var standardIds = standard.Cards.Values
            .Where(card => card.LegacyKind is not null)
            .ToDictionary(card => card.LegacyKind!.Value, card => card.Id);
        var targetingRegistry = ContentRegistry.Build(
            new StandardContentPackage(),
            new SyntheticPackage(
                "supply-shortage-targeting",
                builder => builder.AddDeck(new ContentDeckRecipe(
                    "supply-shortage-targeting:deck",
                    "兵粮寸断空手目标测试牌堆",
                    InitialHandSize: 0,
                    DrawPerTurn: 2,
                    [
                        new ContentDeckCardCount(standardIds[CardKind.SupplyShortage], 2),
                        new ContentDeckCardCount(standardIds[CardKind.Dodge], 20)
                    ]))));
        var targeting = FindTargetingFixture(targetingRegistry, "supply-shortage-targeting:deck");
        var formal = targeting.Game;
        var supplyShortageId = targeting.CardId;
        Require(
            formal.State.Players.Where(player => player.Seat != 0).All(player => player.HandCount == 0),
            "The formal targeting fixture must begin with empty-hand targets.");
        var formalTargets = SupplyShortageTargets(formal, supplyShortageId);
        var expectedFormalTargets = formal.State.Players
            .Where(player => player.IsAlive &&
                player.Seat != 0 &&
                formal.GetCombatDistance(0, player.Seat) == 1)
            .Select(player => player.Seat)
            .OrderBy(seat => seat)
            .ToArray();
        Require(
            formalTargets.SequenceEqual(expectedFormalTargets) &&
            formalTargets.SequenceEqual([1, 4]),
            $"Formal Supply Shortage targets must be the distance-one seats; actual=[{string.Join(',', formalTargets)}].");

        var beforeInvalid = SnapshotJson.Serialize(formal.CreateSnapshot(0, revealAll: true));
        var beforeRevision = formal.Revision;
        var beforeEvents = formal.Events.Count;
        var beforeMovements = formal.CardMovements.Count;
        var invalid = formal.Submit(new PlayCardCommand(
            ActorSeat: 0,
            CardId: supplyShortageId,
            TargetSeats: [2],
            ExpectedRevision: formal.Revision,
            PromptId: formal.PendingDecision!.PromptId));
        Require(!invalid.Accepted, "A forged distance-two Supply Shortage target was accepted.");
        Require(
            formal.Revision == beforeRevision &&
            formal.Events.Count == beforeEvents &&
            formal.CardMovements.Count == beforeMovements &&
            SnapshotJson.Serialize(formal.CreateSnapshot(0, revealAll: true)) == beforeInvalid,
            "Rejecting a forged Supply Shortage target mutated public or trusted state.");

        var resolutionRegistry = ContentRegistry.Build(
            new StandardContentPackage(),
            new SyntheticPackage(
                "supply-shortage-resolution",
                builder => builder.AddDeck(new ContentDeckRecipe(
                    "supply-shortage-resolution:deck",
                    "兵粮寸断空手结算测试牌堆",
                    InitialHandSize: 1,
                    DrawPerTurn: 2,
                    [
                        new ContentDeckCardCount(standardIds[CardKind.SupplyShortage], 2),
                        new ContentDeckCardCount(standardIds[CardKind.Nullification], 2),
                        new ContentDeckCardCount(standardIds[CardKind.Dodge], 20)
                    ]))));
        var countered = FindCounteredFixture(resolutionRegistry);
        var formalResolution = ResolveCounteredSupplyShortage(
            CreateStartedGame(countered.Seed, resolutionRegistry, "supply-shortage-resolution:deck", GameCheckpoint.CurrentRulesVersion),
            countered.TargetSeat);
        Require(
            formalResolution.State.Players.Single(player => player.Seat == countered.TargetSeat).HandCount == 0,
            "The target must spend its final hand card in the Nullification chain.");
        Require(
            formalResolution.Events.Select(item => item.Payload).OfType<DelayedCardPlacedEvent>().Any(item =>
                item.CardKind == CardKind.SupplyShortage && item.TargetSeat == countered.TargetSeat),
            "Formal rules must place Supply Shortage after its target spends the final hand card.");
        Require(
            formalResolution.State.Players.Single(player => player.Seat == countered.TargetSeat).Judgment
                .Any(card => card.Kind == CardKind.SupplyShortage),
            "Formal Supply Shortage did not remain in the target judgment zone.");
        Require(
            formalResolution.Events.Select(item => item.Payload).OfType<CardEffectSkippedEvent>()
                .All(item => item.CardKind != CardKind.SupplyShortage),
            "Formal Supply Shortage incorrectly skipped an empty-hand target.");

        Require(
            formalResolution.ResolutionStack.Count == 0 &&
            formalResolution.State.ProcessingCardCount == 0,
            "Supply Shortage left a stranded resolution frame or processing card.");
        Require(
            formalResolution.CreateCardZoneDiagnostics().Count == 24,
            "Supply Shortage targeting lost a physical card.");

        AssertReplay(formalResolution, resolutionRegistry);
    }

    private static (GameEngine Game, int Seed, int CardId) FindTargetingFixture(
        ContentRegistry registry,
        string deckId)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = CreateGame(seed, registry, deckId, GameCheckpoint.CurrentRulesVersion);
            if (!game.Submit(new StartGameCommand()).Accepted)
                continue;
            var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
                candidate.Kind == LegalActionKind.SupplyShortage);
            if (action?.CardId is { } cardId)
                return (game, seed, cardId);
        }

        throw new InvalidOperationException("Could not find a deterministic Supply Shortage targeting fixture.");
    }

    private static (int Seed, int TargetSeat) FindCounteredFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = CreateStartedGame(
                seed,
                registry,
                "supply-shortage-resolution:deck",
                GameCheckpoint.CurrentRulesVersion);
            var full = game.CreateSnapshot(0, revealAll: true);
            if (!full.Players.Single(player => player.Seat == 0).Hand.Any(card =>
                    card.Kind == CardKind.Nullification))
            {
                continue;
            }

            var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
                candidate.Kind == LegalActionKind.SupplyShortage &&
                candidate.TargetSeat is { } targetSeat &&
                full.Players.Single(player => player.Seat == targetSeat).Hand is
                    [{ Kind: CardKind.Nullification }]);
            if (action?.TargetSeat is not { } target)
                continue;

            var probe = ResolveCounteredSupplyShortage(game, target);
            var responses = probe.Events.Select(item => item.Payload)
                .OfType<NullificationRespondedEvent>()
                .ToArray();
            if (responses.Length == 2 &&
                responses[0].ResponderSeat == target &&
                responses[1].ResponderSeat == 0)
            {
                return (seed, target);
            }
        }

        throw new InvalidOperationException("Could not find a deterministic counter-Nullification Supply Shortage fixture.");
    }

    private static GameEngine ResolveCounteredSupplyShortage(GameEngine game, int targetSeat)
    {
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.SupplyShortage &&
            candidate.TargetSeat == targetSeat);
        Require(game.Submit(new PlayCardCommand(
            ActorSeat: 0,
            CardId: action.CardId!.Value,
            TargetSeats: action.TargetSeats,
            ExpectedRevision: game.Revision,
            PromptId: game.PendingDecision!.PromptId)).Accepted,
            "Supply Shortage play was rejected.");

        for (var step = 0; step < 40 && game.ResolutionStack.Count > 0; step++)
        {
            GameCommand command;
            if (game.PendingDecision is { } prompt)
            {
                Require(prompt.Kind == DecisionKind.Nullification, "Only a Nullification prompt should be pending.");
                var window = game.ResolutionStack.OfType<NullificationWindowFrame>().Single();
                var response = window.EffectNullified ? "nullification" : "pass";
                var choice = prompt.Choices.Single(candidate =>
                    candidate.Parameters.GetValueOrDefault("response") == response);
                command = new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision);
            }
            else
            {
                command = new AdvanceOneStepCommand(game.Revision);
            }

            Require(game.Submit(command).Accepted, "Supply Shortage Nullification continuation failed.");
        }

        return game;
    }

    private static GameEngine CreateStartedGame(
        int seed,
        ContentRegistry registry,
        string deckId,
        int rulesVersion)
    {
        var game = CreateGame(seed, registry, deckId, rulesVersion);
        Require(game.Submit(new StartGameCommand()).Accepted, "Supply Shortage fixture failed to start.");
        return game;
    }

    private static GameEngine CreateGame(
        int seed,
        ContentRegistry registry,
        string deckId,
        int rulesVersion)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                DeckId = deckId,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2
            },
            registry);
        return rulesVersion == GameCheckpoint.CurrentRulesVersion
            ? game
            : GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
    }

    private static int[] SupplyShortageTargets(GameEngine game, int cardId) => game.GetHumanLegalActions()
        .Where(action => action.Kind == LegalActionKind.SupplyShortage && action.CardId == cardId)
        .Select(action => action.TargetSeat ?? -1)
        .OrderBy(seat => seat)
        .ToArray();

    private static void AssertReplay(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(
            SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
            SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            $"Rules v{game.RulesVersion} Supply Shortage checkpoint did not replay exactly.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
