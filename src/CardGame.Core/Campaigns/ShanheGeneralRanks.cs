namespace CardGame.Core.Campaigns;

/// <summary>Verified general-rank privileges fixed when an expedition begins.</summary>
public static class ShanheGeneralRanks
{
    public const string RebirthSkillId = "shanhe:rank-five-rebirth";

    public static ShanheTacticBonuses GetBonuses(int rank) => rank switch
    {
        1 => new(),
        2 => new(InitialHand: 1),
        3 => new(MaxHp: 1, InitialHand: 1, Slash: 1),
        4 => new(MaxHp: 1, InitialHand: 2, Draw: 1, Slash: 1),
        5 => new(MaxHp: 2, InitialHand: 2, Draw: 1, Slash: 1),
        _ => throw new ArgumentOutOfRangeException(nameof(rank), "A Shanhe general rank must be between one and five.")
    };
}
