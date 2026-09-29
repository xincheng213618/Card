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
