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
        : this(legacyRoster ? new Version(1, 0, 0) : new Version(1, 7, 0))
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
            version != new Version(1, 7, 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "Supported classic-general package versions are 1.0.0 through 1.7.0.");
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

        var generalPoolIds = _version switch
        {
            { Major: 1, Minor: 0 } => LegacyClassicGeneralIds,
            { Major: 1, Minor: 1 } => TianduClassicGeneralIds,
            { Major: 1, Minor: 2 } => FanjianClassicGeneralIds,
            { Major: 1, Minor: 3 } => GuanxingClassicGeneralIds,
            { Major: 1, Minor: 4 } => PreHuangGaiClassicGeneralIds,
            { Major: 1, Minor: 5 } => PreHuangGaiClassicGeneralIds,
            { Major: 1, Minor: 6 } => PreHuangGaiClassicGeneralIds,
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
        "classic:huang-gai",
        "standard:zhang-fei",
        "classic:zhou-yu",
        "classic:zhuge-liang",
        "standard:guan-yu",
        "standard:zhao-yun",
        "classic:guo-jia"
    ];

    internal static IReadOnlyList<string> PreHuangGaiClassicGeneralIds { get; } =
    [
        .. ClassicGeneralIds.Where(id => id != "classic:huang-gai")
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
