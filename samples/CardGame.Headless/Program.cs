using CardGame.Content.Standard;
using CardGame.Core;

if (args.Length > 1 || args.Length == 1 && args[0] != "--custom-general")
{
    Console.Error.WriteLine("Usage: dotnet run --project samples/CardGame.Headless [-- --custom-general]");
    return 1;
}

var useCustomGeneral = args.Length == 1;
var registry = StandardContentRegistry.Create();
if (useCustomGeneral)
{
    registry = ContentRegistry.Build(new StandardContentPackage(),
        new TrainingGeneralPackage(registry.Modes["identity:standard-5"]));
}

var engine = GameEngine.CreateStandard(new GameOptions
{
    Seed = 337,
    PlayerCount = 5,
    HumanSeat = 0,
    HumanRole = Role.Lord,
    ModeId = useCustomGeneral ? TrainingGeneralPackage.ModeId : "identity:standard-5",
    UseInteractiveSetup = true,
    AiPolicyVersion = AiDecisionPolicy.CurrentVersion
}, registry);

// Host commands use the current revision. Player commands also answer the exact
// prompt published in that player's view; never invent prompt or choice ids.
Submit(new StartGameCommand(engine.Revision));
var selection = engine.CreateSnapshot(viewerSeat: 0).PendingDecision;
Require(selection is { Kind: DecisionKind.SelectGeneral }, "Expected a private general-selection prompt.");
var generalId = useCustomGeneral ? TrainingGeneralPackage.GeneralId : selection!.ValidContentIds[0];
Require(selection!.ValidContentIds.Contains(generalId), "The selected general must be a published candidate.");
Submit(new SelectGeneralCommand(selection.PlayerSeat, generalId, engine.Revision, selection.PromptId));

// A UI or remote player receives only its own filtered snapshot.
var view = engine.CreateSnapshot(viewerSeat: 0);
var ownPlayer = view.Players.Single(player => player.Seat == 0);
Require(ownPlayer.Hand.Count > 0, "Setup should have dealt the player's hand.");
Require(view.Players.Where(player => player.Seat != 0).All(player => player.Hand.Count == 0),
    "A player's snapshot must not expose opponents' hand cards.");
Console.WriteLine($"Mode: {engine.ModeId}");
Console.WriteLine($"Selected general: {ownPlayer.GeneralId}");
Console.WriteLine($"Revision: {engine.Revision}; next decision: {view.PendingDecision?.Kind}");
Console.WriteLine($"Player view: {ownPlayer.Hand.Count} own cards visible; opponents' hand cards hidden.");

// Checkpoints contain private setup data and the accepted command journal.
// Keep this JSON on the trusted host; it is not a player/network payload.
var checkpointJson = GameCheckpointJson.Serialize(engine.CreateCheckpoint());
var checkpoint = GameCheckpointJson.Deserialize(checkpointJson);
var restored = GameReplay.Restore(checkpoint, registry);
var replayed = GameReplay.Replay(checkpoint.Options, checkpoint.Commands, registry);
foreach (var reconstructed in new[] { restored, replayed })
{
    Require(GameCheckpointJson.Serialize(reconstructed.CreateCheckpoint()) == checkpointJson,
        "The reconstructed checkpoint must match the original.");
    for (var seat = 0; seat < engine.PlayerCount; seat++)
    {
        Require(SnapshotJson.Serialize(engine.CreateSnapshot(seat)) ==
                SnapshotJson.Serialize(reconstructed.CreateSnapshot(seat)),
            $"The reconstructed snapshot for seat {seat} must match the original.");
    }
}
Console.WriteLine($"Checkpoint JSON round trip and command replay: {checkpoint.Commands.Count} commands; " +
                  $"all {engine.PlayerCount} viewer snapshots match.");
return 0;

void Submit(GameCommand command)
{
    var result = engine.Submit(command);
    Require(result.Accepted, $"{command.GetType().Name} rejected: {result.Error?.Code}: {result.Error?.Message}");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
