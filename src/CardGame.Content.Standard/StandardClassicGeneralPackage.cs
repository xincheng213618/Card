using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>
/// Opt-in classic identity roster. The legacy standard and demo expansion
/// packages remain unchanged so their checkpoints keep the original content
/// hashes and v1-v9 behavior.
/// </summary>
public sealed class StandardClassicGeneralPackage : IGameContentPackage
{
    private readonly Version _version;

    public StandardClassicGeneralPackage(bool legacyRoster = false)
        : this(legacyRoster ? new Version(1, 0, 0) : new Version(1, 20, 0))
    {
    }

    public StandardClassicGeneralPackage(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version != new Version(1, 0, 0) &&
            version != new Version(1, 1, 0) &&
            version != new Version(1, 2, 0) &&
            version != new Version(1, 3, 0) &&
            version != new Version(1, 4, 0) &&
            version != new Version(1, 5, 0) &&
            version != new Version(1, 6, 0) &&
            version != new Version(1, 7, 0) &&
            version != new Version(1, 8, 0) &&
            version != new Version(1, 9, 0) &&
            version != new Version(1, 10, 0) &&
            version != new Version(1, 11, 0) &&
            version != new Version(1, 12, 0) &&
            version != new Version(1, 13, 0) &&
            version != new Version(1, 14, 0) &&
            version != new Version(1, 15, 0) &&
            version != new Version(1, 16, 0) &&
            version != new Version(1, 17, 0) &&
            version != new Version(1, 18, 0) &&
            version != new Version(1, 19, 0) &&
            version != new Version(1, 20, 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "Supported classic-general package versions are 1.0.0 through 1.20.0.");
        }

