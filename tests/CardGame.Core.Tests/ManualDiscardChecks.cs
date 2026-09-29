using CardGame.Content.Standard;
using CardGame.Core;

internal static class ManualDiscardChecks
{
    private static readonly ContentRegistry Registry = StandardContentRegistry.Create();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static GameEngine AtDiscard(int seed = 87381, ContentRegistry? registry = null, string? deckId = null)
    {
        var game = AtPlay(seed, registry, deckId);
        Require(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)).Accepted, "End-play rejected.");
        Require(game.PendingDecision?.Kind == DecisionKind.DiscardCards, "Expected a default manual discard boundary.");
        return game;
    }

    private static GameEngine AtPlay(int seed, ContentRegistry? registry = null, string? deckId = null)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, PlayerCount = 5, DeckId = deckId, UseInteractiveSetup = true }, registry ?? Registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Start rejected.");
        var choice = game.PendingDecision!.Choices.First();
        Require(game.Submit(new SelectGeneralCommand(0, choice.ContentIds[0], game.Revision, game.PendingDecision.PromptId)).Accepted, "General rejected.");
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Expected the human play boundary.");
        return game;
    }

    private sealed class HandSizePackage(int initialHandSize) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("test-hand-limit", new Version(1, 0));

        public void Register(IContentRegistryBuilder builder) => builder.AddDeck(
            Registry.GetDeck("standard:basic-demo") with
            {
                Id = "test:hand-limit",
                InitialHandSize = initialHandSize
            });
    }

    public static void HandSizeBoundaries()
    {
        var largeRegistry = ContentRegistry.Build(new StandardContentPackage(), new HandSizePackage(16));
        var large = AtDiscard(registry: largeRegistry, deckId: "test:hand-limit");
        var prompt = large.PendingDecision!;
        Require(prompt.RequiredCardCount >= 10 && prompt.Choices.Count == 0, "Large hand should use a linear subset prompt.");
        var chosen = prompt.ValidCardIds.OrderBy(id => id % 3).ThenByDescending(id => id).Take(prompt.RequiredCardCount).ToArray();
        var kept = large.State.Players[0].Hand.Select(card => card.Id).Except(chosen).Order().ToArray();
        var command = new DiscardCardsCommand(0, chosen, prompt.PromptId, large.Revision);
        Require(large.Submit(command).Accepted, "Large exact subset was rejected.");
        Require(large.State.Players[0].Hand.Select(card => card.Id).Order().SequenceEqual(kept), "Large discard changed retained cards.");
        Require(large.State.Players[0].HandCount == large.State.Players[0].Hp, "Large discard did not reach the hand limit.");

        var smallRegistry = ContentRegistry.Build(new StandardContentPackage(), new HandSizePackage(1));
        var small = AtPlay(87381, smallRegistry, "test:hand-limit");
        var human = small.State.Players[0];
        Require(human.HandCount <= human.Hp, "Small-hand fixture unexpectedly exceeds its limit.");
        Require(small.Submit(new EndPlayPhaseCommand(0, small.Revision, small.PendingDecision!.PromptId)).Accepted, "Small-hand end-play was rejected.");
        Require(small.PendingDecision?.Kind != DecisionKind.DiscardCards &&
            small.Events.Any(item => item.Payload is TurnEndedEvent { ActorSeat: 0 }),
            "An in-limit hand should finish without a discard decision.");
        Require(!small.CardMovements.Any(move => move.From == CardLocation.Hand(0) && move.Reason == CardMoveReasons.HandLimitDiscard),
            "An in-limit hand should not lose cards to the hand limit.");
    }

    public static void Rejections()
    {
        var game = AtDiscard();
        var prompt = game.PendingDecision!;
        var valid = prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray();
        var cards = game.CreateCardZoneDiagnostics();
        var foreign = cards.First(card => card.Location == CardLocation.Hand(1)).CardId;
        var invalid = valid.ToArray();
        invalid[0] = foreign;
        var duplicate = Enumerable.Repeat(valid[0], Math.Max(2, prompt.RequiredCardCount)).ToArray();
        var attempts = new DiscardCardsCommand[]
        {
            new(0, valid, prompt.PromptId, game.Revision - 1),
            new(1, valid, prompt.PromptId, game.Revision),
            new(0, valid, new PromptId(prompt.PromptId.Value + 999), game.Revision),
            new(0, [], prompt.PromptId, game.Revision),
            new(0, prompt.ValidCardIds.ToArray(), prompt.PromptId, game.Revision),
            new(0, duplicate, prompt.PromptId, game.Revision),
            new(0, invalid, prompt.PromptId, game.Revision),
            new(0, null!, prompt.PromptId, game.Revision)
        };
        var snapshot = SnapshotJson.Serialize(game.CreateSnapshot(0, true));
        var revision = game.Revision;
        var moves = game.CardMovements.Count;
        var events = game.Events.Count;
        var notifications = 0;
        game.StateChanged += _ => notifications++;
        foreach (var attempt in attempts)
        {
            var result = game.Submit(attempt);
            Require(!result.Accepted && result.Error is not null, "Invalid subset was accepted.");
            Require(game.Revision == revision && game.CardMovements.Count == moves && game.Events.Count == events && notifications == 0, "Rejection mutated committed state.");
            Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == snapshot, "Rejection changed the game snapshot.");
        }
        Require(game.Submit(new AdvanceCommand(game.Revision)).PendingDecision?.Kind == DecisionKind.DiscardCards, "Host advance skipped the human decision.");
    }

    public static void ExactSelection()
    {
        var game = AtDiscard();
        var prompt = game.PendingDecision!;
        var human = game.State.Players[0];
        Require(prompt.RequiredCardCount == human.HandCount - human.Hp, "Incorrect discard count.");
        Require(prompt.Choices.Count == 0 && prompt.ValidCardIds.Count == human.HandCount, "Prompt should carry a linear-size candidate set.");
        Require(game.CreateSnapshot(1).PendingDecision is null && game.CreateSnapshot(-1).PendingDecision is null, "Private discard selection leaked.");
        var selected = prompt.ValidCardIds.TakeLast(prompt.RequiredCardCount).Reverse().ToArray();
        var kept = human.Hand.Select(card => card.Id).Except(selected).ToArray();
        var expected = selected.Order().ToArray();
        var moves = game.CardMovements.Count;
        var result = game.Submit(new DiscardCardsCommand(0, selected, prompt.PromptId, game.Revision));
        Require(result.Accepted && game.PendingDecision is null && game.State.Status == EngineStatus.Running, "Discard did not yield to the next turn.");
        Require(game.State.Players[0].Hand.Select(card => card.Id).Order().SequenceEqual(kept.Order()), "Engine discarded different cards.");
        var locations = game.CreateCardZoneDiagnostics();
        Require(expected.All(id => locations.Single(card => card.CardId == id).Location == CardLocation.DiscardPile), "Selected cards did not reach discard pile.");
        Require(game.CardMovements.Skip(moves).Select(move => move.CardId).SequenceEqual(expected), "Subset order should not change pile order.");
        Require(locations.Select(card => card.CardId).Distinct().Count() == locations.Count, "A card appeared in multiple zones.");
        var committed = game.Events.Select(item => item.Payload).OfType<HandLimitDiscardedEvent>().Last();
        Require(committed.CardIds.SequenceEqual(expected), "Committed event differs from selection.");
        selected[0] = -100;
        Require(game.AcceptedCommands.OfType<DiscardCardsCommand>().Last().CardIds.All(id => id > 0), "Command journal retained the caller's mutable array.");
        Require(game.ObserverFailures.Count == 0, "Discard observer failed.");
    }

    public static void Replay()
    {
        var game = AtDiscard();
        var checkpoint = game.CreateCheckpoint();
        Require(checkpoint.SchemaVersion == 3, "Checkpoint must reject pre-discard rule semantics.");
        var legacyRejected = false;
        try { GameReplay.Restore(checkpoint with { SchemaVersion = 2 }, Registry); }
        catch (InvalidOperationException exception) when (exception.Message.Contains("schema 2")) { legacyRejected = true; }
        Require(legacyRejected, "An earlier checkpoint schema should be rejected explicitly.");
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint)), Registry);
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(restored.CreateSnapshot(0, true)), "Private discard checkpoint differs.");
        var prompt = game.PendingDecision!;
        var command = new DiscardCardsCommand(0, prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(), prompt.PromptId, game.Revision);
        var roundTripped = (DiscardCardsCommand)CommandJson.Deserialize(CommandJson.Serialize([command])).Single();
        Require(game.Submit(roundTripped).Accepted && restored.Submit(roundTripped).Accepted, "Restored discard could not be submitted.");
        var replay = GameReplay.Replay(checkpoint.Options, CommandJson.Deserialize(CommandJson.Serialize(game.AcceptedCommands)), Registry);
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(replay.CreateSnapshot(0, true)), "Journal did not reproduce post-discard state.");
        Require(SnapshotJson.Serialize(game.State) == SnapshotJson.Serialize(restored.State), "Restored continuation diverged.");
        Require(!game.Submit(command).Accepted, "A stale discard was accepted twice.");
    }

    public static void MatchMatrix()
    {
        var discarded = 0;
        foreach (var count in new[] { 5, 8 })
            foreach (var role in new[] { Role.Lord, Role.Loyalist, Role.Rebel, Role.Renegade })
            {
                var options = new GameOptions { Seed = 19005 + count + (int)role, PlayerCount = count, HumanSeat = count - 1, HumanRole = role, UseInteractiveSetup = true, MaxTurns = 70 };
                var game = GameEngine.CreateStandard(options, Registry);
                Require(game.Submit(new StartGameCommand()).Accepted, "Matrix setup failed.");
                for (var step = 0; step < 5000 && game.State.Status != EngineStatus.Completed; step++)
                {
                    GameCommand command;
                    var prompt = game.PendingDecision;
                    if (prompt is null) command = new AdvanceCommand(game.Revision);
                    else if (prompt.Kind == DecisionKind.SelectGeneral) command = new SelectGeneralCommand(prompt.PlayerSeat, prompt.ValidContentIds[0], game.Revision, prompt.PromptId);
                    else if (prompt.Kind == DecisionKind.PlayCard) command = new EndPlayPhaseCommand(prompt.PlayerSeat, game.Revision, prompt.PromptId);
                    else if (prompt.Kind == DecisionKind.DiscardCards)
                    {
                        command = new DiscardCardsCommand(prompt.PlayerSeat, prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(), prompt.PromptId, game.Revision);
                        discarded++;
                    }
                    else
                    {
                        var choice = prompt.Choices.FirstOrDefault(option => option.Cards.Count == 0) ?? prompt.Choices.First();
                        command = new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision);
                    }
                    var result = game.Submit(command);
                    Require(result.Accepted, $"Matrix {count}/{role}: {result.Error?.Message}");
                }
                Require(game.State.Status == EngineStatus.Completed && game.PendingDecision is null && game.State.ProcessingCardCount == 0, $"Matrix {count}/{role} stalled.");
                Require(game.ObserverFailures.Count == 0, "Matrix observer failure.");
            }
        Require(discarded >= 8, "Matrix did not exercise enough manual discards.");
    }
}
