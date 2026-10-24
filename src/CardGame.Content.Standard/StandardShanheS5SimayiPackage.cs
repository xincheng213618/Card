using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete base general of S5 main battle/group 2677.</summary>
public sealed class StandardShanheS5SimayiPackage : IGameContentPackage
{
    public const string SimayiId = "shanhe:s5-sima-yi-2677";
    public const string RenjieId = "shanhe:s5-renjie-14192";
    public const string BaiyinId = "shanhe:s5-baiyin-14193";
    public const string LianpoId = "ol:lianpo-2016";
    public const string JilueId = "ol:jilue-2016";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-simayi-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-simayi",
            tagOverrides: new Dictionary<string, SkillTag> { [RenjieId] = SkillTag.Locked, [BaiyinId] = SkillTag.Awakening });
        // S5 酸枣 chapter12 / boss5128 -> main battle/group2677 司马隐谋.
        // General208 is raw4/4, D2/H4/Slash1/Armor0, without base cards/tactics.
        // All four skills 14192/14193/320/321 are initially printed; nightmare card20776 is excluded.
        // Source321 is the original five Jilue abilities, not the boundary Guicai/Jizhi/Zhiheng edition.
        builder.AddGeneral(new(SimayiId, "司马懿", "shen-sima-yi", RenjieId, "wei", 4,
            [BaiyinId, LianpoId, JilueId])
        {
            InitialHp = 4, CharacterId = "character:sima-yi", VariantId = "shanhe-s5-config-2677",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
