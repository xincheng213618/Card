using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class RecordedSelectionSaveChecks
{
    public static void LoadHistoricalFingerprintWithoutReplacingSource()
    {
        var registry = ComposedSkillContentRegistry.CreateShowcase(includeActivities: true, historicalSelectionWeights: true);
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, HumanSeat = 0, HumanRole = Role.Lord,
            AiPolicyVersion = AiDecisionPolicy.CorrelatedActiveSkillVersion, UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false }, registry);
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "Historical setup starts through its normal command.");
        var checkpoint = game.CreateCheckpoint();
        var source = GameCheckpointJson.Serialize(checkpoint);
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, checkpoint));
        using var model = new MainViewModel(false, game.Seed, true, store) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        var restored = Program.Engine(model);
        Program.Assert(restored.Revision == game.Revision && restored.CreateCheckpoint().ContentHash == registry.ContentHash &&
            Enumerable.Range(0, game.PlayerCount).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(restored.CreateSnapshot(seat))) &&
            store.WriteCount == 1 && GameCheckpointJson.Serialize(store.Read(GameSaveSlot.Manual).Checkpoint) == source,
            "The actual desktop load chooses recorded metadata, restores all viewers and never replaces the source save.");
    }
}
