using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>
/// Opt-in public-team modes. Keeping this package separate preserves the
/// original standard content hash and the identity-mode checkpoint contract.
/// </summary>
public sealed class StandardTeamModePackage : IGameContentPackage
{
    public PackageManifest Manifest { get; } = new(
        "standard-team-modes",
        new Version(1, 0, 0),
        [new PackageDependency("standard", new Version(1, 11, 0))]);

    public void Register(IContentRegistryBuilder builder)
    {
        builder.AddMode(new ContentModeDefinition(
            Id: "team:standard-2v2",
            Name: "2v2公开阵营（演示）",
            MinPlayers: 4,
            MaxPlayers: 4,
            RoleCounts: new Dictionary<string, int>(),
            DeckId: "standard:basic-demo",
            GeneralCandidateCount: 3,
            GeneralPoolIds: StandardContentPackage.StandardGeneralIds,
            ModeKind: ContentModeKind.Team,
            TeamCounts: new Dictionary<string, int>
            {
                ["team:blue"] = 2,
                ["team:red"] = 2
            }));
    }
}
