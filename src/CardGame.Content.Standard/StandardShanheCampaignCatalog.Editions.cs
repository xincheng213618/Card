using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public sealed record ShanheCampaignEdition(string Id, string Name, string Description);

public static partial class StandardShanheCampaignCatalog
{
    public const string LocalEditionId = "lords-dong-local";
    public const string Returning2026EditionId = "lords-dong-2026";

    public static IReadOnlyList<ShanheCampaignEdition> Editions { get; } = Array.AsReadOnly(new[]
    {
        new ShanheCampaignEdition(LocalEditionId, "诸侯伐董 · 2024主题本地版",
            "采用已核实的2024战法和技能版本；剧情、地图、敌军及难度数值为本地编排。"),
        new ShanheCampaignEdition(Returning2026EditionId, "诸侯伐董 · 2026专属形态",
            "加入2026羁绊、队·初，以及可重复获得的精于谋略II、队·牌、队·甲，虎牢由风篁助战神鬼无前吕布；直接登场、同行场次与难度成长为本地编排，变身入口尚未还原。")
    });

    public static ShanheCampaignDefinition CreateDefinition(int difficulty, string editionId) => editionId switch
    {
        LocalEditionId => CreateDefinition(difficulty),
        Returning2026EditionId => CreateReturning2026Definition(difficulty),
        _ => throw new ArgumentException("Unknown Shanhe campaign edition.", nameof(editionId))
    };

