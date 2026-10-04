namespace CardGame.Core;

public sealed record PlayerSkillContext(
    int Seat,
    int Hp,
    int MaxHp,
    int HandCount,
    TurnPhase Phase,
    bool IsOwnTurn = false,
    bool IsFaceDown = false,
    bool IsChained = false,
    bool IsClassicIdentityMode = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, bool>? RuntimeBooleanStates = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, int>? PublicCounters = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? HandLimit = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] bool? HasUsableHandCard = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] bool? PreviousPlayCardIsBasic = null);

public static class GameRules
{
    /// <summary>
    /// Returns every living player tied at the greatest positive marker count.
    /// Zero-only tables deliberately produce no candidate.
    /// </summary>
    public static IReadOnlyList<int> GetMaximumMarkerCandidates(
        IEnumerable<PlayerMarkerCandidateState> players)
    {
        ArgumentNullException.ThrowIfNull(players);
        var eligible = players
            .Where(player => player.IsAlive && player.Count > 0)
            .OrderBy(player => player.Seat)
            .ToArray();
        if (eligible.Length == 0)
        {
            return [];
        }

        var maximum = eligible.Max(player => player.Count);
        return Array.AsReadOnly(eligible
            .Where(player => player.Count == maximum)
            .Select(player => player.Seat)
            .ToArray());
    }

    /// <summary>
    /// Evaluates a public-team mode independently of identity roles. A team
    /// wins as soon as it is the only team with a living member; returning
    /// null keeps the in-progress and empty-table cases explicit.
    /// </summary>
    public static string? EvaluateWinningTeam(IEnumerable<TeamLifeState> players)
    {
        var survivingTeams = players
            .Where(player => player.IsAlive)
            .Select(player => player.TeamId)
            .Where(teamId => !string.IsNullOrWhiteSpace(teamId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return survivingTeams.Length == 1 ? survivingTeams[0] : null;
    }

    /// <summary>
    /// Evaluates a national-war mode independently of identity roles and
    /// public-team compatibility values.
    /// </summary>
    public static string? EvaluateWinningFaction(IEnumerable<FactionLifeState> players)
    {
        var survivingFactions = players
            .Where(player => player.IsAlive)
            .Select(player => player.FactionId)
            .Where(factionId => !string.IsNullOrWhiteSpace(factionId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return survivingFactions.Length == 1 ? survivingFactions[0] : null;
    }

    public static Winner EvaluateWinner(IEnumerable<PlayerLifeState> players)
    {
        var list = players.ToArray();
        if (list.Length == 0)
        {
            return Winner.None;
        }

        var lord = list.Single(player => player.Role == Role.Lord);
        if (!lord.IsAlive)
        {
            var survivors = list.Where(player => player.IsAlive).ToArray();
            return survivors.Length == 1 && survivors[0].Role == Role.Renegade
                ? Winner.Renegade
                : Winner.Rebels;
        }

        var enemiesRemain = list.Any(player =>
            player.IsAlive && player.Role is Role.Rebel or Role.Renegade);

        return enemiesRemain ? Winner.None : Winner.LordAndLoyalists;
    }
}
