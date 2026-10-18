using CardGame.Content.Standard;
using CardGame.Core;

// An original tutorial character that reuses an existing skill. This package is
// compiled into the sample host; it does not alter the built-in mode or registry.
internal sealed class TrainingGeneralPackage(ContentModeDefinition baseMode) : IGameContentPackage
{
    public const string GeneralId = "sample:training-general";
    public const string ModeId = "sample:training-5";

    public PackageManifest Manifest { get; } = new("headless-training", new Version(1, 0, 0),
        [new PackageDependency("standard", StandardContentPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        builder.AddGeneral(new ContentGeneralDefinition(
            Id: GeneralId,
            Name: "练习武将",
            PortraitKey: "zhang_fei",
            SkillId: "standard:paoxiao",
            FactionId: "shu",
            BaseHp: 4));

        var pool = new[] { GeneralId }.Concat(baseMode.GeneralPoolIds!).ToArray();
        builder.AddMode(baseMode with
        {
            Id = ModeId,
            Name = "Headless training",
            GeneralPoolIds = pool,
            // Expose every candidate to make the tutorial character selectable
            // without a seed search or changes to the standard draft.
            GeneralCandidateCount = pool.Length
        });
    }
}
