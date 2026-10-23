using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete basic Li Jue of S5 main group 2636.</summary>
public sealed class StandardShanheS5LiJuePackage : IGameContentPackage
{
    public const string LijueId = "shanhe:s5-li-jue-2636";
    public const string LangxiId = "shanhe:s5-langxi-14113";
    public const string YisuanId = "ol:yisuan";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-li-jue-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-li-jue");
        builder.AddGeneral(new(LijueId, "李傕", "ol-li-jue", LangxiId, "qun", 4, [YisuanId])
        {
            InitialHp = 4, CharacterId = "character:li-jue", VariantId = "shanhe-s5-config-2636",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
