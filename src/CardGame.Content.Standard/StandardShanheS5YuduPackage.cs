using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete three-skill base form of S5 groups 2633 and 22633.</summary>
public sealed class StandardShanheS5YuduPackage : IGameContentPackage
{
    public const string YuduId = "shanhe:s5-yudu-2633";
    public const string CuiduId = "shanhe:s5-cuidu-14104";
    public const string ZhongduId = "shanhe:s5-zhongdu-14105";
    public const string NiepanId = "shanhe:s5-niepan-95";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-yudu-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-yudu", tagOverrides: new Dictionary<string, SkillTag>
        {
            [CuiduId] = SkillTag.Locked,
            [ZhongduId] = SkillTag.Locked,
            [NiepanId] = SkillTag.Limited
        });
        // S5 chapter 1 / boss 5018 / battle and group 2633 (or 22633):
        // 3/3, Draw 2 / hand 4 / Slash 1 / Armor 0. Zhongdu is printed initially.
        // No higher-band fixed card is a base loadout. Qun/Male are local
        // mappings; an empty portrait key uses the ordinary missing-art fallback.
        builder.AddGeneral(new(YuduId, "于毒·黑山匪害", "", CuiduId, "qun", 3, [ZhongduId, NiepanId])
        {
            InitialHp = 3, CharacterId = "character:yu-du", VariantId = "shanhe-s5-config-2633",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
