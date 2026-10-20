using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The separately imported chapter-14 Dong Zhuo form and its acquired skill.</summary>
public sealed class StandardShanheS5FengyiPackage : IGameContentPackage
{
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-fengyi-boss", CurrentVersion,
        [new("shanhe-s5-config-bosses", StandardShanheS5BossPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-jiuchi");
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-daoshu");
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-zaiqi");
        // S5 / chapter 14 / node 5148 / battle-group 2686: general 470,
        // initially wounded at 10/15. This is an initial value, not damage.
        // The user confirmed the granted Zaiqi's lost-HP-plus-one semantics.
        builder.AddGeneral(new(StandardShanheS5BossPackage.DongZhuoFengyiId, "董卓·覆銮凶影", "dong_zhuo",
            StandardShanheS5BossPackage.JiuchiId, "qun", 15,
            [StandardShanheS5BossPackage.RoulinId, "boundary:benghuai-current", StandardShanheS5BossPackage.DaoshuId])
        {
            InitialHp = 10, CharacterId = "character:dong-zhuo", VariantId = "shanhe-s5-config-fengyi",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
