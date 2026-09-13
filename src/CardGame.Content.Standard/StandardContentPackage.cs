using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>
/// The first formal content package. It mirrors the already playable demo data
/// while using stable namespaced ids and no mutable process-global registration.
/// </summary>
public sealed class StandardContentPackage : IGameContentPackage
{
    public PackageManifest Manifest { get; } = new(
        Id: "standard",
        Version: new Version(1, 11, 0),
        Dependencies: []);

    public void Register(IContentRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddCard(new ContentCardDefinition(
            Id: "standard:slash",
            DisplayName: "杀",
            CategoryName: "基本牌",
            Description: "选择一名角色；其未打出闪则受到 1 点伤害。",
            LegacyKind: CardKind.Slash,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "targeted-damage"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:dodge",
            DisplayName: "闪",
            CategoryName: "基本牌",
            Description: "受到杀、火杀或雷杀时打出，抵消这次伤害。",
            LegacyKind: CardKind.Dodge,
            AiTags: new Dictionary<string, string>
            {
                ["response"] = "slash"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:peach",
            DisplayName: "桃",
            CategoryName: "基本牌",
            Description: "出牌阶段回复自己 1 点体力；濒死时可救援一名角色。",
            LegacyKind: CardKind.Peach,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "recover",
                ["response"] = "dying"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:duel",
            DisplayName: "决斗",
            CategoryName: "锦囊牌",
            Description: "选择一名角色；双方交替打出杀，未打出者受到 1 点伤害。",
            LegacyKind: CardKind.Duel,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "alternating-slash-response"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:draw_two",
            DisplayName: "无中生有",
            CategoryName: "锦囊牌",
            Description: "出牌阶段使用，摸两张牌。",
            LegacyKind: CardKind.DrawTwo,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "draw-two"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:barbarian_assault",
            DisplayName: "南蛮入侵",
            CategoryName: "锦囊牌",
            Description: "所有其他角色依次打出杀，否则受到 1 点伤害。",
            LegacyKind: CardKind.BarbarianAssault,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "group-slash-response"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:arrow_barrage",
            DisplayName: "万箭齐发",
            CategoryName: "锦囊牌",
            Description: "所有其他角色依次打出闪，否则受到 1 点伤害。",
            LegacyKind: CardKind.ArrowBarrage,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "group-dodge-response"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:peach_garden",
            DisplayName: "桃园结义",
            CategoryName: "锦囊牌",
            Description: "所有存活角色各回复 1 点体力。",
            LegacyKind: CardKind.PeachGarden,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "group-recovery"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:five_grains",
            DisplayName: "五谷丰登",
            CategoryName: "锦囊牌",
            Description: "公开展示牌堆顶牌；所有存活角色按座次各选择一张，余牌弃置。",
            LegacyKind: CardKind.FiveGrains,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "public-draft"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:dismantlement",
            DisplayName: "过河拆桥",
            CategoryName: "锦囊牌",
            Description: "选择一名其他角色；盲弃置其一张手牌，或弃置其一张公开装备/判定区牌。",
            LegacyKind: CardKind.Dismantlement,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "target-card-discard",
                ["target-zone"] = "hand-or-equipment-or-judgment"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:snatch",
            DisplayName: "顺手牵羊",
            CategoryName: "锦囊牌",
            Description: "选择一名距离为 1 的其他角色；盲取其一张手牌，或获得其一张公开装备/判定区牌。",
            LegacyKind: CardKind.Snatch,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "target-card-take",
                ["target-zone"] = "hand-or-equipment-or-judgment"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:nullification",
            DisplayName: "无懈可击",
            CategoryName: "锦囊牌",
            Description: "抵消一张锦囊牌的效果；无懈之间可以继续互相抵消。",
            LegacyKind: CardKind.Nullification,
            AiTags: new Dictionary<string, string>
            {
                ["response"] = "trick",
                ["action"] = "counter"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:iron_chain",
            DisplayName: "铁索连环",
            CategoryName: "锦囊牌",
            Description: "横置或重置一至两名其他存活角色；被火焰或雷电伤害时，连环角色会传导同量伤害。",
            LegacyKind: CardKind.IronChain,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "toggle-one-or-two-chain",
                ["damage"] = "propagate-fire-thunder"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:indulgence",
            DisplayName: "乐不思蜀",
            CategoryName: "锦囊牌",
            Description: "选择一名其他角色；其下个回合判定，若为红色则跳过出牌阶段。",
            LegacyKind: CardKind.Indulgence,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "delayed-judgment-skip-play"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:supply_shortage",
            DisplayName: "兵粮寸断",
            CategoryName: "锦囊牌",
            Description: "选择一名有手牌的其他角色；其下个回合判定，若为黑色则跳过摸牌阶段。",
            LegacyKind: CardKind.SupplyShortage,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "delayed-judgment-skip-draw"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:lightning",
            DisplayName: "闪电",
            CategoryName: "锦囊牌",
            Description: "置于自己的判定区；下个回合判定为黑桃 2 至 9 时受到 3 点雷电伤害，否则移至下一名存活角色的判定区。",
            LegacyKind: CardKind.Lightning,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "delayed-judgment-lightning",
                ["damage"] = "three-thunder-or-transfer"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:fire_slash",
            DisplayName: "火杀",
            CategoryName: "基本牌",
            Description: "选择一名角色；其未打出闪则受到 1 点火焰伤害。",
            LegacyKind: CardKind.FireSlash,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "attack-nature-fire"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:thunder_slash",
            DisplayName: "雷杀",
            CategoryName: "基本牌",
            Description: "选择一名角色；其未打出闪则受到 1 点雷电伤害。",
            LegacyKind: CardKind.ThunderSlash,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "attack-nature-thunder"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:alcohol",
            DisplayName: "酒",
            CategoryName: "基本牌",
            Description: "出牌阶段使用使本回合下一张杀伤害 +1；濒死时仅可自救 1 点体力。",
            LegacyKind: CardKind.Alcohol,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "slash-damage-boost"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:fire_attack",
            DisplayName: "火攻",
            CategoryName: "锦囊牌",
            Description: "选择一名有手牌的角色；其展示一张手牌，你弃置一张相同花色的手牌后对其造成 1 点火焰伤害。",
            LegacyKind: CardKind.FireAttack,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "private-reveal-same-suit-fire-damage"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:crossbow",
            DisplayName: "诸葛连弩",
            CategoryName: "装备牌",
            Description: "装备至武器槽；攻击范围 +1，出牌阶段使用杀不受次数限制。",
            LegacyKind: CardKind.Crossbow,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "equip-weapon",
                ["modifier"] = "unlimited-slash"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:bagua",
            DisplayName: "八卦阵",
            CategoryName: "装备牌",
            Description: "装备至防具槽；成为普通/火/雷杀的直接目标时可选择公开判定，红色判定牌视为闪。",
            LegacyKind: CardKind.BaguaFormation,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "equip-armor",
                ["response"] = "public-judgment-dodge"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:renwang_shield",
            DisplayName: "仁王盾",
            CategoryName: "装备牌",
            Description: "装备至防具槽；黑色杀不能对你使用。",
            LegacyKind: CardKind.RenwangShield,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "equip-armor",
                ["modifier"] = "block-black-slash"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:qinggang_sword",
            DisplayName: "青釭剑",
            CategoryName: "装备牌",
            Description: "装备至武器槽；你使用杀时无视目标的防具。",
            LegacyKind: CardKind.QinggangSword,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "equip-weapon",
                ["modifier"] = "ignore-armor"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:offensive_horse",
            DisplayName: "赤兔",
            CategoryName: "装备牌",
            Description: "装备至进攻坐骑槽；你到其他角色的战斗距离 -1。",
            LegacyKind: CardKind.OffensiveHorse,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "equip-offensive-horse",
                ["modifier"] = "outgoing-distance-minus-one"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:defensive_horse",
            DisplayName: "绝影",
            CategoryName: "装备牌",
            Description: "装备至防御坐骑槽；其他角色到你的战斗距离 +1。",
            LegacyKind: CardKind.DefensiveHorse,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "equip-defensive-horse",
                ["modifier"] = "incoming-distance-plus-one"
            }));
        builder.AddCard(new ContentCardDefinition(
            Id: "standard:jade_seal",
            DisplayName: "玉玺",
            CategoryName: "装备牌",
            Description: "装备至宝物槽；摸牌阶段额外摸一张牌。",
            LegacyKind: CardKind.JadeSeal,
            AiTags: new Dictionary<string, string>
            {
                ["action"] = "equip-treasure",
                ["modifier"] = "draw-plus-one"
            }));

        builder.AddSkill(new ContentSkillDefinition(
            "standard:none", "无", "演示版暂未启用技能。", SkillKind.None));
        builder.AddSkill(new ContentSkillDefinition(
            "standard:jianxiong", "奸雄", "受到杀造成的伤害后，获得这张杀。", SkillKind.Jianxiong));
        builder.AddSkill(new ContentSkillDefinition(
            "standard:feedback", "反馈", "受到伤害且伤害牌仍在处理区时，可选择发动并获得造成伤害的牌。", SkillKind.Feedback));
        builder.AddSkill(new ContentSkillDefinition(
            "standard:paoxiao", "咆哮", "出牌阶段使用杀没有次数限制。", SkillKind.Paoxiao));
        builder.AddSkill(new ContentSkillDefinition(
            "standard:yingzi", "英姿", "摸牌阶段额外摸一张牌。", SkillKind.Yingzi));
        builder.AddSkill(new ContentSkillDefinition(
            "standard:kongcheng", "空城", "没有手牌时不能成为杀的目标。", SkillKind.Kongcheng));
        builder.AddSkill(new ContentSkillDefinition(
            "standard:wusheng", "武圣", "红色牌可当作杀使用。", SkillKind.Wusheng));
        builder.AddSkill(new ContentSkillDefinition(
            "standard:longdan", "龙胆", "杀可当闪，闪可当杀使用。", SkillKind.Longdan));
        builder.AddSkill(new ContentSkillDefinition(
            "standard:yiji", "遗计", "受到伤害后摸两张牌，然后可将其中一张交给一名其他存活角色。", SkillKind.Yiji));
        builder.AddSkill(new ContentSkillDefinition(
            "standard:jieming", "节命", "受到伤害后，可令一名手牌数少于体力上限的角色摸牌至上限。", SkillKind.Jieming));
        builder.AddSkill(new ContentSkillDefinition(
            "standard:yuanhu", "援护", "其他角色受到伤害后，可弃置一张牌令其回复 1 点体力。", SkillKind.Yuanhu));
        builder.AddSkill(new ContentSkillDefinition(
            "standard:ganglie", "刚烈", "受到伤害后可进行判定；若为红色，伤害来源选择弃置两张手牌或受到 1 点伤害。", SkillKind.Ganglie));
        builder.AddSkill(new ContentSkillDefinition(
            "standard:guicai", "鬼才", "判定牌生效前，可弃置一张手牌替换之。", SkillKind.Guicai));

        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:cao-cao", "曹操", "cao_cao", "standard:jianxiong", "wei"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:zhang-fei", "张飞", "zhang_fei", "standard:paoxiao", "shu"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:zhou-yu", "周瑜", "zhou_yu", "standard:yingzi", "wu"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:zhuge-liang", "诸葛亮", "zhuge_liang", "standard:kongcheng", "shu"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:liu-bei", "刘备", "liu_bei", "standard:none", "shu"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:guan-yu", "关羽", "guan_yu", "standard:wusheng", "shu"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:zhao-yun", "赵云", "zhao_yun", "standard:longdan", "shu"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:sun-quan", "孙权", "sun_quan", "standard:none", "wu"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:hua-tuo", "华佗", "hua_tuo", "standard:feedback", "qun"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:guo-jia", "郭嘉", "guo_jia", "standard:yiji", "wei"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:xun-yu", "荀彧", "xun_yu", "standard:jieming", "wei"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:demo-yuanhu", "援护者", "supporter", "standard:yuanhu", "qun"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:demo-ganglie", "刚烈者", "ganglie", "standard:ganglie", "wei"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:demo-guicai", "鬼才者", "guicai", "standard:guicai", "wei"));

