namespace CardGame.Core;

/// <summary>A cooperative three-player campaign against successive single bosses.</summary>
public sealed record BossActivityDefinition(
    IReadOnlyList<string> PlayerPoolIds,
    IReadOnlyList<BossActivityStage> Stages,
    int PlayerTier,
    IReadOnlyList<string> PlayerSkillIds,
    string PlayerTeamId = "team:allies",
    string EnemyTeamId = "team:boss");

public sealed record BossActivityStage(string Name, IReadOnlyList<string> GeneralPoolIds, int InitialHandSize = 4);

public sealed record BossActivityStageStartedEvent(int StageIndex, int BossSeat, string GeneralId) : IGameEvent;
