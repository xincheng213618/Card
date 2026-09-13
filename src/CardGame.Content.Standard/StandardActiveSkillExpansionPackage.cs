using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>
/// Opt-in content package for the active-skill slice. It depends on the
/// stable standard package and adds demo generals and two mode ids, so
/// the original standard registry and its replay fingerprint remain unchanged.
/// </summary>
public sealed class StandardActiveSkillExpansionPackage : IGameContentPackage
{
    private readonly bool _includeJijiu;

    public StandardActiveSkillExpansionPackage(bool includeJijiu = false)
    {
        _includeJijiu = includeJijiu;
    }

    public PackageManifest Manifest { get; } = new(
        Id: "standard-active-skills",
        Version: new Version(1, 0, 0),
        Dependencies: [new PackageDependency("standard", new Version(1, 11, 0))]);

    public void Register(IContentRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddSkill(new ContentSkillDefinition(
            "standard:kujin",
            "苦肉",
            "出牌阶段可在体力大于 0 时失去 1 点体力；若进入濒死，救援结算后再摸两张牌。",
            SkillKind.Kujin));
        builder.AddSkill(new ContentSkillDefinition(
            "standard:zhiheng",
            "制衡",
            "出牌阶段弃置至少一张手牌，然后摸等量牌。",
            SkillKind.Zhiheng));
        builder.AddSkill(new ContentSkillDefinition(
            "standard:rende",
            "仁德",
            "出牌阶段将一至若干张手牌交给一名其他角色；一次交给至少两张时回复 1 点体力。",
            SkillKind.Rende));
        builder.AddSkill(new ContentSkillDefinition(
            "standard:qingnang",
            "青囊",
            "出牌阶段每回合弃置一张手牌，令一名受伤角色回复 1 点体力。",
            SkillKind.Qingnang));
        builder.AddSkill(new ContentSkillDefinition(
            "standard:huichun",
            "回春",
            "出牌阶段每回合弃置两张手牌，令至少两名受伤角色各回复 1 点体力。",
            SkillKind.Huichun));
        builder.AddSkill(new ContentSkillDefinition(
            "standard:mashu",
            "马术",
            "锁定技，你计算与其他角色的距离始终 -1。",
            SkillKind.Mashu));
        builder.AddSkill(new ContentSkillDefinition(
            "standard:qicai",
            "奇才",
            "锁定技，你使用锦囊牌无距离限制。",
            SkillKind.Qicai));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:demo-kujin",
            "黄盖",
            "sun_quan",
            "standard:kujin",
            "wu"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:demo-zhiheng",
            "制衡者",
            "sun_quan",
            "standard:zhiheng",
            "wu"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:demo-rende",
            "仁德者",
            "liu_bei",
            "standard:rende",
            "shu"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:demo-qingnang",
            "青囊者",
            "hua_tuo",
            "standard:qingnang",
            "qun"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:demo-huichun",
            "回春者",
            "hua_tuo",
            "standard:huichun",
            "qun"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:demo-mashu",
            "马术者",
            "zhao_yun",
            "standard:mashu",
            "shu"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:demo-qicai",
            "奇才者",
            "zhuge_liang",
            "standard:qicai",
            "shu"));

        var additionalGeneralIds = new List<string>
        {
                "standard:demo-kujin",
                "standard:demo-zhiheng",
                "standard:demo-rende",
                "standard:demo-qingnang",
                "standard:demo-huichun",
                "standard:demo-mashu",
                "standard:demo-qicai"
        };
        if (_includeJijiu)
        {
            additionalGeneralIds.Add("standard:demo-jijiu");
        }

        var generalPool = StandardContentPackage.StandardGeneralIds
            .Concat(additionalGeneralIds)
            .ToArray();
        builder.AddMode(new ContentModeDefinition(
            Id: "identity:active-skills-8",
            Name: "八人身份局（扩展技能演示）",
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
            GeneralCandidateCount: 21,
            GeneralPoolIds: generalPool));
        builder.AddMode(new ContentModeDefinition(
            Id: "identity:active-skills-5",
            Name: "五人身份局（扩展技能演示）",
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
            GeneralCandidateCount: 21,
            GeneralPoolIds: generalPool));
    }
}
