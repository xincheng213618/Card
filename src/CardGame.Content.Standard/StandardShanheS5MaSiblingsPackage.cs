using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The full basic Ma Chao / Ma Yun Lu roster of S5 main group 2642.</summary>
public sealed class StandardShanheS5MaSiblingsPackage : IGameContentPackage
{
    public const string MachaoId = "shanhe:s5-ma-chao-2642";
    public const string MayunluId = "shanhe:s5-ma-yun-lu-2642";
    public const string MashuId = "shanhe:s5-mashu-14118";
    public const string TieqiId = "shanhe:s5-tieqi-14122";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-ma-siblings-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-ma-siblings",
            tagOverrides: new Dictionary<string, SkillTag> { [MashuId] = SkillTag.Locked });
        // Chapter 3 / boss 5038 / battle and main group 2642: both 4/4,
        // Draw2/Hand4/Slash1/Armor0. Medium 20766/20761 are excluded.
        builder.AddGeneral(new(MachaoId, "马超", "boundary-ma-chao", TieqiId, "shu", 4, [MashuId])
        {
            InitialHp = 4, CharacterId = "character:ma-chao", VariantId = "shanhe-s5-config-2642",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        // 266 matches ordinary Fengpo exactly; the source's non-boss second
        // body stays in the complete encounter, rather than being dropped.
        builder.AddGeneral(new(MayunluId, "马云騄", "ol-ma-yun-lu", "ol:fengpo", "shu", 4, [MashuId])
        {
            InitialHp = 4, CharacterId = "character:ma-yun-lu", VariantId = "shanhe-s5-config-2642",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Female
        });
    }
}
