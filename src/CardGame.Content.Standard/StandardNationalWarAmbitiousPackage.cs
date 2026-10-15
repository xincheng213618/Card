using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>
/// M3's first independent-faction experiment. It builds on the four-player
/// national package, adds a six-player distribution with one solo faction, and
/// leaves the full formation/ambitious rules for later slices.
/// </summary>
public sealed class StandardNationalWarAmbitiousPackage : IGameContentPackage
{
    public PackageManifest Manifest { get; } = new(
        "standard-national-war-ambitious",
        new Version(1, 0, 0),
        [new PackageDependency("standard-national-war-lite", new Version(1, 0, 0))]);

    public void Register(IContentRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        CharacterContentCatalog.Default.Register(builder, Manifest.Id);

        builder.AddMode(new ContentModeDefinition(
            Id: "national:ambitious-6",
            Name: "六人国战（野心家试验）",
            MinPlayers: 6,
            MaxPlayers: 6,
            RoleCounts: new Dictionary<string, int>(),
            DeckId: "standard:basic-demo",
            GeneralCandidateCount: 3,
            GeneralPoolIds:
            [
                "national:wei-cao-cao",
                "national:wei-guo-jia",
                "national:wei-xun-yu",
                "national:wei-ganglie",
                "national:wei-xiahou-dun",
                "national:wei-sima-yi",
                "national:shu-zhang-fei",
                "national:shu-guan-yu",
                "national:shu-zhao-yun",
                "national:shu-liu-bei",
                "national:ambitious-lu-bu",
                "national:ambitious-diao-chan"
            ],
            ModeKind: ContentModeKind.NationalWarLite,
            FactionCounts: new Dictionary<string, int>
            {
                ["wei"] = 3,
                ["shu"] = 2,
                ["ambitious"] = 1
            },
            SoloFactionIds: ["ambitious"]));
    }
}
