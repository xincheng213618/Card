using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>
/// Optional rescue-skill layer. It is separate from the original active-skill
/// package so old active-skill checkpoints can still be restored with their
/// original package signature and content hash.
/// </summary>
public sealed class StandardRescueSkillExpansionPackage : IGameContentPackage
{
    public PackageManifest Manifest { get; } = new(
        Id: "standard-rescue-skills",
        Version: new Version(1, 0, 0),
        Dependencies: [new PackageDependency("standard-active-skills", new Version(1, 0, 0))]);

    public void Register(IContentRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("conversion-cutover", "standard:jijiu"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "standard:demo-jijiu",
            "急救者",
            "hua_tuo",
            "standard:jijiu",
            "qun"));
    }
}
