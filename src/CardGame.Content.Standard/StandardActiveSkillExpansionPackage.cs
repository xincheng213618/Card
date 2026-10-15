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

    public static Version CurrentVersion { get; } = new(1, 2, 0);

    public StandardActiveSkillExpansionPackage(bool includeJijiu = false)
    {
        _includeJijiu = includeJijiu;
        Manifest = new PackageManifest(
            Id: "standard-active-skills",
            Version: CurrentVersion,
            Dependencies: [new PackageDependency("standard", new Version(1, 11, 0))]);
    }

    public PackageManifest Manifest { get; }

    public void Register(IContentRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        CharacterContentCatalog.Default.Register(builder, Manifest.Id);

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
