namespace CardGame.Core.Campaigns;

public static partial class ShanheCampaignContent
{
    private static void ValidateBossRewards(ShanheNodeDefinition node, IReadOnlySet<string> rewardIds)
    {
        Require(node.ChapterNumber is >= 0 and <= 99, "Chapter numbers must be between zero and ninety-nine.");
        if (node.ChapterTitle is not null) RequireText(node.ChapterTitle, "chapter title");
        if (node.BossRewards is not { } bundle) return;
        Require(node.Kind == ShanheNodeKind.BossBattle && node.ChapterNumber > 0,
            "Only a numbered chapter boss can publish a completion reward bundle.");
        Require(bundle.FixedGold is >= 0 and <= 100_000 && bundle.SkillSlots is >= 0 and <= 8,
            "Invalid chapter completion credits.");
        Require(bundle.Groups is { Count: > 0 and <= 8 } && bundle.Groups.All(group => group is not null),
            "A chapter bundle requires one to eight explicit reward groups.");
        UniqueIds(bundle.Groups.Select(group => group.Id), "boss reward group");
        var grouped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in bundle.Groups)
        {
            RequireText(group.Name, "boss reward group name");
            Require(group.ChoiceCount is >= 1 and <= 12 && group.RewardIds is { Count: > 0 and <= 512 } &&
                group.RewardIds.Count >= group.ChoiceCount, "A boss group requires a bounded candidate pool for its offer count.");
            UniqueIds(group.RewardIds, "boss group reward");
            Require(group.RewardIds.All(id => rewardIds.Contains(id) && grouped.Add(id)),
                "Boss groups must reference known, disjoint reward pools.");
        }
    }

    private static ShanheBossRewardDefinition? FreezeBossRewards(ShanheBossRewardDefinition? bundle) =>
        bundle is null ? null : bundle with
        {
            Groups = Freeze(bundle.Groups.Select(group => group with { RewardIds = Freeze(group.RewardIds) }))
        };
}
