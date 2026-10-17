using System.Collections.ObjectModel;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private sealed record CodexRewardBinding(int NodeId, ShanheRewardDefinition Reward);

    // Explicit matches against Assets/Data/shanhe-codex-2026.json. Same-name
    // variants are not interchangeable: e.g. node 3 is OL Biyue, not classic Biyue.
    private static readonly IReadOnlyList<CodexRewardBinding> CodexRewardBindings = Array.AsReadOnly(new[]
    {
        CodexSkill(3, "闭月", "ol:biyue", "结束阶段，若你没有手牌，你可以摸两张牌，否则你可以摸一张牌。"),
        CodexSkill(4, "势斩", "boundary:shizhan-current", "出牌阶段限两次，你可以令一名其他角色视为对你使用一张【决斗】。"),
        CodexSkill(5, "烈刃", "classic:lieren", "当你使用【杀】对目标角色造成伤害后，你可以与其拼点：若你赢，你获得其一张牌。"),
        CodexSkill(6, "卫境", "ol:weijing", "每轮限一次，你可以视为使用一张【杀】或【闪】。"),
        CodexSkill(7, "绝情", "classic:jueqing", "锁定技，你即将造成的伤害视为失去体力。"),
        CodexSkill(9, "困奋", "ol:kunfen", "锁定技，结束阶段，你失去1点体力，然后摸两张牌。"),
        CodexSkill(10, "矜功", "ol:jingong", "出牌阶段限一次，你可以将一张【杀】或装备牌当三张随机锦囊牌中的一张使用。"),
        CodexEquipment(13, "藤甲", CardKind.Tengjia,
            "锁定技，【南蛮入侵】【万箭齐发】和普通【杀】对你无效。当你受到火焰伤害时，此伤害+1。"),
        CodexSkill(14, "奇策", "classic:qice", "出牌阶段限一次，你可以将所有手牌当任意普通锦囊牌使用。"),
        CodexEquipment(15, "寒冰剑", CardKind.IceSword,
            "攻击范围2。当你使用【杀】对目标角色造成伤害时，若其有牌，你可以防止此伤害，然后你依次弃置其两张牌。"),
        CodexSkill(18, "智愚", "classic:zhiyu", "当你受到伤害后，你可以摸一张牌，然后展示所有手牌，若颜色均相同，伤害来源弃置一张手牌。"),
        CodexEquipment(19, "古锭刀", CardKind.GudingBlade,
            "攻击范围2。锁定技，当你使用【杀】对目标角色造成伤害时，若该角色没有手牌，则此伤害+1。"),
        CodexEquipment(21, "丈八蛇矛", CardKind.ZhangbaSerpentSpear,
            "攻击范围3。你可以将两张手牌当【杀】使用或打出。"),
        CodexEquipment(52, "朱雀羽扇", CardKind.ZhuqueFan,
            "攻击范围4。当你使用普通【杀】时，你可以将此【杀】改为火【杀】。"),
        CodexEquipment(54, "赤炎镇魂琴", CardKind.CrimsonSoulZither,
            "锁定技，你造成的伤害均视为火焰伤害。"),
        CodexEquipment(57, "玲珑狮蛮带", CardKind.LionBelt,
            "当其他角色使用牌指定你为唯一目标后，你可以判定，若结果为红桃，此牌对你无效。"),
        CodexEquipment(66, "仁王盾", CardKind.RenwangShield, "锁定技，黑色【杀】对你无效。"),
        CodexEquipment(88, "虚妄之冕", CardKind.DelusionCrown, "锁定技，摸牌阶段，你多摸两张牌。你的手牌上限-1。"),
        CodexEquipment(101, "鬼龙斩月刀", CardKind.GhostDragonCrescentBlade, "锁定技，你使用的红色【杀】不能被【闪】响应。"),
        CodexEquipment(115, "国风玉袍", CardKind.NationalStyleRobe, "锁定技，你不能成为其他角色使用普通锦囊牌的目标。")
    });

    /// <summary>Verified source-tree node ids and their acquirable runtime reward ids.</summary>
    public static IReadOnlyDictionary<int, string> CodexRewardIdsByNodeId { get; } =
        new ReadOnlyDictionary<int, string>(CodexRewardBindings.Select(binding => new KeyValuePair<int, string>(binding.NodeId, binding.Reward.Id))
            .Append(new(2, OpeningInheritanceRewardId)).ToDictionary(pair => pair.Key, pair => pair.Value));

    /// <summary>Adds the opt-in codex pool without altering the historical default campaign definition.</summary>
    public static ShanheCampaignDefinition CreateCodexDefinition(int difficulty, string editionId)
    {
        var original = CreateDefinition(difficulty, editionId);
        var additionalRewards = CodexRewardBindings.Select(binding => binding.Reward).Concat(new[]
        {
            CodexBasicCard(4004), CodexBasicCard(4008), CodexBasicCard(4076), CodexBasicCard(4092), CodexBasicCard(4107)
        }).ToArray();
        var allRewards = original.Rewards.Concat(additionalRewards).ToArray();
        var rewardIds = additionalRewards.Select(reward => reward.Id).ToArray();
        return WithOpeningInheritance(WithChapterRewards(original with
        {
            Rewards = allRewards,
            Nodes = original.Nodes.Select(node => node with
            {
                RewardIds = node.Kind is ShanheNodeKind.Battle or ShanheNodeKind.EliteBattle or ShanheNodeKind.BossBattle
                    ? (node.RewardIds ?? []).Where(id => id is not ("peach" or "duel")).Concat(rewardIds).ToArray() : node.RewardIds,
                Choices = node.Kind == ShanheNodeKind.Shop
                    ? CodexShopChoices(node, allRewards) : node.Choices
            }).ToArray()
        }));
    }

    private static CodexRewardBinding CodexSkill(int nodeId, string name, string skillId, string description) =>
        new(nodeId, new("codex-" + nodeId.ToString(System.Globalization.CultureInfo.InvariantCulture), "传习·" + name, SkillId: skillId)
        { Description = description + "占用一个额外技能槽。" });

    private static CodexRewardBinding CodexEquipment(int nodeId, string name, CardKind kind, string description)
    {
        var rewardId = "codex-" + nodeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var metadata = ShopMetadataByRewardId[rewardId];
        return new(nodeId, new(rewardId, name, StartingEquipment: [new(kind, metadata.Suit!.Value, metadata.Rank!.Value)])
        { Description = description + "每场战斗开始时装备；同槽新装备替换已有开场装备。" });
    }

    private static ShanheRewardDefinition CodexBasicCard(int sourceId)
    {
        var rewardId = "codex-card-" + sourceId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var metadata = ShopMetadataByRewardId[rewardId];
        var kind = metadata.CardKind!.Value;
        var card = new ShanheStartingCard(kind, metadata.Suit!.Value, metadata.Rank!.Value);
        return new(rewardId, CardCatalog.Get(kind).DisplayName,
            StartingCards: EquipmentCatalog.IsEquipment(kind) ? null : [card],
            StartingEquipment: EquipmentCatalog.IsEquipment(kind) ? [card] : null)
        { Description = CardCatalog.Get(kind).Description + (EquipmentCatalog.IsEquipment(kind) ? "每场战斗开始时装备。" : "每场战斗开始时获得这张牌。") };
    }

    private static IReadOnlyList<ShanheNodeChoiceDefinition> CodexShopChoices(ShanheNodeDefinition node, IReadOnlyList<ShanheRewardDefinition> rewards)
    {
        // This opt-in shop contains catalogue-priced stock only. Local medicine
        // and additive training remain available through their original route
        // choices and battle rewards, without inventing an original shop price.
        var authored = (node.Choices ?? []).Where(choice => choice.RewardId is null ||
            choice.RewardId is not ("peach" or "duel") && ShopMetadataByRewardId.ContainsKey(choice.RewardId)).ToArray();
        var existing = authored.Select(choice => choice.RewardId).ToHashSet(StringComparer.Ordinal);
        return authored.Concat(rewards.Where(reward => ShopMetadataByRewardId.ContainsKey(reward.Id) && !existing.Contains(reward.Id))
            .Select(reward => new ShanheNodeChoiceDefinition("buy-" + reward.Id, reward.Name, RewardId: reward.Id, LeavesNode: false)))
            .Select(choice => choice.RewardId is { } rewardId && ShopMetadataByRewardId.TryGetValue(rewardId, out var metadata)
                ? choice with
                {
                    Name = rewards.Single(reward => reward.Id == rewardId).Name + $" · {metadata.Price}钱",
                    GoldCost = metadata.Price,
                    Description = rewards.Single(reward => reward.Id == rewardId).Description + $"\n公开条目原价{metadata.Price}铜钱。"
                } : choice).ToArray();
    }

    private static readonly IReadOnlyDictionary<CardKind, string> CodexAdditionalEquipment = new ReadOnlyDictionary<CardKind, string>(
        new Dictionary<CardKind, string>
        {
            [CardKind.CrimsonSoulZither] = "crimson-soul-zither",
            [CardKind.LionBelt] = "lion-belt",
            [CardKind.DelusionCrown] = "delusion-crown",
            [CardKind.GhostDragonCrescentBlade] = "ghost-dragon-crescent-blade",
            [CardKind.NationalStyleRobe] = "national-style-robe"
        });

    private static bool RequiresCodexEquipmentRegistry(ShanheBattleSetup setup) =>
        setup.StartingEquipment.Any(card => CodexAdditionalEquipment.ContainsKey(card.Kind));

    // Only recipes that actually carry a newly available equipment kind receive
    // this package. Historical battle registries therefore keep their old hash.
    private sealed class ShanheCodexEquipmentPackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("shanhe-codex-equipment", new Version(1, 0, 0),
            [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var (kind, id) in CodexAdditionalEquipment)
            {
                var card = CardCatalog.Get(kind);
                builder.AddCard(new("shanhe-codex:equipment-" + id, card.DisplayName, card.CategoryName, card.Description, kind));
            }
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("hulao-equipment", "hulao:lion-belt"));
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("shenwu-equipment", "shenwu:delusion-crown") with { Tags = SkillTag.Locked });
        }
    }
}
