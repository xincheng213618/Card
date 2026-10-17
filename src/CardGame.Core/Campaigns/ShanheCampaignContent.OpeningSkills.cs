namespace CardGame.Core.Campaigns;

public static partial class ShanheCampaignContent
{
    private static void ValidateOpeningSkillReward(ShanheCampaignDefinition definition, ShanheRewardDefinition reward)
    {
        if (reward.OpeningSkillRewardIds is not { } pool) return;
        Require(pool.Count is > 0 and <= 512, "An initial skill tactic requires a bounded explicit skill pool.");
        UniqueIds(pool, "opening skill reward");
        Require(reward.TacticSkillId is null && reward.SkillId is null && reward.Gold == 0 && reward.Heal == 0 &&
            reward.AddSkillSlots == 0 && (reward.Bonuses is null || reward.Bonuses == new ShanheTacticBonuses()) &&
            (reward.StartingCards?.Count ?? 0) == 0 && (reward.StartingEquipment?.Count ?? 0) == 0,
            "An initial skill tactic must not impersonate a battle program or a direct skill grant.");
        foreach (var id in pool)
            Require(definition.Rewards.SingleOrDefault(candidate => candidate.Id == id) is { SkillId: not null, OpeningSkillRewardIds: null },
                "An initial skill pool may reference only known ordinary skill rewards.");
        Require(definition.Nodes.All(node => !(node.RewardIds ?? []).Contains(reward.Id, StringComparer.Ordinal) &&
            !(node.Choices ?? []).Any(choice => choice.RewardId == reward.Id) &&
            !(node.BossRewards?.Groups ?? []).Any(group => group.RewardIds.Contains(reward.Id, StringComparer.Ordinal))),
            "An initial skill tactic can only be equipped before an expedition, not granted repeatedly on the route.");
    }
}
