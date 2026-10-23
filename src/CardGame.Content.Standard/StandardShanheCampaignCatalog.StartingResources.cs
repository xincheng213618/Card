using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    // Apply only after the full new definition has been composed. Historical
    // constructors keep their original descriptions and entire content hash.
    private static ShanheCampaignDefinition WithStartingResourcesContract(ShanheCampaignDefinition definition)
    {
        const string useNotice = "使用或响应结束时，若原应进入弃牌堆，则移出游戏；若先被技能获得，仍保留该牌。";
        var physical = definition.Rewards.Where(reward => reward.StartingCards?.Count > 0 || reward.StartingEquipment?.Count > 0)
            .ToDictionary(reward => reward.Id, reward => reward with
            {
                Description = reward.Description
                    .Replace("本地版按普通实体牌流转。", string.Empty, StringComparison.Ordinal)
                    .Replace("每场开始装备八卦阵", "每场开始将八卦阵加入手牌", StringComparison.Ordinal)
                    .Replace("每场开始装备青釭剑", "每场开始将青釭剑加入手牌", StringComparison.Ordinal)
                    .Replace("每场战斗开始时装备", "每场战斗开始时加入手牌", StringComparison.Ordinal) + useNotice
            }, StringComparer.Ordinal);
        var originals = definition.Rewards.ToDictionary(reward => reward.Id, StringComparer.Ordinal);
        return ShanheCampaignContent.FreezeAndValidate(definition with
        {
            StartingResourcesExpireOnUse = true,
            Rewards = definition.Rewards.Select(reward => physical.GetValueOrDefault(reward.Id) ?? reward).ToArray(),
            Nodes = definition.Nodes.Select(node => node with
            {
                // Codex shops copy the reward text before adding the price. Keep
                // that authored price and update only the copied resource contract.
                Choices = node.Choices?.Select(choice => choice.RewardId is { } id && physical.TryGetValue(id, out var updated) &&
                    !string.IsNullOrEmpty(originals[id].Description) && !string.IsNullOrEmpty(choice.Description)
                    ? choice with { Description = choice.Description.Replace(originals[id].Description, updated.Description, StringComparison.Ordinal) }
                    : choice).ToArray()
            }).ToArray()
        });
    }
}
