using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>All three basic enemies of S5 main group 2679, in source order.</summary>
public sealed class StandardShanheS5CaoWeiPioneersPackage : IGameContentPackage
{
    public const string CaozhenId = "shanhe:s5-cao-zhen-2679";
    public const string CaocaoId = "shanhe:s5-cao-cao-2679";
    public const string CaohongId = "shanhe:s5-cao-hong-2679";
    public const string JianxiongId = "shanhe:s5-jianxiong-14198";
    public const string HujiaId = "shanhe:s5-hujia-995";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-cao-wei-pioneers-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-cao-wei-pioneers",
            tagOverrides: new Dictionary<string, SkillTag> { [HujiaId] = SkillTag.Lord });
        // Acid-Zao 5128 -> battle/main group 2679, independent novice 22679.
        // Every enemy is raw 4/4, Draw2/Hand4/Slash1/Armor0/Gain0. No basic
        // fixed cards: 20881/20886/20882 are all Hard-only and excluded.
        builder.AddGeneral(new(CaozhenId, "曹真", "cao_zhen", "classic:sidi", "wei", 4, [])
        {
            InitialHp = 4, CharacterId = "character:cao-zhen", VariantId = "shanhe-s5-config-2679",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        builder.AddGeneral(new(CaocaoId, "曹操", "cao_cao", JianxiongId, "wei", 4, [HujiaId])
        {
            InitialHp = 4, CharacterId = "character:cao-cao", VariantId = "shanhe-s5-config-2679",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        builder.AddGeneral(new(CaohongId, "曹洪", "ol-cao-hong", "ol:yuanhu", "wei", 4, [])
        {
            InitialHp = 4, CharacterId = "character:cao-hong", VariantId = "shanhe-s5-config-2679",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