        builder.AddDeck(new ContentDeckRecipe(
            Id: "standard:basic-demo",
            Name: "基础牌演示牌堆",
            InitialHandSize: 4,
            DrawPerTurn: 2,
            Cards:
            [
                new ContentDeckCardCount("standard:slash", 18),
                new ContentDeckCardCount("standard:dodge", 18),
                new ContentDeckCardCount("standard:peach", 10),
                new ContentDeckCardCount("standard:duel", 4),
                new ContentDeckCardCount("standard:draw_two", 2),
                new ContentDeckCardCount("standard:barbarian_assault", 2),
                new ContentDeckCardCount("standard:arrow_barrage", 2),
                new ContentDeckCardCount("standard:peach_garden", 2),
                new ContentDeckCardCount("standard:five_grains", 2),
                new ContentDeckCardCount("standard:dismantlement", 2),
                new ContentDeckCardCount("standard:snatch", 2),
                new ContentDeckCardCount("standard:fire_slash", 2),
                new ContentDeckCardCount("standard:thunder_slash", 2),
                new ContentDeckCardCount("standard:alcohol", 2),
                new ContentDeckCardCount("standard:fire_attack", 2),
                new ContentDeckCardCount("standard:crossbow", 2),
                new ContentDeckCardCount("standard:bagua", 1),
                new ContentDeckCardCount("standard:offensive_horse", 1),
                new ContentDeckCardCount("standard:defensive_horse", 1),
                new ContentDeckCardCount("standard:jade_seal", 1),
                new ContentDeckCardCount("standard:qinggang_sword", 1),
                new ContentDeckCardCount("standard:nullification", 2),
                new ContentDeckCardCount("standard:iron_chain", 2),
                new ContentDeckCardCount("standard:indulgence", 2),
                new ContentDeckCardCount("standard:supply_shortage", 2),
                new ContentDeckCardCount("standard:lightning", 2),
                new ContentDeckCardCount("standard:renwang_shield", 1)
         ]));

