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


    public static void PartialPaymentsRestoreRemainingQuota()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new HandSizePackage(6));
        var game = AtDiscard(registry: registry, deckId: "test:hand-limit");
        var initial = game.PendingDecision!;
        var count = initial.RequiredCardCount;
        Require(count > 1, "The fixed discard fixture must require multiple cards.");
        var first = new DiscardCardsCommand(0, [initial.ValidCardIds[0]], initial.PromptId, game.Revision);
        Require(game.Submit(first).Accepted, "One-card discard payment was rejected.");
        Require(game.PendingDecision is { Kind: DecisionKind.DiscardCards } next && next.RequiredCardCount == count - 1 &&
            next.PromptId != initial.PromptId && !next.ValidCardIds.Contains(first.CardIds[0]) &&
            game.State.CurrentSeat == 0 && game.State.Phase == TurnPhase.Discard,
            "A partial payment must move its card once and publish only the remaining obligation in the same phase.");
        Require(!game.Submit(first with { ExpectedRevision = game.Revision }).Accepted, "The old discard prompt was reused.");
        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(restored.CreateSnapshot(0, true)),
            "Cold recovery changed the partially paid hand or its remaining quota.");
        var prompt = game.PendingDecision!;
        var last = new DiscardCardsCommand(0, prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(), prompt.PromptId, game.Revision);
        Require(game.Submit(last).Accepted && restored.Submit(last).Accepted, "Remaining discard payment was rejected.");
        Require(game.PendingDecision?.Kind != DecisionKind.DiscardCards && !game.ResolutionStack.OfType<ManualDiscardFrame>().Any(),
            "Paying the remaining quota must retire its frame and automatically leave the discard decision.");
        Require(game.CardMovements.Count(move => move.From == CardLocation.Hand(0) &&
            move.Reason == CardMoveReasons.HandLimitDiscard) == count, "Partial payments lost or repeated physical cards.");
        var replayed = GameReplay.Replay(checkpoint.Options, game.AcceptedCommands, registry);
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(restored.CreateSnapshot(0, true)) &&
            SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(replayed.CreateSnapshot(0, true)),
            "Partial discard cold continuation or command replay diverged.");
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

}
