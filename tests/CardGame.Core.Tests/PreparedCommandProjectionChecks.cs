using CardGame.Content.Standard;
using CardGame.Core;

internal static class PreparedCommandProjectionChecks
{
    public static void PublishedResultAndReentrantRejectionSharePreparedView()
    {
        var registry = StandardContentRegistry.Create();
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 337, HumanSeat = 0, HumanRole = Role.Lord,
            AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 100
        }, registry);
        var notifications = new List<string>();
        GameSnapshot? published = null;
        CommandResult? reentry = null;
        game.EventCommitted += item =>
        {
            Require(game.Revision == 1 && game.AcceptedCommands.Count == 1,
                "Typed-event observers must see the committed revision and command.");
            notifications.Add("event");
        };
        game.StateChanged += snapshot =>
        {
            notifications.Add("state");
            published = snapshot;
            reentry = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(reentry is { Accepted: false, Error.Code: CommandErrorCode.ReentrantOperation } &&
                    ReferenceEquals(reentry.Result.State, snapshot),
                "Reentry must reject against the prepared committed view without projecting another result.");
        };

        var result = game.Submit(new StartGameCommand());
        var committedSnapshot = published ??
            throw new InvalidOperationException("The command did not publish its prepared view.");
        Require(result.Accepted &&
                ReferenceEquals(result.Result.State, committedSnapshot) &&
                ReferenceEquals(result.Result.PendingDecision, committedSnapshot.PendingDecision),
            "The public result and state observer must share the one prepared projection.");
        Require(notifications.Count(item => item == "state") == 1 &&
                notifications.Count(item => item == "event") > 0 &&
                notifications[^1] == "state" && game.ObserverFailures.Count == 0,
            "The command must deliver committed events before one final state publication.");
        Require(game.Revision == 1 && game.AcceptedCommands.Count == 1 &&
                result.Result.Revision == committedSnapshot.Revision &&
                SnapshotJson.Serialize(result.Result.State) == SnapshotJson.Serialize(game.CreateSnapshot(0)),
            "The prepared result must carry the final revision and viewer-safe state.");

        var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(game.Events.Select(SerializeEvent).SequenceEqual(replay.Events.Select(SerializeEvent)) &&
                Enumerable.Range(0, game.PlayerCount).All(seat =>
                    SnapshotJson.Serialize(game.CreateSnapshot(seat)) ==
                    SnapshotJson.Serialize(replay.CreateSnapshot(seat))),
            "Every viewer and the committed event stream must match deterministic replay.");
    }

    private static string SerializeEvent(EventEnvelope item) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            item.Id, item.ParentId, item.Sequence, item.Revision, item.CorrelationId,
            PayloadType = item.Payload.GetType().FullName,
            Payload = System.Text.Json.JsonSerializer.Serialize(item.Payload, item.Payload.GetType())
        });

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
