using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The shared basic Gongsun Zan and the Gongsun Yue of S5 groups 2662/2681.</summary>
public sealed class StandardShanheS5GongsunZanPackage : IGameContentPackage
{
    public const string GongsunZanId = "shanhe:s5-gong-sun-zan-2662";
    public const string GongsunYueId = "shanhe:s5-gong-sun-yue-2662";
    public const string QiaomengId = "shanhe:s5-qiaomeng-14166";
    public const string JiuzhuId = "shanhe:s5-jiuzhu-505";
    public const string YicongId = "boundary:yicong-current";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-gongsun-zan-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-gongsun-zan");
        // General 320 is the same 4/4 basic form in both main groups. 204 is
        // precisely current boundary Yicong; 14166 is a different Qiaomeng.
        builder.AddGeneral(new(GongsunZanId, "公孙瓒", "gongsun_zan", QiaomengId, "qun", 4, [YicongId])
        {
            InitialHp = 4, CharacterId = "character:gongsun-zan", VariantId = "shanhe-s5-config-2662",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        // Raw 4871/skin1000165 is named Gongsun Yue here, independently of
        // other encounters reusing that source general. No nightmare skills.
        builder.AddGeneral(new(GongsunYueId, "公孙越", "supporter", JiuzhuId, "qun", 4, [])
        {
            InitialHp = 4, CharacterId = "character:gongsun-yue", VariantId = "shanhe-s5-config-2662",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
