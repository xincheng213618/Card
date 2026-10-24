using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete two-emperor base roster of S5 main group 2643.</summary>
public sealed class StandardShanheS5HanEmperorsPackage : IGameContentPackage
{
    public const string LiuxieId = "shanhe:s5-liu-xie-2643";
    public const string LiubianId = "shanhe:s5-liu-bian-2643";
    public const string ShiyuanId = "shanhe:s5-shiyuan-3050";
    public const string YuweiId = "ol:yuwei";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-han-emperors-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-han-emperors");
        // S5 chapter 3 / boss 5038 / battle and main group 2643: original
        // order 73 Liu Xie and non-boss 487 Liu Bian, both raw 3/3,
        // Draw2/Hand4/Slash1/Armor0. Medium 20846/20847 and Hard 3386/3385
        // are excluded; the independent new-player group is not a next phase.
        builder.AddGeneral(new(LiuxieId, "刘协", "ol-liu-xie", "ol:tianming", "qun", 3, ["ol:mizhao"])
        {
            InitialHp = 3, CharacterId = "character:liu-xie", VariantId = "shanhe-s5-config-2643",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        builder.AddGeneral(new(LiubianId, "刘辩", "ol-liu-bian", ShiyuanId, "qun", 3, ["ol:dushi", YuweiId])
        {
            InitialHp = 3, CharacterId = "character:liu-bian", VariantId = "shanhe-s5-config-2643",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
