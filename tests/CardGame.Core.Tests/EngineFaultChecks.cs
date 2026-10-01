using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class EngineFaultChecks
{
    public static void CommittedObserversSeeTheAcceptedCommand()
    {
        var game = CreateGame();
        var observed = 0;
        game.EventCommitted += _ =>
        {
            observed++;
            Check(game.AcceptedCommands.Count == game.Revision,
                "A committed event must expose the matching accepted command prefix.");
            Check(game.CreateCheckpoint().Revision == game.Revision,
                "A committed event must permit a consistent checkpoint.");
        };

        var result = game.Submit(new StartGameCommand(game.Revision));
        Check(result.Accepted && observed > 0, "The start command must commit observable events.");
    }

    public static void InternalFailureStopsTheSessionAndPreservesTheLastPrefix()
    {
        var registry = StandardContentRegistry.Create();
        var game = CreateGame(registry);
        Check(game.Submit(new StartGameCommand(game.Revision)).Accepted, "The setup command must be accepted.");
        var committed = game.CreateCheckpoint();
        var committedJson = GameCheckpointJson.Serialize(committed);
        var trustedViews = ViewerSnapshots(game);
        var trustedEvents = EventTrace(game);
        var trustedZones = game.CreateCardZoneDiagnostics().ToArray();

        // Force the public-operation invariant to throw after command execution.
        var field = typeof(GameEngine).GetField("_initialCardCount",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The card-inventory invariant field was not found.");
        field.SetValue(game, (int)field.GetValue(game)! + 1);
        Throws(() => game.Submit(new AdvanceOneStepCommand(game.Revision)));

        Check(game.IsFaulted, "An internal failure must poison the mutable session.");
        Check(game.State.Status == EngineStatus.Faulted,
            "A faulted session must not report an ordinary running status.");
        Check(game.AcceptedCommands.Count == committed.Commands.Count,
            "The failed command must not enter the trusted journal.");
        Check(game.LastTrustedCheckpoint is { } recovery &&
              recovery.Revision == committed.Revision &&
              GameCheckpointJson.Serialize(recovery) == committedJson,
            "The last complete command prefix must be available for reconstruction.");
        Throws(() => game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Throws(() => game.CreateCheckpoint());
        var restored = GameReplay.Restore(game.LastTrustedCheckpoint!, registry);
        Check(!restored.IsFaulted && restored.Revision == committed.Revision &&
              ViewerSnapshots(restored).SequenceEqual(trustedViews) &&
              EventTrace(restored).SequenceEqual(trustedEvents) &&
              restored.CreateCardZoneDiagnostics().SequenceEqual(trustedZones),
            "Replaying the trusted prefix must restore every viewer snapshot, ordered event and physical card zone.");
    }

    private static string[] ViewerSnapshots(GameEngine game) =>
        Enumerable.Range(0, game.PlayerCount)
            .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)))
            .Append(SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)))
            .ToArray();

    private static string[] EventTrace(GameEngine game) =>
        game.Events.Select(item => JsonSerializer.Serialize(new
        {
            item.Id,
            item.ParentId,
            item.Sequence,
            item.Revision,
            item.CorrelationId,
            PayloadType = item.Payload.GetType().FullName,
            Payload = JsonSerializer.Serialize(item.Payload, item.Payload.GetType())
        })).ToArray();

    private static GameEngine CreateGame(ContentRegistry? registry = null) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = 337,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false,
            MaxTurns = 100
        }, registry ?? StandardContentRegistry.Create());

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException("The operation unexpectedly succeeded.");
    }
}
