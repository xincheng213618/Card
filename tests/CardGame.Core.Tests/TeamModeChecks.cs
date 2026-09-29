using System.Text.Json;
using System.Text.Json.Serialization;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class TeamModeChecks
{
    private static readonly JsonSerializerOptions EventJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void ContentAndPrivacy()
    {
        var standard = StandardContentRegistry.Create();
        var registry = StandardContentRegistry.CreateWithTeamModes();
        var mode = registry.Modes["team:standard-2v2"];

        Require(standard.Packages.Count == 1, "The identity registry must keep its original package set.");
        Require(registry.Packages.Count == 2 &&
                registry.Packages.Any(package => package.Id == "standard-team-modes"),
            "The team mode must be isolated in its opt-in package.");
        Require(standard.ContentHash != registry.ContentHash,
            "Team metadata must participate in a distinct content hash.");
        Require(mode.ModeKind == ContentModeKind.Team &&
                mode.TeamCounts is not null &&
                mode.TeamCounts["team:blue"] == 2 &&
                mode.TeamCounts["team:red"] == 2,
            "The standard team mode must expose two public teams of two.");

        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 20260908,
            PlayerCount = 4,
            HumanSeat = 0,
            HumanRole = null,
            HumanTeamId = "team:blue",
            ModeId = "team:standard-2v2",
            UseInteractiveSetup = false,
            UseInteractiveDiscard = false,
            MaxTurns = 120
        }, registry);

        var state = game.State;
        Require(state.Seed is null, "Ordinary team snapshots must not expose the seed.");
        Require(state.Players.Count == 4 && state.Players.All(player =>
            player.TeamId is "team:blue" or "team:red" &&
            player.IsTeamRevealed &&
            player.Role is Role.TeamA or Role.TeamB),
            "Team membership and compatibility roles must be public to every viewer.");
        Require(state.Players.Count(player => player.TeamId == "team:blue") == 2 &&
                state.Players.Count(player => player.TeamId == "team:red") == 2,
            "Team assignment must match the registered distribution.");
        Require(state.Players.Single(player => player.Seat == 0).TeamId == "team:blue",
            "The configured human team override must be honored.");
        Require(state.Players.Single(player => player.Seat == state.CurrentSeat).HandCount == 3,
            "The first public-team seat must start with one fewer card.");
        Require(state.Players.Where(player => player.Seat != 0).All(player => player.Hand.Count == 0),
            "Other team hands must remain redacted.");

        var start = game.Submit(new StartGameCommand());
        Require(start.Accepted, "The public-team game must start through the command boundary.");
        Require(game.Events.Count(eventItem => eventItem.Payload is TeamAssignedEvent) == 4,
            "Setup must publish one public team assignment per seat.");
        Require(game.Events.Any(eventItem => eventItem.Payload is GameStartedEvent),
            "A non-interactive team setup must publish GameStarted.");
    }

    public static void AiMatchIsDeterministic()
    {
        var registry = StandardContentRegistry.CreateWithTeamModes();
        var options = new GameOptions
        {
            Seed = 721019,
            PlayerCount = 4,
            HumanSeat = -1,
            HumanRole = null,
            ModeId = "team:standard-2v2",
            UseInteractiveSetup = false,
            UseInteractiveDiscard = false,
            AiPolicyVersion = 2,
            MaxTurns = 160
        };
        var left = GameEngine.CreateStandard(options, registry);
        var right = GameEngine.CreateStandard(options, registry);
        var leftResult = left.Submit(new StartGameCommand());
        var rightResult = right.Submit(new StartGameCommand());

        Require(leftResult.Accepted && rightResult.Accepted, "Both fixed-seed team games must start.");
        Require(left.State.Status == EngineStatus.Completed && right.State.Status == EngineStatus.Completed,
            "The fixed-seed all-AI team match must terminate.");
        Require(left.State.Winner is Winner.TeamA or Winner.TeamB or Winner.Draw,
            "The team match must report a team winner or an explicit draw.");
        Require(left.State.Winner == right.State.Winner &&
                left.State.WinnerTeamId == right.State.WinnerTeamId,
            "Fixed-seed team winners must be deterministic.");
        Require(SnapshotJson.Serialize(left.CreateSnapshot(-1, revealAll: true)) ==
                SnapshotJson.Serialize(right.CreateSnapshot(-1, revealAll: true)),
            "Fixed-seed team snapshots must replay identically.");
        Require(EventSignatures(left).SequenceEqual(EventSignatures(right)),
            "Fixed-seed team event streams must replay identically.");
        Require(left.CreateCardZoneDiagnostics().Count == 90 &&
                left.CreateCardZoneDiagnostics().Select(card => card.CardId).Distinct().Count() == 90,
            "Team mode must conserve the physical card inventory.");

        var restored = GameReplay.Restore(left.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(left.CreateSnapshot(-1, revealAll: true)) ==
                SnapshotJson.Serialize(restored.CreateSnapshot(-1, revealAll: true)) &&
                EventSignatures(left).SequenceEqual(EventSignatures(restored)),
            "A completed team checkpoint must restore the same public result and events.");
    }

    public static void WinningTeamRules()
    {
        Require(GameRules.EvaluateWinningTeam(
            [
                new TeamLifeState("team:blue", true),
                new TeamLifeState("team:blue", true),
                new TeamLifeState("team:red", false),
                new TeamLifeState("team:red", false)
            ]) == "team:blue",
            "A surviving blue team must be recognized as the winner.");
        Require(GameRules.EvaluateWinningTeam(
            [
                new TeamLifeState("team:blue", true),
                new TeamLifeState("team:red", true)
            ]) is null,
            "Two surviving teams must keep the match in progress.");
    }

    private static IEnumerable<string> EventSignatures(GameEngine game) =>
        game.Events.Select(eventItem =>
            $"{eventItem.Sequence}:{eventItem.Payload.GetType().FullName}:" +
            JsonSerializer.Serialize(eventItem.Payload, eventItem.Payload.GetType(), EventJsonOptions));
}
