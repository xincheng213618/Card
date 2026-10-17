using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>The anniversary July 2019 eight-seat survival battle.</summary>
public sealed record WenheActivityDefinition(int PlayerTier = 5, bool EnableAspirations = true);

public enum WenheRuleKind { Chaos, Bounty, LastStand, Equipment, Sweep, Famine, Poison }
public sealed record WenheRoundRuleSnapshot(int RoundNumber, WenheRuleKind Rule);
public sealed record WenheAspirationSnapshot(int GoalRank, bool CanUseChampionMark, bool ChampionMarkUsed);

public sealed record WenheRoundRuleChangedEvent(int RoundNumber, WenheRuleKind Rule) : IGameEvent;
public sealed record WenhePlayerRankedEvent(long DeathFrameId, int Seat, int Rank) : IGameEvent;
public sealed record WenheChampionMarkUsedEvent(long ProgramFrameId, int Seat) : IGameEvent;

public sealed partial record PlayerSnapshot
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SurvivalRank { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? WenheChampionMarkUsed { get; init; }
}
