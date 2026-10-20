using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete single-skill base form of S5 groups 2648 and 22648.</summary>
public sealed class StandardShanheS5YujinPackage : IGameContentPackage
{
    public const string YujinId = "shanhe:s5-yujin-zhenjun-2648";
    public const string ZhenjunId = "shanhe:s5-zhenjun-238";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-yujin-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-yujin");
        // S5 chapter 5 / boss 5058 / battle and group 2648 (or 22648):
        // 4/4, Draw 2 / hand 4 / Slash 1 / Armor 0. Other skills and fixed cards
        // belong to higher source bands. Wei/Male and portrait are local mappings.
        builder.AddGeneral(new(YujinId, "于禁·毅重镇军", "boundary_yu_jin", ZhenjunId, "wei", 4)
        {
            InitialHp = 4, CharacterId = "character:yu-jin", VariantId = "shanhe-s5-config-2648",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