        builder.AddMode(new ContentModeDefinition(
            Id: "identity:standard-8",
            Name: "八人身份局（演示）",
            MinPlayers: 8,
            MaxPlayers: 8,
            RoleCounts: new Dictionary<string, int>
            {
                [nameof(Role.Lord)] = 1,
                [nameof(Role.Loyalist)] = 2,
                [nameof(Role.Rebel)] = 4,
                [nameof(Role.Renegade)] = 1
            },
            DeckId: "standard:basic-demo",
            GeneralCandidateCount: 3,
            GeneralPoolIds: StandardGeneralIds));
        builder.AddMode(new ContentModeDefinition(
            Id: "identity:standard-5",
            Name: "五人身份局（演示）",
            MinPlayers: 5,
            MaxPlayers: 5,
            RoleCounts: new Dictionary<string, int>
            {
                [nameof(Role.Lord)] = 1,
                [nameof(Role.Loyalist)] = 1,
                [nameof(Role.Rebel)] = 2,
                [nameof(Role.Renegade)] = 1
            },
            DeckId: "standard:basic-demo",
            GeneralCandidateCount: 3,
            GeneralPoolIds: StandardGeneralIds));
    }

    internal static IReadOnlyList<string> StandardGeneralIds { get; } =
    [
        "standard:cao-cao",
        "standard:zhang-fei",
        "standard:zhou-yu",
        "standard:zhuge-liang",
        "standard:liu-bei",
        "standard:guan-yu",
        "standard:zhao-yun",
        "standard:sun-quan",
        "standard:hua-tuo",
        "standard:guo-jia",
        "standard:xun-yu",
        "standard:demo-yuanhu",
        "standard:demo-ganglie",
        "standard:demo-guicai"
    ];
}