        _version = version;
        Manifest = new PackageManifest(
            Id: "standard-classic-generals",
            Version: version,
            Dependencies: [new PackageDependency("standard-rescue-skills", new Version(1, 0, 0))]);
    }

    public PackageManifest Manifest { get; }

    public void Register(IContentRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddSkill(new ContentSkillDefinition(
            "classic:feedback",
            "反馈",
            "受到伤害后，你可以获得伤害来源的一张牌。",
            SkillKind.Feedback));
        if (_version >= new Version(1, 1, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:tiandu",
                "天妒",
                "当你的判定牌生效后，你可以获得此牌。",
                SkillKind.Tiandu));
        }
        if (_version >= new Version(1, 2, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:fanjian",
                "反间",
                "出牌阶段限一次，你可以令一名其他角色选择一种花色，令其获得并展示你的一张随机手牌；若花色不同，你对其造成1点伤害。",
                SkillKind.Fanjian));
        }
        if (_version >= new Version(1, 3, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:guanxing",
                "观星",
                "准备阶段，你可以观看牌堆顶的X张牌（X为存活角色数且至多为5），然后以任意顺序置于牌堆顶或牌堆底。",
                SkillKind.Guanxing));
        }
        if (_version >= new Version(1, 4, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:hujia",
                "护驾",
                "主公技，当你需要使用或打出【闪】时，你可以令其他魏势力角色依次选择是否打出一张【闪】；视为由你使用或打出。",
                SkillKind.Hujia));
        }
        if (_version >= new Version(1, 5, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:jijiang",
                "激将",
                "主公技，当你需要使用或打出【杀】时，你可以令其他蜀势力角色依次选择是否打出一张【杀】；视为由你使用或打出。",
                SkillKind.Jijiang));
        }
        if (_version >= new Version(1, 6, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:jiuyuan",
                "救援",
                "主公技，锁定技，其他吴势力角色对处于濒死状态的你使用的【桃】回复的体力+1。",
                SkillKind.Jiuyuan));
        }
        if (_version >= new Version(1, 8, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:qixi",
                "奇袭",
                "你可以将一张黑色牌当【过河拆桥】使用。",
                SkillKind.Qixi));
        }
        if (_version >= new Version(1, 9, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:keji",
                "克己",
                "若你未于本回合出牌阶段使用或打出过【杀】，你可以跳过弃牌阶段。",
                SkillKind.Keji));
        }
        if (_version >= new Version(1, 10, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:tuxi",
                "突袭",
                "摸牌阶段，你可以改为获得至多两名其他角色的各一张手牌。",
                SkillKind.Tuxi));
        }
        if (_version >= new Version(1, 11, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:luoyi",
                "裸衣",
                "摸牌阶段，你可以少摸一张牌，若如此做，每当你于此回合内使用【杀】或【决斗】对目标角色造成伤害时，此伤害+1。",
                SkillKind.Luoyi));
        }
        if (_version >= new Version(1, 12, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:qiangxi",
                "强袭",
                "出牌阶段限一次，你可以失去1点体力或弃置一张武器牌，并选择你攻击范围内的一名其他角色，对其造成1点伤害。",
                SkillKind.Qiangxi));
        }
        if (_version >= new Version(1, 13, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:duanliang",
                "断粮",
                "你可以将一张黑色基本牌或黑色装备牌当【兵粮寸断】使用；你可以对距离为2的角色使用【兵粮寸断】。",
                SkillKind.Duanliang));
        }
        if (_version >= new Version(1, 14, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:luoshen",
                "洛神",
                "准备阶段开始时，你可以进行判定，若结果为黑色，你可以再次进行判定，直到出现红色的结果，然后你获得所有生效后的黑色判定牌。",
                SkillKind.Luoshen));
            builder.AddSkill(new ContentSkillDefinition(
                "classic:qingguo",
                "倾国",
                "你可以将1张黑色手牌当【闪】使用或打出。",
                SkillKind.Qingguo));
        }
        if (_version >= new Version(1, 15, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:jizhi",
                "集智",
                "每当你使用普通锦囊牌时，你可以摸一张牌。",
                SkillKind.Jizhi));
        }
        if (_version >= new Version(1, 16, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:tieqi",
                "铁骑",
                "每当你使用【杀】指定一名目标角色后，你可以进行判定，若结果为红色，该角色不能使用【闪】响应此【杀】。",
                SkillKind.Tieqi));
        }
        if (_version >= new Version(1, 17, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:liegong",
                "烈弓",
                "当你于出牌阶段内使用【杀】指定一个目标后，若该角色的手牌数不小于你的体力值或不大于你的攻击范围，则你可以令其不能使用【闪】响应此【杀】。",
                SkillKind.Liegong));
        }
        if (_version >= new Version(1, 18, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:kuanggu",
                "狂骨",
                "锁定技，当你对距离1以内的一名角色造成1点伤害后，你回复1点体力。",
                SkillKind.Kuanggu));
        }
        if (_version >= new Version(1, 19, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:wushuang",
                "无双",
                "锁定技，你使用的【杀】需两张【闪】才能抵消；与你【决斗】的角色每次需打出两张【杀】。",
                SkillKind.Wushuang));
        }
        if (_version >= new Version(1, 20, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:paoxiao",
                "咆哮",
                "锁定技，你使用【杀】无次数限制。",
                SkillKind.Paoxiao));
        }

        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:liu-bei",
            "刘备",
            "liu_bei",
            "standard:rende",
            "shu",
            BaseHp: 4,
            AdditionalSkillIds: _version >= new Version(1, 5, 0)
                ? ["classic:jijiang"]
                : null));
        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:sun-quan",
            "孙权",
            "sun_quan",
            "standard:zhiheng",
            "wu",
            BaseHp: 4,
            AdditionalSkillIds: _version >= new Version(1, 6, 0)
                ? ["classic:jiuyuan"]
                : null));
        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:sima-yi",
            "司马懿",
            "sima_yi",
            "classic:feedback",
            "wei",
            BaseHp: 3,
            AdditionalSkillIds: ["standard:guicai"]));
        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:xiahou-dun",
            "夏侯惇",
            "xiahou_dun",
            "standard:ganglie",
            "wei",
            BaseHp: 4));
        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:hua-tuo",
            "华佗",
            "hua_tuo",
            "standard:qingnang",
            "qun",
            BaseHp: 3,
            AdditionalSkillIds: ["standard:jijiu"]));
        if (_version >= new Version(1, 1, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:guo-jia",
                "郭嘉",
                "guo_jia",
                "classic:tiandu",
                "wei",
                BaseHp: 3,
                AdditionalSkillIds: ["standard:yiji"]));
        }
        if (_version >= new Version(1, 2, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhou-yu",
                "周瑜",
                "zhou_yu",
                "standard:yingzi",
                "wu",
                BaseHp: 3,
                AdditionalSkillIds: ["classic:fanjian"]));
        }
        if (_version >= new Version(1, 3, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhuge-liang",
                "诸葛亮",
                "zhuge_liang",
                "classic:guanxing",
                "shu",
                BaseHp: 3,
                AdditionalSkillIds: ["standard:kongcheng"]));
        }
        if (_version >= new Version(1, 4, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:cao-cao",
                "曹操",
                "cao_cao",
                "standard:jianxiong",
                "wei",
                BaseHp: 4,
                AdditionalSkillIds: ["classic:hujia"]));
        }
        if (_version >= new Version(1, 7, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:huang-gai",
                "黄盖",
                "huang_gai",
                "standard:kujin",
                "wu",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 8, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:gan-ning",
                "甘宁",
                "gan_ning",
                "classic:qixi",
                "wu",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 9, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:lu-meng",
                "吕蒙",
                "lu_meng",
                "classic:keji",
                "wu",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 10, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhang-liao",
                "张辽",
                "zhang_liao",
                "classic:tuxi",
                "wei",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 11, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:xu-chu",
                "许褚",
                "xu_chu",
                "classic:luoyi",
                "wei",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 12, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:dian-wei",
                "典韦",
                "dian_wei",
                "classic:qiangxi",
                "wei",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 13, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:xu-huang",
                "徐晃",
                "xu_huang",
                "classic:duanliang",
                "wei",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 14, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhen-ji",
                "甄姬",
                "zhen_ji",
                "classic:luoshen",
                "wei",
                BaseHp: 3,
                AdditionalSkillIds: ["classic:qingguo"]));
        }
        if (_version >= new Version(1, 15, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:huang-yueying",
                "黄月英",
                "huang_yueying",
                "classic:jizhi",
                "shu",
                BaseHp: 3,
                AdditionalSkillIds: ["standard:qicai"]));
        }
        if (_version >= new Version(1, 16, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:ma-chao",
                "马超",
                "ma_chao",
                "classic:tieqi",
                "shu",
                BaseHp: 4,
                AdditionalSkillIds: ["standard:mashu"]));
        }
        if (_version >= new Version(1, 17, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:huang-zhong",
                "黄忠",
                "huang_zhong",
                "classic:liegong",
                "shu",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 18, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:wei-yan",
                "魏延",
                "wei_yan",
                "classic:kuanggu",
                "shu",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 19, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:lu-bu",
                "吕布",
                "lu_bu",
                "classic:wushuang",
                "qun",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 20, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhang-fei",
                "张飞",
                "zhang_fei",
                "classic:paoxiao",
                "shu",
                BaseHp: 4));
        }

        var generalPoolIds = _version switch
        {
            { Major: 1, Minor: 0 } => LegacyClassicGeneralIds,
            { Major: 1, Minor: 1 } => TianduClassicGeneralIds,
            { Major: 1, Minor: 2 } => FanjianClassicGeneralIds,
            { Major: 1, Minor: 3 } => GuanxingClassicGeneralIds,
            { Major: 1, Minor: 4 } => PreHuangGaiClassicGeneralIds,
            { Major: 1, Minor: 5 } => PreHuangGaiClassicGeneralIds,
            { Major: 1, Minor: 6 } => PreHuangGaiClassicGeneralIds,
            { Major: 1, Minor: 7 } => PreGanNingClassicGeneralIds,
            { Major: 1, Minor: 8 } => PreLuMengClassicGeneralIds,
            { Major: 1, Minor: 9 } => PreZhangLiaoClassicGeneralIds,
            { Major: 1, Minor: 10 } => PreXuChuClassicGeneralIds,
            { Major: 1, Minor: 11 } => PreDianWeiClassicGeneralIds,
            { Major: 1, Minor: 12 } => PreXuHuangClassicGeneralIds,
            { Major: 1, Minor: 13 } => PreZhenJiClassicGeneralIds,
            { Major: 1, Minor: 14 } => PreHuangYueyingClassicGeneralIds,
            { Major: 1, Minor: 15 } => PreMaChaoClassicGeneralIds,
            { Major: 1, Minor: 16 } => PreHuangZhongClassicGeneralIds,
            { Major: 1, Minor: 17 } => PreWeiYanClassicGeneralIds,
            { Major: 1, Minor: 18 } => PreLuBuClassicGeneralIds,
            { Major: 1, Minor: 19 } => PreZhangFeiClassicGeneralIds,
            _ => ClassicGeneralIds
        };

        builder.AddMode(new ContentModeDefinition(
            Id: "identity:classic-8",
            Name: "八人经典身份（正式武将首批）",
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
            GeneralPoolIds: generalPoolIds));
        builder.AddMode(new ContentModeDefinition(
            Id: "identity:classic-5",
            Name: "五人经典身份（正式武将首批）",
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
            GeneralPoolIds: generalPoolIds));
    }

    internal static IReadOnlyList<string> ClassicGeneralIds { get; } =
    [
        "classic:liu-bei",
        "classic:sun-quan",
        "classic:sima-yi",
        "classic:xiahou-dun",
        "classic:hua-tuo",
        "classic:cao-cao",
        "classic:zhang-liao",
        "classic:xu-chu",
        "classic:dian-wei",
        "classic:xu-huang",
        "classic:zhen-ji",
        "classic:huang-yueying",
        "classic:ma-chao",
        "classic:huang-zhong",
        "classic:wei-yan",
        "classic:lu-bu",
        "classic:huang-gai",
        "classic:gan-ning",
        "classic:lu-meng",
        "classic:zhang-fei",
        "classic:zhou-yu",
        "classic:zhuge-liang",
        "standard:guan-yu",
        "standard:zhao-yun",
        "classic:guo-jia"
    ];

    internal static IReadOnlyList<string> PreZhangFeiClassicGeneralIds { get; } =
    [
        .. ClassicGeneralIds.Select(id => id == "classic:zhang-fei" ? "standard:zhang-fei" : id)
    ];

    internal static IReadOnlyList<string> PreLuBuClassicGeneralIds { get; } =
    [
        .. PreZhangFeiClassicGeneralIds.Where(id => id != "classic:lu-bu")
    ];

    internal static IReadOnlyList<string> PreWeiYanClassicGeneralIds { get; } =
    [
        .. PreLuBuClassicGeneralIds.Where(id => id != "classic:wei-yan")
    ];

    internal static IReadOnlyList<string> PreHuangZhongClassicGeneralIds { get; } =
    [
        .. PreWeiYanClassicGeneralIds.Where(id => id != "classic:huang-zhong")
    ];

    internal static IReadOnlyList<string> PreMaChaoClassicGeneralIds { get; } =
    [
        .. PreHuangZhongClassicGeneralIds.Where(id => id != "classic:ma-chao")
    ];

    internal static IReadOnlyList<string> PreHuangYueyingClassicGeneralIds { get; } =
    [
        .. PreMaChaoClassicGeneralIds.Where(id => id != "classic:huang-yueying")
    ];

    internal static IReadOnlyList<string> PreZhenJiClassicGeneralIds { get; } =
    [
        .. PreHuangYueyingClassicGeneralIds.Where(id => id != "classic:zhen-ji")
    ];

    internal static IReadOnlyList<string> PreXuHuangClassicGeneralIds { get; } =
    [
        .. PreZhenJiClassicGeneralIds.Where(id => id != "classic:xu-huang")
    ];

    internal static IReadOnlyList<string> PreDianWeiClassicGeneralIds { get; } =
    [
        .. PreXuHuangClassicGeneralIds.Where(id => id != "classic:dian-wei")
    ];

    internal static IReadOnlyList<string> PreXuChuClassicGeneralIds { get; } =
    [
        .. PreDianWeiClassicGeneralIds.Where(id => id != "classic:xu-chu")
    ];

    internal static IReadOnlyList<string> PreZhangLiaoClassicGeneralIds { get; } =
    [
        .. PreXuChuClassicGeneralIds.Where(id => id != "classic:zhang-liao")
    ];

    internal static IReadOnlyList<string> PreLuMengClassicGeneralIds { get; } =
    [
        .. PreZhangLiaoClassicGeneralIds.Where(id => id != "classic:lu-meng")
    ];

    internal static IReadOnlyList<string> PreGanNingClassicGeneralIds { get; } =
    [
        .. PreLuMengClassicGeneralIds.Where(id => id != "classic:gan-ning")
    ];

    internal static IReadOnlyList<string> PreHuangGaiClassicGeneralIds { get; } =
    [
        .. PreGanNingClassicGeneralIds.Where(id => id != "classic:huang-gai")
    ];

    internal static IReadOnlyList<string> GuanxingClassicGeneralIds { get; } =
    [
        .. PreHuangGaiClassicGeneralIds.Select(id => id == "classic:cao-cao" ? "standard:cao-cao" : id)
    ];

    internal static IReadOnlyList<string> FanjianClassicGeneralIds { get; } =
    [
        .. GuanxingClassicGeneralIds.Select(id => id == "classic:zhuge-liang" ? "standard:zhuge-liang" : id)
    ];

    internal static IReadOnlyList<string> TianduClassicGeneralIds { get; } =
    [
        .. FanjianClassicGeneralIds.Select(id => id == "classic:zhou-yu" ? "standard:zhou-yu" : id)
    ];

    internal static IReadOnlyList<string> LegacyClassicGeneralIds { get; } =
    [
        .. TianduClassicGeneralIds.Select(id => id == "classic:guo-jia" ? "standard:guo-jia" : id)
    ];
}
