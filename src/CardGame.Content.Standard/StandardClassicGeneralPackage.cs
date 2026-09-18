using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>
/// Opt-in classic identity roster. The legacy standard and demo expansion
/// packages remain unchanged so their checkpoints keep the original content
/// hashes and v1-v9 behavior.
/// </summary>
public sealed class StandardClassicGeneralPackage : IGameContentPackage
{
    public PackageManifest Manifest { get; } = new(
        Id: "standard-classic-generals",
        Version: new Version(1, 0, 0),
        Dependencies: [new PackageDependency("standard-rescue-skills", new Version(1, 0, 0))]);

    public void Register(IContentRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddSkill(new ContentSkillDefinition(
            "classic:feedback",
            "反馈",
            "受到伤害后，你可以获得伤害来源的一张牌。",
            SkillKind.Feedback));

        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:liu-bei",
            "刘备",
            "liu_bei",
            "standard:rende",
            "shu",
            BaseHp: 4));
        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:sun-quan",
            "孙权",
            "sun_quan",
            "standard:zhiheng",
            "wu",
            BaseHp: 4));
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
            GeneralPoolIds: ClassicGeneralIds));
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
            GeneralPoolIds: ClassicGeneralIds));
    }

    internal static IReadOnlyList<string> ClassicGeneralIds { get; } =
    [
        "classic:liu-bei",
        "classic:sun-quan",
        "classic:sima-yi",
        "classic:xiahou-dun",
        "classic:hua-tuo",
        "standard:cao-cao",
        "standard:zhang-fei",
        "standard:zhou-yu",
        "standard:zhuge-liang",
        "standard:guan-yu",
        "standard:zhao-yun",
        "standard:guo-jia"
    ];
}
