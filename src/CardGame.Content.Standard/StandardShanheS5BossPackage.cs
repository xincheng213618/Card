using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>Explicit forms from the public Shanhe configuration snapshot, separate from panel-verified forms.</summary>
public sealed class StandardShanheS5BossPackage : IGameContentPackage
{
    public const string ZhangliaoBaseId = "shanhe:s5-zhangliao-base";
    public const string ZhangliaoLingdongId = "shanhe:s5-zhangliao-lingdong";
    public const string ZhangliaoHuangkongId = "shanhe:s5-zhangliao-huangkong";
    public const string DongZhuoLuoyangId = "shanhe:s5-dongzhuo-luoyang";
    public const string DongZhuoFengyiId = "shanhe:s5-dongzhuo-fengyi";
    public const string JiuchiId = "shanhe:s5-jiuchi-14210";
    public const string DaoshuId = "shanhe:s5-daoshu-952";
    public const string DongMinBaseId = "shanhe:current-dongmin-dungong-base";
    public const string DongMinCuanchaoId = "shanhe:current-dongmin-dungong-cuanchao";
    public const string LvbuBajiId = "shanhe:s5-lvbu-baji";
    public const string LiyuId = "shanhe:s5-liyu-14128";
    public const string DiaoChanBiyueId = "shanhe:s5-diao-chan-biyue";
    public const string BiyueId = "shanhe:s5-biyue-14213";
    public const string LijianId = "shanhe:s5-lijian-68";
    public const string RoulinId = "shanhe:s5-roulin-14211";
    public const string DungongId = "shanhe:current-dungong-3387";
    public const string CuanchaoId = "shanhe:current-cuanchao-14069";
    public const string DongZhuoBaonueId = "boundary:baonue-current";
    public const string TuxiId = "shanhe:s5-tuxi-722";
    public const string LingdongId = "shanhe:s5-lingdong-14045";
    public const string HuangkongId = "shanhe:s5-huangkong-14052";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-config-bosses", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion),
            new("shanhe-bosses-2026", StandardShanheBossPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-zhangliao");
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-lingdong", skill => skill with { Tags = SkillTag.Locked });
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-huangkong", skill => skill with { Tags = SkillTag.Locked });
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-current-dungong", skill => skill with { Tags = SkillTag.Locked });
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-current-cuanchao", skill => skill with { Tags = SkillTag.Conversion });
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-liyu");
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-biyue", skill => skill with { Tags = SkillTag.Locked });
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-lijian");
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-roulin", skill => skill with { Tags = SkillTag.Locked });
        // enemy-groups 2646 has 4/4 before unspecified rank/difficulty calculations.
        // Its base skills are 722 and 3389. The latter is exactly the retained
        // boundary Pojun program; the all-hand 14112 variant must not replace it.
        builder.AddGeneral(new(ZhangliaoBaseId, "张辽·文远之威", "zhang_liao", TuxiId, "wei", 4,
            ["boundary:pojun"])
        {
            CharacterId = "character:zhang-liao", VariantId = "shanhe-s5-config-base",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        builder.AddGeneral(new(ZhangliaoLingdongId, "张辽·文远之威（灵动）", "zhang_liao", TuxiId, "wei", 4,
            ["boundary:pojun", LingdongId])
        {
            CharacterId = "character:zhang-liao", VariantId = "shanhe-s5-config-lingdong",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        builder.AddGeneral(new(ZhangliaoHuangkongId, "张辽·文远之威（灵动·惶恐）", "zhang_liao", TuxiId, "wei", 4,
            ["boundary:pojun", LingdongId, HuangkongId])
        {
            CharacterId = "character:zhang-liao", VariantId = "shanhe-s5-config-huangkong",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        // Luoyang boss-node groups 2670/2671 and 22670/22671 print these four
        // skills on general 470 at raw 4/4. No stage transition is implied.
        builder.AddGeneral(new(DongZhuoLuoyangId, "董卓·洛阳技能形态", "dong_zhuo", "boundary:jiuchi-current", "qun", 4,
            ["boundary:roulin-current", "boundary:benghuai-current", DongZhuoBaonueId])
        {
            CharacterId = "character:dong-zhuo", VariantId = "shanhe-s5-config-luoyang",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        // Current global group 267101 prints 5/5 and only Dungong at base.
        // Its season/stage parent is not published; Qun and local placement
        // are explicit scenario choices, not an inferred Dong Zhuo transition.
        builder.AddGeneral(new(DongMinBaseId, "董旻·盾弓基础形态", "dong_min", DungongId, "qun", 5)
        {
            CharacterId = "character:dong-min", VariantId = "shanhe-current-config-dungong-base",
            RulesetId = "shanhe-current-global-config", Gender = GeneralGender.Male
        });
        // The same groups list Cuanchao at different upper difficulty bands.
        // This is a verified skill form, not a complete equipment/stage recipe.
        builder.AddGeneral(new(DongMinCuanchaoId, "董旻·盾弓·篡朝技能形态", "dong_min", DungongId, "qun", 5,
            [CuanchaoId])
        {
            CharacterId = "character:dong-min", VariantId = "shanhe-current-config-dungong-cuanchao",
            RulesetId = "shanhe-current-global-config", Gender = GeneralGender.Male
        });
        // S5 chapter 14 / node 5148 / groups 2686 and 22686 list this
        // non-boss follower with exactly these two base skills at raw 10/10.
        // This independent form does not imply the full three-enemy encounter,
        // a transformation, Shenqu/Jiwu, or an initial zone for fixed cards.
        builder.AddGeneral(new(LvbuBajiId, "吕布·霸戟雄英", "lu_bu", "shanhe:shengui-wushuang", "qun", 10,
            [LiyuId])
        {
            CharacterId = "character:lu-bu", VariantId = "shanhe-s5-config-baji",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        // The same published group prints this second non-boss follower at 6/6
        // with only 68 Lijian and 14213 Biyue. Fixed-card starting zones are unknown.
        builder.AddGeneral(new(DiaoChanBiyueId, "貂蝉·闭月仙伶", "diao_chan", BiyueId, "qun", 6,
            [LijianId])
        {
            CharacterId = "character:diao-chan", VariantId = "shanhe-s5-config-biyue",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Female
        });
    }
}
