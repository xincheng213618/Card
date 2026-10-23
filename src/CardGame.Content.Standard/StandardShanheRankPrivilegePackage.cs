using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The rank-five privilege is imported only while a campaign still owns its one rebirth.</summary>
public sealed class StandardShanheRankPrivilegePackage : IGameContentPackage
{
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-rank-privileges", CurrentVersion, []);

    public void Register(IContentRegistryBuilder builder) =>
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-rank-privileges", skill => skill with { Tags = SkillTag.Limited });
}
