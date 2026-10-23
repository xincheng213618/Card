using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete three-skill base form of S5 group 2668.</summary>
public sealed class StandardShanheS5LiruPackage : IGameContentPackage
{
    public const string LiruId = "shanhe:s5-li-ru-2668";
    public const string MiejiId = "shanhe:s5-mieji-14175";
    public const string FenchengId = "shanhe:s5-fencheng-14176";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-liru-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-li-ru");
        // S5 chapter 10 / boss 5108 / battle and group 2668: 3/3,
        // Draw 2 / hand 4 / Slash 1 / Armor 0, no fixed cards or tactics.
        // The separate new-player group 22668 does not specify an equipment zone.
        // Qun/Male and the existing portrait are local interaction/art mappings.
        builder.AddGeneral(new(LiruId, "李儒·毒士谋深", "li_ru", "boundary:juece-current", "qun", 3,
            [MiejiId, FenchengId])
        {
            InitialHp = 3, CharacterId = "character:li-ru", VariantId = "shanhe-s5-config-2668",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
