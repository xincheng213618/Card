namespace CardGame.Core.Campaigns;

public sealed record ShanheBossRewardGroupDefinition(string Id, string Name, IReadOnlyList<string> RewardIds, int ChoiceCount = 3);
public sealed record ShanheBossRewardDefinition(int FixedGold, int SkillSlots, IReadOnlyList<ShanheBossRewardGroupDefinition> Groups);

public sealed record ShanheBossRewardGroupSnapshot(string Id, string Name,
    IReadOnlyList<ShanheRewardDefinition> Offers, ShanheRewardDefinition? SelectedReward)
{
    public bool IsExhausted => Offers.Count == 0;
    public bool IsResolved => SelectedReward is not null || IsExhausted;
}

public sealed record ShanheBossRewardSummary(int ChapterNumber, string Title, int FixedGold, int SkillSlots,
    IReadOnlyList<ShanheBossRewardGroupSnapshot> Groups, long Revision)
{
    public bool CanConfirm => Groups.All(group => group.IsResolved);
}
