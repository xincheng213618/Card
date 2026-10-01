using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>
/// Formal standard-national Zhang Jiao slice. The package stays separate from
/// the Lite roster so older national checkpoints retain their package list and
/// content fingerprint.
/// </summary>
public sealed class StandardNationalZhangJiaoPackage : IGameContentPackage
{
    private static SkillProgramCatalog Catalog =>
        EmbeddedSkillProgramCatalog.Catalog("national-zhang-jiao");

    public PackageManifest Manifest { get; } = new(
        "standard-national-zhang-jiao",
        new Version(1, 0, 0),
        [
            new PackageDependency("standard-national-war-lite", new Version(1, 1, 0)),
            new PackageDependency("standard-active-skills", new Version(1, 0, 0)),
            new PackageDependency("standard-rescue-skills", new Version(1, 0, 0))
        ]);

    public void Register(IContentRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        foreach (var (id, program) in Catalog.Programs.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var presentation = Catalog.Presentations[id];
            builder.AddSkill(new ContentSkillDefinition(
                id,
                presentation.Name,
                presentation.Description)
            {
                Program = program
            });
        }

        builder.AddGeneral(new ContentGeneralDefinition(
            "national:zhang-jiao",
            "张角",
            "zhang_jiao",
            "national:leiji",
            "qun",
            BaseHp: 3,
            AdditionalSkillIds: ["national:guidao"]));
        builder.AddGeneral(new ContentGeneralDefinition(
            "national:hua-tuo",
            "华佗",
            "hua_tuo",
            "standard:jijiu",
            "qun",
            BaseHp: 3,
            AdditionalSkillIds: ["standard:qingnang"]));

        builder.AddMode(new ContentModeDefinition(
            Id: "national:zhang-jiao-4",
            Name: "四人国战（张角试验）",
            MinPlayers: 4,
            MaxPlayers: 4,
            RoleCounts: new Dictionary<string, int>(),
            DeckId: "standard:basic-demo",
            GeneralCandidateCount: 2,
            GeneralPoolIds:
            [
                "national:wei-cao-cao",
                "national:wei-guo-jia",
                "national:shu-zhang-fei",
                "national:shu-guan-yu",
                "national:shu-zhao-yun",
                "national:shu-liu-bei",
                "national:zhang-jiao",
                "national:hua-tuo"
            ],
            ModeKind: ContentModeKind.NationalWarLite,
            FactionCounts: new Dictionary<string, int>
            {
                ["wei"] = 1,
                ["shu"] = 2,
                ["qun"] = 1
            }));
    }

}
