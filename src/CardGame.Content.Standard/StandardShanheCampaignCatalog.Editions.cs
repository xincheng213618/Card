using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public sealed record ShanheCampaignEdition(string Id, string Name, string Description)
{
    public string? DefaultGodFactionId { get; init; }
}

public static partial class StandardShanheCampaignCatalog
{
    public const string LocalEditionId = "lords-dong-local";
    public const string Returning2026EditionId = "lords-dong-2026";
    public const string CurrentFarStrikeSkillId = "shanhe:current-yuanji-2129";
    public const string CurrentFarStrikeRewardId = "tactic-yuanji-current-2129";
    public const string S5ZaiqiSkillId = "shanhe:s5-zaiqi-887";
    public const string S5ZaiqiRewardId = "codex-55";
    public const string S5KanpoSkillId = "boundary:kanpo-current";
    public const string S5KanpoRewardId = "codex-48";
    public const string CurrentJinnangJiSkillId = "shanhe:current-jinnangji-2011";
    public const string CurrentJinnangJiRewardId = "tactic-jinnangji-current-2011";
    public const string CurrentDuoduoYishanSkillId = "shanhe:current-duoduo-yishan-2082";
    public const string CurrentDuoduoYishanRewardId = "tactic-duoduo-yishan-current-2082";
    public const string CurrentDuoduoYishan2SkillId = "shanhe:current-duoduo-yishan-2083";
    public const string CurrentDuoduoYishan2RewardId = "tactic-duoduo-yishan-current-2083";
    public const string CurrentDuoduoYishan3SkillId = "shanhe:current-duoduo-yishan-2084";
    public const string CurrentDuoduoYishan3RewardId = "tactic-duoduo-yishan-current-2084";
    public const string CurrentYaoliSkillId = "shanhe:current-yaoli-2319";
    public const string CurrentYaoliRewardId = "tactic-yaoli-current-2319";
    public const string CurrentYaoli2SkillId = "shanhe:current-yaoli-2320";
    public const string CurrentYaoli2RewardId = "tactic-yaoli-current-2320";
    public const string CurrentYaoli3SkillId = "shanhe:current-yaoli-2321";
    public const string CurrentYaoli3RewardId = "tactic-yaoli-current-2321";
    public const string CurrentYaoli4SkillId = "shanhe:current-yaoli-2322";
    public const string CurrentYaoli4RewardId = "tactic-yaoli-current-2322";
    public const string CurrentYaoli5SkillId = "shanhe:current-yaoli-2191";
    public const string CurrentYaoli5RewardId = "tactic-yaoli-current-2191";
    public const string CurrentYaoli6SkillId = "shanhe:current-yaoli-2192";
    public const string CurrentYaoli6RewardId = "tactic-yaoli-current-2192";
    public const string S5ShuangxiongSkillId = "boundary:shuangxiong-current";
    public const string S5ShuangxiongRewardId = "codex-114";

    public static IReadOnlyList<ShanheCampaignEdition> Editions { get; } = Array.AsReadOnly(new[]
    {
        new ShanheCampaignEdition(LocalEditionId, "诸侯伐董 · 2024主题本地版",
            "采用已核实的2024战法和技能版本；剧情、地图、敌军及难度数值为本地编排。"),
        new ShanheCampaignEdition(Returning2026EditionId, "诸侯伐董 · 2026专属形态",
            "加入2026羁绊、队·初，以及可重复获得的精于谋略II、队·牌、队·甲。虎牢可遇风篁助战的神鬼无前吕布，或文远之威张辽；本地第2档张辽增加灵动判定防伤，第3档再加入惶恐的摸牌与弃牌数量翻倍；洛阳加入酒池、肉林、崩坏与逐点暴虐的独立董卓形态，以及当前全站配置的董旻技能形态：本地第1档仅盾弓，第2档起增加篡朝，接受发动后转换阴阳并令此次用牌的牌效果整体无效。董旻的原版关卡和阶段归属未核实，独立投放与其他遭遇分布、联动顺序及成长均为本地编排。另可遇到仅动态无双与专版利驭的霸戟雄英吕布：整区获牌后，按实际获得数量使用原生决斗；此形态原版属于崤函道，本地独立放入洛阳，另加入与闭月仙伶貂蝉同行的双敌候选：离间使用不能被无懈的真实决斗，专版闭月空手补至手牌上限，否则获得流离直到下一本人回合。另有凤仪情仇三敌阵容：覆銮凶影董卓以10/15体力登场，黑色手牌可作酒且不限次数，酒杀造成伤害后永久失去崩坏并获得已损失体力值加一张的再起；肉林使所有其他角色的双向杀均需两闪，盗书可猜花色取牌。三敌使用原始基础体力与技能，地图位置和孙坚同行为本地编排。神武将出征默认按群势力参与战斗，技能改势力仍按实际结果结算。另加入普通战法队友·杀，使其他队友每个出牌阶段可用杀次数+1。当前公开S5的远击技按最终距离大于1增加伤害，不限于杀；山河册节点35可研习，本地每次征程限取一份。") { DefaultGodFactionId = "qun" }
    });

