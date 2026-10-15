using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>
/// The first national-war slice. It deliberately reuses the standard deck and
/// skills while giving the mode its own general ids, faction contract and
/// hidden dual-general setup. Future packages can add real national skills
/// without changing this state-machine boundary.
/// </summary>
public sealed class StandardNationalWarLitePackage : IGameContentPackage
{
    public PackageManifest Manifest { get; } = new(
        "standard-national-war-lite",
        new Version(1, 1, 0),
        [new PackageDependency("standard", new Version(1, 11, 0))]);

    public void Register(IContentRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        CharacterContentCatalog.Default.Register(builder, Manifest.Id);

        builder.AddMode(new ContentModeDefinition(
            Id: "national:lite-4",
            Name: "四人国战（简化）",
            MinPlayers: 4,
            MaxPlayers: 4,
            RoleCounts: new Dictionary<string, int>(),
            DeckId: "standard:basic-demo",
            GeneralCandidateCount: 3,
            GeneralPoolIds:
            [
                "national:wei-cao-cao",
                "national:wei-guo-jia",
                "national:wei-xun-yu",
                "national:wei-ganglie",
                "national:shu-zhang-fei",
                "national:shu-guan-yu",
                "national:shu-zhao-yun",
                "national:shu-liu-bei"
            ],
            ModeKind: ContentModeKind.NationalWarLite,
            FactionCounts: new Dictionary<string, int>
            {
                ["wei"] = 2,
                ["shu"] = 2
            }));
    }
}
