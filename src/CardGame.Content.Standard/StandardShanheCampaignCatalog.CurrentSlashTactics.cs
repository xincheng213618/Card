using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    public const string CurrentDangtouSkillId = "shanhe:current-dangtou-2036";
    public const string CurrentDangtouRewardId = "tactic-dangtou-current-2036";
    public const string CurrentDangtou2SkillId = "shanhe:current-dangtou-2037";
    public const string CurrentDangtou2RewardId = "tactic-dangtou-current-2037";
    public const string CurrentCommonDangtouSkillId = "shanhe:current-dangtou-2038";
    public const string CurrentCommonDangtouRewardId = "tactic-dangtou-current-2038";
    public const string CurrentLeihuoSkillId = "shanhe:current-leihuo-2100";
    public const string CurrentLeihuoRewardId = "tactic-leihuo-current-2100";
    public const string CurrentLeihuo2SkillId = "shanhe:current-leihuo-2101";
    public const string CurrentLeihuo2RewardId = "tactic-leihuo-current-2101";
    public const string CurrentLianjiSkillId = "shanhe:current-lianji-2051";
    public const string CurrentLianjiRewardId = "tactic-lianji-current-2051";
    public const string CurrentLianji2SkillId = "shanhe:current-lianji-2052";
    public const string CurrentLianji2RewardId = "tactic-lianji-current-2052";
    public const string CurrentErlianjiSkillId = "shanhe:current-lianji-2053";
    public const string CurrentErlianjiRewardId = "tactic-lianji-current-2053";

    private static IReadOnlyList<ShanheRewardDefinition> CurrentSlashTacticRewards { get; } = Array.AsReadOnly(new[]
    {
        new ShanheRewardDefinition(CurrentDangtouRewardId, "战法·当头一棒")
        { TacticSkillId = CurrentDangtouSkillId, Description = "史诗战法：每轮，你使用的首张杀伤害+1。被闪避仍计入首张。不占额外技能槽，本版每次征程限获得一份。" },
        new ShanheRewardDefinition(CurrentDangtou2RewardId, "战法·当头一棒Ⅱ")
        { TacticSkillId = CurrentDangtou2SkillId, Description = "传说战法：每轮，你使用的首张杀伤害+2。被闪避仍计入首张。不占额外技能槽，本版每次征程限获得一份。" },
        new ShanheRewardDefinition(CurrentCommonDangtouRewardId, "战法·当头一棒")
        { TacticSkillId = CurrentCommonDangtouSkillId, Description = "普通战法：每轮，你使用的首张杀伤害+1。被闪避仍计入首张。不占额外技能槽，本版每次征程限获得一份。" },
        new ShanheRewardDefinition(CurrentLeihuoRewardId, "战法·雷火势Ⅰ")
        { TacticSkillId = CurrentLeihuoSkillId, Description = "稀有战法：每个回合，你使用的首张属性杀伤害+1。普通杀不占属性杀的序号。不占额外技能槽，本版每次征程限获得一份。" },
        new ShanheRewardDefinition(CurrentLeihuo2RewardId, "战法·雷火势Ⅱ")
        { TacticSkillId = CurrentLeihuo2SkillId, Description = "传说战法：每个回合，你使用的首张属性杀伤害+2。普通杀不占属性杀的序号。不占额外技能槽，本版每次征程限获得一份。" },
        new ShanheRewardDefinition(CurrentLianjiRewardId, "战法·连击Ⅰ")
        { TacticSkillId = CurrentLianjiSkillId, Description = "稀有战法：你的出牌阶段，出杀次数+1。普通杀、属性杀与转化杀共用次数。不占额外技能槽，本版每次征程限获得一份。" },
        new ShanheRewardDefinition(CurrentLianji2RewardId, "战法·连击Ⅱ")
        { TacticSkillId = CurrentLianji2SkillId, Description = "传说战法：你的出牌阶段，出杀次数+3。普通杀、属性杀与转化杀共用次数。不占额外技能槽，本版每次征程限获得一份。" },
        new ShanheRewardDefinition(CurrentErlianjiRewardId, "战法·二连击")
        { TacticSkillId = CurrentErlianjiSkillId, Description = "普通战法：你的出牌阶段，出杀次数+1。普通杀、属性杀与转化杀共用次数。不占额外技能槽，本版每次征程限获得一份。" }
    });

    private static ShanheCampaignDefinition WithCurrentSlashTactics(ShanheCampaignDefinition definition)
    {
        var rewardIds = CurrentSlashTacticRewards.Select(reward => reward.Id).ToArray();
        var choices = CurrentSlashTacticRewards.Select(reward => new ShanheNodeChoiceDefinition(
            "buy-" + reward.Id, $"{reward.Name} · {ShopMetadataByRewardId[reward.Id].Price}钱",
            GoldCost: ShopMetadataByRewardId[reward.Id].Price, RewardId: reward.Id, LeavesNode: false)).ToArray();
        return definition with
        {
            Rewards = definition.Rewards.Concat(CurrentSlashTacticRewards).ToArray(),
            Nodes = definition.Nodes.Select(node => node with
            {
                RewardIds = node.Kind is ShanheNodeKind.Battle or ShanheNodeKind.EliteBattle or ShanheNodeKind.BossBattle
                    ? (node.RewardIds ?? []).Concat(rewardIds).ToArray() : node.RewardIds,
                Choices = node.Kind == ShanheNodeKind.Shop ? (node.Choices ?? []).Concat(choices).ToArray() : node.Choices
            }).ToArray()
        };
    }

    private static bool RequiresCurrentSlashTacticsRegistry(ShanheBattleSetup setup) =>
        setup.TacticSkillIds.Concat(setup.ExtraSkillIds)
            .Concat(setup.AllyRoster.Concat(setup.EnemyRoster).SelectMany(member => member.ExtraSkillIds ?? []))
            .Any(id => id is CurrentDangtouSkillId or CurrentDangtou2SkillId or CurrentCommonDangtouSkillId or
                CurrentLeihuoSkillId or CurrentLeihuo2SkillId or CurrentLianjiSkillId or CurrentLianji2SkillId or CurrentErlianjiSkillId);
}