    public static ShanheCampaignDefinition CreateDefinition(int difficulty, string editionId) =>
        WithStartingResourcesContract(CreateCurrentBaseDefinition(difficulty, editionId));

    private static ShanheCampaignDefinition CreateCurrentBaseDefinition(int difficulty, string editionId) => editionId switch
    {
        LocalEditionId => CreateLegacyLocalDefinition(difficulty),
        Returning2026EditionId => WithS5CourtGroupEncounters(CreateS5RegionalDefinition(difficulty)),
        _ => throw new ArgumentException("Unknown Shanhe campaign edition.", nameof(editionId))
    };

    private static ShanheCampaignDefinition CreateS5RegionalDefinition(int difficulty) =>
        WithS5RegionalGroupEncounters(CreateS5LiGuoDefinition(difficulty));

    private static ShanheCampaignDefinition CreateS5LiGuoDefinition(int difficulty) =>
        WithS5LiGuoBossEncounters(WithS5LordGroupEncounters(WithS5HebeiBossEncounters(WithS5StrategyBossEncounters(
            WithS5CoalitionBossEncounters(WithS5ThreeBossEncounters(CreateReturning2026Definition(difficulty)))))));

    public static ShanheCampaignDefinition CreateDefinitionForCheckpoint(ShanheCampaignCheckpoint checkpoint, int difficulty)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(checkpoint.Options);
        ArgumentNullException.ThrowIfNull(checkpoint.Commands);
        var editionId = EditionIdForCampaign(checkpoint.CampaignId, difficulty);
        var definition = checkpoint.Options.UseCodexRewardPool
            ? CreateCodexDefinition(difficulty, editionId) : CreateDefinition(difficulty, editionId);
        var roster = checkpoint.PlayerGeneralIds ?? LegacyPlayerGeneralIds;
        var available = GetPlayerGeneralIds().ToHashSet(StringComparer.Ordinal);
        if (roster.Count is 0 or > 2048 || roster.Any(id => id is null || !available.Contains(id)))
            throw new ArgumentException("Campaign checkpoint player roster is unavailable.", nameof(checkpoint));
        ShanheCampaignDefinition WithSavedRoster(ShanheCampaignDefinition source) =>
            ShanheCampaignContent.FreezeAndValidate(source with { PlayerGeneralIds = roster });
        definition = WithSavedRoster(definition);
        if (string.Equals(ShanheCampaignContent.GetContentHash(definition), checkpoint.CampaignHash, StringComparison.Ordinal))
            return definition;
        // Only the immediately preceding expedition content remains supported.
        if (editionId == Returning2026EditionId)
        {
            var previous = CreateS5RegionalDefinition(difficulty);
            if (checkpoint.Options.UseCodexRewardPool) previous = WithCodexRewards(previous, editionId);
            previous = WithSavedRoster(WithStartingResourcesContract(previous));
            if (string.Equals(ShanheCampaignContent.GetContentHash(previous), checkpoint.CampaignHash, StringComparison.Ordinal))
                return previous;
        }
        throw new ArgumentException("Campaign checkpoint content fingerprint does not match a supported definition.", nameof(checkpoint));
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
        var original = CreateLegacyLocalDefinition(difficulty);
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
            new("tactic-duisha-2026", "战法·队友·杀")
            { TacticSkillId = "shanhe:duisha-2026", Description = "2026普通战法：其他存活的公开同队角色每个出牌阶段可用杀次数+1；持有者自身不受益，不占技能槽。本次征程限获得一份。" },
            new(CurrentFarStrikeRewardId, "战法·远击技")
            { TacticSkillId = CurrentFarStrikeSkillId, Description = "当前S5配置普通战法：造成伤害时，若你到目标的最终距离大于1，此伤害+1；不限于杀或牌伤害。距离固定为1时不能触发。不占技能槽，本次征程限获得一份；铁索沿本地伤害流程逐段判断。" },
            new(CurrentJinnangJiRewardId, "战法·锦囊计")
            { TacticSkillId = CurrentJinnangJiSkillId, Description = "稀有战法：手牌上限增加本回合本人摸牌阶段实际摸牌总数的一半，合计后向下取整。额外摸牌计入，突袭获牌与再起亮牌获得不计。不占额外技能槽，本次征程限获得一份。" },
            new(CurrentDuoduoYishanRewardId, "战法·多多益善")
            { TacticSkillId = CurrentDuoduoYishanSkillId, Description = "普通战法：每个回合，你第5次实际摸牌后摸1张牌。一次摸多张只算一次，获得牌不算摸牌。不占额外技能槽，本版每次征程限获得一份。" },
            new(CurrentDuoduoYishan2RewardId, "战法·多多益善Ⅱ")
            { TacticSkillId = CurrentDuoduoYishan2SkillId, Description = "稀有战法：每个回合，你第3次实际摸牌后摸1张牌。一次摸多张只算一次，获得牌不算摸牌。不占额外技能槽，本版每次征程限获得一份。" },
            new(CurrentDuoduoYishan3RewardId, "战法·多多益善Ⅲ")
            { TacticSkillId = CurrentDuoduoYishan3SkillId, Description = "传说战法：每个回合，你第1次实际摸牌后摸2张牌。一次摸多张只算一次，获得牌不算摸牌。不占额外技能槽，本版每次征程限获得一份。" },
            new(CurrentYaoliRewardId, "战法·药理")
            { TacticSkillId = CurrentYaoliSkillId, Description = "普通战法：每个回合，你首次回复体力时，额外回复1点。直接增加同次真实回复，仍受体力上限限制。不占额外技能槽，本版每次征程限获得一份。" },
            new(CurrentYaoli2RewardId, "战法·药理II")
            { TacticSkillId = CurrentYaoli2SkillId, Description = "稀有战法：每个回合，你前3次回复体力时，额外回复1点。一次回复多点仍只算一次。不占额外技能槽，本版每次征程限获得一份。" },
            new(CurrentYaoli3RewardId, "战法·药理III")
            { TacticSkillId = CurrentYaoli3SkillId, Description = "史诗战法：每个回合，你首次回复体力时，额外回复2点。直接增加同次真实回复，仍受体力上限限制。不占额外技能槽，本版每次征程限获得一份。" },
            new(CurrentYaoli4RewardId, "战法·药理IV")
            { TacticSkillId = CurrentYaoli4SkillId, Description = "传说战法：每个回合，你前2次回复体力时，额外回复2点。一次回复多点仍只算一次。不占额外技能槽，本版每次征程限获得一份。" },
            new(CurrentYaoli5RewardId, "战法·药理Ⅴ")
            { TacticSkillId = CurrentYaoli5SkillId, Description = "史诗战法：你回复体力时，额外回复1点。直接增加同次真实回复，仍受体力上限限制。不占额外技能槽，本版每次征程限获得一份。" },
            new(CurrentYaoli6RewardId, "战法·药理Ⅵ")
            { TacticSkillId = CurrentYaoli6SkillId, Description = "传说战法：你回复体力时，额外回复2点。直接增加同次真实回复，仍受体力上限限制。不占额外技能槽，本版每次征程限获得一份。" },
            new("tactic-duijia-2026", "战法·队·甲")
            { TacticSkillId = "shanhe:duijia-2026", Description = "2026队友战法：其他公开同队角色每场开战获得3点初始护甲。逐点代付伤害，不取消伤害后技能和铁索传导；可重复叠加，不占技能槽。" }
        ];
        var nodes = original.Nodes.Select(node => node.Id != "hulao" ? node : node with
        {
            Name = "第一章 · 虎牢 · 吕布与张辽",
            Kind = ShanheNodeKind.BossBattle,
            StoryText = "虎牢关前，两支敌军分守关隘。你可能迎战神鬼无前的吕布，也可能遭遇文远之威张辽。\n吕布形态按已核实面板登场，风篁同行助战；张辽采用当前配置的突袭与破军，本地第2档加入灵动，以真实判定的最终红色结果防止伤害；第3档再加入惶恐，翻倍摸牌计划和应弃牌量。常规加牌后惶恐翻倍，突袭再逐张扣减；该联动顺序、遭遇分布、三档技能选择与成长为本地编排。",
            Description = "神鬼无前或文远之威技能形态；张辽在本地第2档增加灵动，第3档再增加惶恐。本地直接登场、联动顺序与成长，原版完整难度和变身条件仍待补齐。",
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
        }).Select(node => node.Id == "luoyang" ? WithS5LuoyangEncounter(node, difficulty) : node).Select(node => node with
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
                    new("buy-duisha-2026", "战法·队友·杀 · 本地价200钱", 200, RewardId: "tactic-duisha-2026", LeavesNode: false),
                    new("buy-yuanji-current-2129", "战法·远击技 · 200钱", 200, RewardId: CurrentFarStrikeRewardId, LeavesNode: false),
                    new("buy-jinnangji-current-2011", "战法·锦囊计 · 300钱", 300, RewardId: CurrentJinnangJiRewardId, LeavesNode: false),
                    new("buy-duoduo-yishan-current-2082", "战法·多多益善 · 200钱", 200, RewardId: CurrentDuoduoYishanRewardId, LeavesNode: false),
                    new("buy-duoduo-yishan-current-2083", "战法·多多益善Ⅱ · 300钱", 300, RewardId: CurrentDuoduoYishan2RewardId, LeavesNode: false),
                    new("buy-duoduo-yishan-current-2084", "战法·多多益善Ⅲ · 700钱", 700, RewardId: CurrentDuoduoYishan3RewardId, LeavesNode: false),
                    new("buy-yaoli-current-2319", "战法·药理 · 200钱", 200, RewardId: CurrentYaoliRewardId, LeavesNode: false),
                    new("buy-yaoli-current-2320", "战法·药理II · 300钱", 300, RewardId: CurrentYaoli2RewardId, LeavesNode: false),
                    new("buy-yaoli-current-2321", "战法·药理III · 500钱", 500, RewardId: CurrentYaoli3RewardId, LeavesNode: false),
                    new("buy-yaoli-current-2322", "战法·药理IV · 700钱", 700, RewardId: CurrentYaoli4RewardId, LeavesNode: false),
                    new("buy-yaoli-current-2191", "战法·药理Ⅴ · 500钱", 500, RewardId: CurrentYaoli5RewardId, LeavesNode: false),
                    new("buy-yaoli-current-2192", "战法·药理Ⅵ · 700钱", 700, RewardId: CurrentYaoli6RewardId, LeavesNode: false),
                    new("buy-duijia-2026", "战法·队·甲 · 本地价100钱", 100, RewardId: "tactic-duijia-2026", LeavesNode: false)
                }).ToArray() : node.Choices
        }).ToArray();
        nodes = nodes.Select(node => node.Id == "hulao" ? WithS5HuaxiongEncounter(WithS5GaoshunEncounter(node, difficulty)) : node).ToArray();
        nodes = nodes.Select(node => node.Id == "dagu" ? WithS5YuduEncounter(WithS5TaishiciEncounter(node)) : node).ToArray();
        nodes = nodes.Select(node => node.Id == "huanyuan" ? WithS5YujinEncounter(WithS5CaocaoEncounter(node)) : node).ToArray();
        nodes = nodes.Select(node => node.Id == "luoyang" ? WithS5LiruEncounter(node) : node).ToArray();
        nodes = nodes.Select(node => node.Id == "guangcheng" ? WithS5CaoxingHighEncounters(WithS5ShiduEncounters(WithS5CaoxingEncounter(node), difficulty), difficulty) : node).ToArray();
        return WithCurrentSlashTactics(WithChapterRewards(original with
        {
            Id = "shanhe:zhuhou-2026-" + difficulty.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DefaultGodFactionId = Editions.Single(edition => edition.Id == Returning2026EditionId).DefaultGodFactionId,
            Name = "山河图 · 诸侯伐董（2026专属形态·本地编排）",
            Nodes = nodes,
            Rewards = original.Rewards.Concat(teamRewards).ToArray(),
            Description = Editions.Single(edition => edition.Id == Returning2026EditionId).Description,
            IntroText = original.IntroText + "\n\n本版加入2026羁绊、队·初，以及可重复获取的精于谋略II、队·牌、队·甲，可从征程奖励取得；队初使用真正的开局摸牌，可触发弘德等获得牌技能。队牌使其他存活队友的摸牌阶段摸牌数每份增加2；队甲在每场开战时使其他队友每份获得3点护甲。护甲逐点代付伤害，伤害后技能和属性传导仍按完整伤害结算。精于谋略II按伤害时的当前手牌判断少于6，各份独立增加杀伤害；该时序和替换层沿本地实现。虎牢可迎战风篁助战的神鬼无前吕布，其神躯、动态无双、抚悼、修好和风篁固定起手按核实版本实现；也可遇到采用当前配置突袭、破军的文远之威形态。张辽突袭没有手牌数量门槛，以真实摸牌预算选取目标；破军暂时移出的原牌在回合结束归还。本地第2档起张辽增加灵动：受到伤害时真实判定，最终红色结果防止该段伤害，不扣护甲；改判及取得判定牌仍按原生流程结算。第3档再加入惶恐：常规摸牌数量修正后翻倍，再由突袭逐张扣减；弃牌阶段应弃数量翻倍，手牌上限不变，受实际可弃手牌数限制。惶恐与其他数量技能的先后沿本地约定，专属装备仍待补齐。普通战法队友·杀使其他存活公开队友每个出牌阶段可用杀次数+1，普通、属性和转化杀共用额度；本次征程限获得一份。当前远击技使你对最终距离大于1的目标造成伤害时伤害+1，牌和技能伤害均可生效；近身等固定最终距离1会使其失效，铁索沿本地来源加伤流程逐段判断。此当前S5规则仅在本入口投放，山河册节点35可研习。本版另可遇到仅动态无双与专版利驭的霸戟雄英吕布：整区获牌后，按实际获得数量使用原生决斗；此形态原版属于崤函道，本地独立放入洛阳，另加入与闭月仙伶貂蝉同行的双敌候选：离间使用不能被无懈的真实决斗，专版闭月空手补至手牌上限，否则获得流离直到下一本人回合。另有凤仪情仇三敌阵容：覆銮凶影董卓以10/15体力登场，黑色手牌可作酒且不限次数，酒杀造成伤害后永久失去崩坏并获得已损失体力值加一张的再起；肉林使所有其他角色的双向杀均需两闪，盗书可猜花色取牌。三敌使用原始基础体力与技能，地图位置和孙坚同行为本地编排。神武将出征默认按群势力参与战斗，因此也可能触发董卓暴虐；后续原生改势力或化身声明按实际势力结算。洛阳采用酒池、肉林、崩坏与逐点暴虐的独立董卓形态；其他群角色造成伤害可能让董卓回复并获得判定牌，同队与敌队均如此。路线、遭遇分布、三档技能选择、同行场次、商店价格、难度成长和直接登场为本地编排。"
        }));
    }
}
