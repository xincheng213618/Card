using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete three-general base roster of S5 main group 2683.</summary>
public sealed class StandardShanheS5TaoyuanPackage : IGameContentPackage
{
    public const string ZhangfeiId = "shanhe:s5-zhang-fei-2683";
    public const string LiubeiId = "shanhe:s5-liu-bei-2683";
    public const string GuanyuId = "shanhe:s5-guan-yu-2683";
    public const string TishenId = "shanhe:s5-tishen-14203";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-taoyuan-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-taoyuan");
        // S5 chapter 13 / boss 5138 / battle and main group 2683 桃园三英:
        // original order 301 Zhang Fei, 299 Liu Bei, 300 Guan Yu, all raw 4/4,
        // Draw 2 / hand 4 / Slash 1 / Armor 0, no base fixed cards or tactics.
        // Exclude Hard fixed cards 20811/20812/20813. Five base skills exactly
        // reuse boundary programs; 14203 Tishen has no limited-use quota.
        // Shu/Male and portraits reuse the ordinary boundary form mappings.
        builder.AddGeneral(new(ZhangfeiId, "张飞", "boundary_zhang_fei", "boundary:paoxiao", "shu", 4, [TishenId])
        {
            InitialHp = 4, CharacterId = "character:zhang-fei", VariantId = "shanhe-s5-config-2683",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        builder.AddGeneral(new(LiubeiId, "刘备", "boundary_liu_bei", "boundary:rende", "shu", 4, ["boundary:jijiang"])
        {
            InitialHp = 4, CharacterId = "character:liu-bei", VariantId = "shanhe-s5-config-2683",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        builder.AddGeneral(new(GuanyuId, "关羽", "boundary_guan_yu", "boundary:wusheng", "shu", 4, ["boundary:yijue"])
        {
            InitialHp = 4, CharacterId = "character:guan-yu", VariantId = "shanhe-s5-config-2683",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
