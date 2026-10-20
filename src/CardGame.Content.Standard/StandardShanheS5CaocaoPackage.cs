using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete single-skill base form of S5 groups 2649 and 22649.</summary>
public sealed class StandardShanheS5CaocaoPackage : IGameContentPackage
{
    public const string CaocaoId = "shanhe:s5-caocao-guixin-2649";
    public const string GuixinId = "shanhe:s5-guixin-14138";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-caocao-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-caocao");
        // S5 chapter 5 / boss 5058 / battle and group 2649 (or 22649):
        // 3/3, Draw 2 / hand 4 / Slash 1 / Armor 0. Feiying and fixed cards
        // belong to higher source bands. Wei/Male and portrait are local mappings.
        builder.AddGeneral(new(CaocaoId, "曹操·归心", "cao_cao", GuixinId, "wei", 3)
        {
            InitialHp = 3, CharacterId = "character:cao-cao", VariantId = "shanhe-s5-config-2649",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
