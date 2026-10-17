using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>The official June 2018 eight-seat four-team treasure race.</summary>
public sealed record DragonBoatActivityDefinition(
    IReadOnlyDictionary<string, IReadOnlyList<string>> PlayerPoolIds, int PlayerTier = 5, int Rounds = 2);

public sealed record DragonBoatTeamSnapshot(string TeamId, int Treasures, int Rank, long ReachedSequence, bool TangZiVictory);
public sealed record DragonBoatTreasureChangedEvent(string TeamId, int Previous, int Current, int SourceSeat, int TargetSeat, long ReachedSequence) : IGameEvent;
public sealed record DragonBoatTeamRankedEvent(string TeamId, int Rank, int Treasures, long ReachedSequence, bool TangZiVictory) : IGameEvent;

public sealed record DragonBoatDraftSnapshot(int TeammateSeat)
{
    private readonly IReadOnlyList<string> _generalIds = Array.Empty<string>();
    public IReadOnlyList<string> GeneralIds { get => _generalIds; init => _generalIds = Array.AsReadOnly(value.ToArray()); }
}
