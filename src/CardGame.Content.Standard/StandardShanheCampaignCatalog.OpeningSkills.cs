using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    public const string OpeningInheritanceRewardId = "codex-2";

    private static ShanheCampaignDefinition WithOpeningInheritance(ShanheCampaignDefinition definition)
    {
        var registry = PlayerRosterRegistry;
        var pool = definition.Rewards.Where(reward => reward.SkillId is { } skill &&
                ShopMetadataByRewardId.TryGetValue(reward.Id, out var metadata) && metadata.Rarity == 2 && metadata.Kind == "技能" &&
                registry.Skills.TryGetValue(skill, out var implementation) && implementation.ImplementationStatus == SkillImplementationStatus.Complete)
            .DistinctBy(reward => reward.SkillId).Select(reward => reward.Id).ToArray();
        if (pool.Length < 3) throw new InvalidOperationException("The inheritance tactic requires at least three implemented rare skills.");
        return definition with
        {
            Rewards = definition.Rewards.Append(new ShanheRewardDefinition(OpeningInheritanceRewardId, "战法·传承")
            {
                Description = "开局时，从三个稀有技能中选择并获得一个。本次征程仅触发一次，获得的技能占用额外技能槽。",
                OpeningSkillRewardIds = pool
            }).ToArray()
        };
    }
}