    public static ShanheCampaignDefinition CreateDefinitionForCheckpoint(ShanheCampaignCheckpoint checkpoint, int difficulty)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(checkpoint.Options);
        ArgumentNullException.ThrowIfNull(checkpoint.Commands);
        var editionId = EditionIdForCampaign(checkpoint.CampaignId, difficulty);
        var definition = checkpoint.Options?.UseCodexRewardPool == true
            ? CreateCodexDefinition(difficulty, editionId) : CreateDefinition(difficulty, editionId);
        var roster = checkpoint.PlayerGeneralIds ?? LegacyPlayerGeneralIds;
        var available = GetPlayerGeneralIds().ToHashSet(StringComparer.Ordinal);
        if (roster.Count is 0 or > 2048 || roster.Any(id => id is null || !available.Contains(id)))
            throw new ArgumentException("Campaign checkpoint player roster is unavailable.", nameof(checkpoint));
        definition = ShanheCampaignContent.FreezeAndValidate(definition with { PlayerGeneralIds = roster });
        // Every historical candidate must match the entire fingerprint; optional metadata never bypasses it.
        var candidates = new List<ShanheCampaignDefinition> { definition };
        if (!checkpoint.Commands.Any(command => command is ChooseShanheBossRewardCommand or ConfirmShanheBossRewardCommand))
            candidates.Add(WithoutChapterRewards(definition));
        if (!checkpoint.Commands.Any(command => command is ChooseShanheOpeningSkillCommand) &&
            !checkpoint.Options.EquippedTacticIds.Contains(OpeningInheritanceRewardId, StringComparer.Ordinal))
            candidates.AddRange(candidates.ToArray().Select(WithoutOpeningInheritance));
        if (!checkpoint.Commands.Any(command => command is SkipShanheRewardCommand))
        {
            candidates.AddRange(candidates.ToArray().Select(candidate => candidate with
                { Nodes = candidate.Nodes.Select(node => node with { RewardSkipGold = 0 }).ToArray() }));
        }
        return candidates.FirstOrDefault(candidate => string.Equals(ShanheCampaignContent.GetContentHash(candidate),
            checkpoint.CampaignHash, StringComparison.Ordinal)) is { } matching
            ? ShanheCampaignContent.FreezeAndValidate(matching)
            : throw new ArgumentException("Campaign checkpoint content fingerprint does not match.", nameof(checkpoint));
    }

    public static string EditionIdForCampaign(string campaignId, int difficulty)
    {
        if (difficulty is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(difficulty));
        var suffix = difficulty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return campaignId == "shanhe:zhuhou-local-" + suffix ? LocalEditionId
            : campaignId == "shanhe:zhuhou-2026-" + suffix ? Returning2026EditionId
            : throw new ArgumentException("The campaign edition and difficulty do not match.", nameof(campaignId));
    }

    private static ShanheCampaignDefinition CreateReturning2026Definition(int difficulty)
    {
        var original = CreateDefinition(difficulty);
        ShanheRewardDefinition[] teamRewards =
        [
            new("tactic-jiban-2026", "战法·羁绊")
            { TacticSkillId = "shanhe:jiban-2026", Description = "2026稀有战法：每名其他存活队友令你的出杀次数+2；队友死亡后即时重算。不占额外技能槽。" },
            new("tactic-duichu-2026", "战法·队·初")
            { TacticSkillId = "shanhe:duichu-2026", Description = "2026普通战法：开战时其他队友各真实摸2张牌，可触发获得牌技能。固定起手不被替换，不占额外技能槽。" },
            new("tactic-jingyu-moulue-2-2026", "战法·精于谋略 II")
            { TacticSkillId = "shanhe:jingyu-moulue-2-2026", Description = "2026战法：手牌少于6时，自己使用杀伤害+1。可重复获得，各份独立结算。不占额外技能槽；品级未核，商店定价为本地编排。" },
            new("tactic-duipai-2026", "战法·队·牌")
            { TacticSkillId = "shanhe:duipai-2026", Description = "2026队友战法：其他公开同队存活角色的摸牌阶段摸牌数+2；不增加你的摸牌或技能摸牌。可重复叠加，不占技能槽。" },
            new("tactic-duijia-2026", "战法·队·甲")
            { TacticSkillId = "shanhe:duijia-2026", Description = "2026队友战法：其他公开同队角色每场开战获得3点初始护甲。逐点代付伤害，不取消伤害后技能和铁索传导；可重复叠加，不占技能槽。" }
        ];
        var nodes = original.Nodes.Select(node => node.Id != "hulao" ? node : node with
        {
            Name = "第一章 · 虎牢 · 吕布与张辽",
            Kind = ShanheNodeKind.BossBattle,
            StoryText = "虎牢关前，两支敌军分守关隘。你可能迎战神鬼无前的吕布，也可能遭遇文远之威张辽。\n吕布形态按已核实面板登场，风篁同行助战；张辽采用当前公开配置的突袭与破军基础技能。遭遇分布与成长为本地编排。",
            Description = "神鬼无前或文远之威基础形态；本地直接登场与成长。未假定未知变身条件，也不把张辽基础形态当作完整高难度配置。",
            Encounters = WithS5ZhangliaoEncounter(node.Encounters!.Select(encounter => encounter with
            {
                Id = encounter.Id + ":shengui-2026",
                Name = "神鬼无前 · 专属形态",
                Enemies = encounter.Enemies.Select((enemy, index) => index == 0
                    ? enemy with { GeneralId = StandardShanheBossPackage.ShenguiLvbuId } : enemy).ToArray(),
                Allies = [new(StandardShanheBossPackage.FenghuangId)
                {
                    StartingHand = [new(CardKind.Slash, Suit.Heart, 5), new(CardKind.Dodge, Suit.Heart, 13),
                        new(CardKind.ArrowBarrage, Suit.Heart, 1), new(CardKind.Alcohol, Suit.Spade, 3)]
                }]
            }), difficulty)
        }).Select(node => node with
        {
            RewardIds = node.Kind is ShanheNodeKind.Battle or ShanheNodeKind.EliteBattle or ShanheNodeKind.BossBattle
                ? (node.RewardIds ?? []).Concat(teamRewards.Select(reward => reward.Id)).ToArray() : node.RewardIds,
            Choices = node.Kind == ShanheNodeKind.Shop
                ? (node.Choices ?? []).Concat(new ShanheNodeChoiceDefinition[]
                {
                    new("buy-jiban-2026", "战法·羁绊 · 100钱", 100, RewardId: "tactic-jiban-2026", LeavesNode: false),
                    new("buy-duichu-2026", "战法·队·初 · 60钱", 60, RewardId: "tactic-duichu-2026", LeavesNode: false),
                    new("buy-jingyu-moulue-2-2026", "战法·精于谋略 II · 100钱", 100, RewardId: "tactic-jingyu-moulue-2-2026", LeavesNode: false),
                    new("buy-duipai-2026", "战法·队·牌 · 本地价100钱", 100, RewardId: "tactic-duipai-2026", LeavesNode: false),
                    new("buy-duijia-2026", "战法·队·甲 · 本地价100钱", 100, RewardId: "tactic-duijia-2026", LeavesNode: false)
                }).ToArray() : node.Choices
        }).ToArray();
        return WithChapterRewards(original with
        {
            Id = "shanhe:zhuhou-2026-" + difficulty.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Name = "山河图 · 诸侯伐董（2026专属形态·本地编排）",
            Nodes = nodes,
            Rewards = original.Rewards.Concat(teamRewards).ToArray(),
            Description = Editions.Single(edition => edition.Id == Returning2026EditionId).Description,
            IntroText = original.IntroText + "\n\n本版加入2026羁绊、队·初，以及可重复获取的精于谋略II、队·牌、队·甲，可从征程奖励取得；队初使用真正的开局摸牌，可触发弘德等获得牌技能。队牌使其他存活队友的摸牌阶段摸牌数每份增加2；队甲在每场开战时使其他队友每份获得3点护甲。护甲逐点代付伤害，伤害后技能和属性传导仍按完整伤害结算。精于谋略II按伤害时的当前手牌判断少于6，各份独立增加杀伤害；该时序和替换层沿本地实现。虎牢路线由风篁以原版固定起手助战神鬼无前吕布。其神躯、动态无双、抚悼和修好按核实版本实现；路线、同行场次、商店价格、难度成长和直接登场仍为本地编排。"
        });
    }
}
