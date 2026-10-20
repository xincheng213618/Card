using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete raw two-enemy roster of S5 Dongshi Luanchao group 2685.</summary>
public sealed class StandardShanheS5DongshiPackage : IGameContentPackage
{
    public const string DongZhuoBaxinId = "shanhe:s5-dongzhuo-baxin";
    public const string DongJiQiaoxiaoId = "shanhe:s5-dongji-qiaoxiao";
    public const string LianzhuId = "shanhe:s5-lianzhu-14212";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-dongshi-boss", CurrentVersion,
        [new("shanhe-s5-fengyi-boss", StandardShanheS5FengyiPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-lianzhu");
        // S5 / chapter 14 / boss 5148 / battle and group 2685. Both source
        // members have no fixed cards or tactics. The separate new-player
        // group 22685 is not a stage or a source of this roster's equipment.
        builder.AddGeneral(new(DongZhuoBaxinId, "董卓·霸心毁朝", "dong_zhuo",
            StandardShanheS5BossPackage.JiuchiId, "qun", 15,
            [StandardShanheS5BossPackage.RoulinId, "boundary:benghuai-current", StandardShanheS5BossPackage.DaoshuId])
        {
            InitialHp = 10, CharacterId = "character:dong-zhuo", VariantId = "shanhe-s5-config-dongshi-2685",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        // The extracted group does not encode faction or gender. Qun/Female
        // and the existing Dong Bai portrait are explicit local presentation
        // and interaction choices, not fields inferred from the source row.
        builder.AddGeneral(new(DongJiQiaoxiaoId, "巧笑董姬", "dong_bai", LianzhuId, "qun", 8,
            ["ol:xiahui"])
        {
            CharacterId = "character:shanhe-dongji", VariantId = "shanhe-s5-config-dongshi-366",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Female
        });
    }
}