public static class StandardContentRegistry
{
    public static ContentRegistry Create() =>
        ContentRegistry.Build(new StandardContentPackage());

    /// <summary>
    /// Builds the opt-in content variant used by the local WPF showcase. The
    /// original standard registry remains byte-for-byte compatible for old
    /// saves and replay fixtures.
    /// </summary>
    public static ContentRegistry CreateWithActiveSkills() =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage());

    /// <summary>
    /// Builds the active-skill showcase plus the separately versioned rescue
    /// extension. The original active-skill registry remains available for
    /// checkpoints that predate the Jijiu package.
    /// </summary>
    public static ContentRegistry CreateWithRescueSkills() =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage());

    public static ContentRegistry CreateWithTeamModes() =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardTeamModePackage());

    public static ContentRegistry CreateWithNationalWarLite(bool legacyVitals = false) =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardNationalWarLitePackage(legacyVitals));

    public static ContentRegistry CreateWithNationalWarAmbitious(bool legacyVitals = false) =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardNationalWarLitePackage(legacyVitals),
            new StandardNationalWarAmbitiousPackage());

    public static ContentRegistry CreateWithActiveSkillsAndTeamModes() =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(),
            new StandardTeamModePackage());

    public static ContentRegistry CreateWithActiveSkillsAndNationalWarLite(bool legacyVitals = false) =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(),
            new StandardNationalWarLitePackage(legacyVitals));

    public static ContentRegistry CreateWithActiveSkillsAndNationalWarAmbitious(bool legacyVitals = false) =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(),
            new StandardNationalWarLitePackage(legacyVitals),
            new StandardNationalWarAmbitiousPackage());

    public static ContentRegistry CreateWithRescueSkillsAndTeamModes() =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardTeamModePackage());

    public static ContentRegistry CreateWithRescueSkillsAndTeamModesAndNationalWarLite(bool legacyVitals = false) =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardTeamModePackage(),
            new StandardNationalWarLitePackage(legacyVitals));

    public static ContentRegistry CreateWithRescueSkillsAndNationalWarAmbitious(bool legacyVitals = false) =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardNationalWarLitePackage(legacyVitals),
            new StandardNationalWarAmbitiousPackage());

    public static ContentRegistry CreateWithActiveSkillsAndTeamModesAndNationalWarAmbitious(bool legacyVitals = false) =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(),
            new StandardTeamModePackage(),
            new StandardNationalWarLitePackage(legacyVitals),
            new StandardNationalWarAmbitiousPackage());

    public static ContentRegistry CreateWithTeamModesAndNationalWarAmbitious(bool legacyVitals = false) =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardTeamModePackage(),
            new StandardNationalWarLitePackage(legacyVitals),
            new StandardNationalWarAmbitiousPackage());

    public static ContentRegistry CreateWithRescueSkillsAndTeamModesAndNationalWarAmbitious(bool legacyVitals = false) =>
        ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardTeamModePackage(),
            new StandardNationalWarLitePackage(legacyVitals),
            new StandardNationalWarAmbitiousPackage());
}
