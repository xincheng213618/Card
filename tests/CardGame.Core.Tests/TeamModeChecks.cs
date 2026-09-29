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
