namespace CardGame.Core;

/// <summary>One six-seat encounter with three allied players, a leader and two minions.</summary>
public sealed record ShenwuActivityDefinition(
    IReadOnlyList<string> PlayerPoolIds,
    IReadOnlyList<string> GodPoolIds,
    IReadOnlyList<string> LeaderPoolIds,
    IReadOnlyList<string> MinionGeneralIds,
    IReadOnlyList<int> PlayerSeatOffsets,
    int LeaderSeatOffset,
    IReadOnlyList<int> MinionSeatOffsets,
    int PlayerTier,
    IReadOnlyList<string> PlayerSkillIds,
    IReadOnlyDictionary<string, string> HalfHpSkillIds,
    string PlayerTeamId = "team:allies",
    string EnemyTeamId = "team:beasts");

public sealed record ShenwuSharedGodCandidatesEvent(IReadOnlyList<string> GeneralIds) : IGameEvent;
public sealed record ShenwuLeaderChosenEvent(int LeaderSeat, string GeneralId) : IGameEvent;
public sealed record ShenwuLeaderEnragedEvent(int LeaderSeat, string SkillId, int Hp, int MaximumHp) : IGameEvent;
