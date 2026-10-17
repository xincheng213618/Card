namespace CardGame.Core;

/// <summary>A deterministic eight-seat PvE battle, relative to the local player's seat.</summary>
public sealed record TeamActivityDefinition(
    IReadOnlyDictionary<string, IReadOnlyList<string>> PlayerPools,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SpiritPools,
    IReadOnlyDictionary<string, IReadOnlyList<string>> MachinePools,
    int PlayerTier,
    int SpiritTierMinimum,
    int SpiritTierMaximum,
    int MachineTier,
    IReadOnlyDictionary<string, IReadOnlyList<string>> PlayerSkillIds);
