using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete basic roster of S5 main group 2650, in source order.</summary>
public sealed class StandardShanheS5DianweiWeiziPackage : IGameContentPackage
{
    public const string DianweiId = "shanhe:s5-dian-wei-2650";
    public const string WeiziId = "shanhe:s5-wei-zi-2650";
    public const string QiangxiId = "shanhe:s5-qiangxi-14139";
    public const string NingeId = "boundary:ninge";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-dianwei-weizi-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-dianwei-weizi");
        // 229 Dian Wei is not the boss flag, but remains the first enemy.
        // Both draw two/start with four/Slash1/Armor0. Hard-only 20841 and
        // Wei Zi's medium-only 3097/3098 do not belong to this basic form.
        builder.AddGeneral(new(DianweiId, "典韦", "boundary_dian_wei", QiangxiId, "wei", 4, [NingeId])
        {
            InitialHp = 4, CharacterId = "character:dian-wei", VariantId = "shanhe-s5-config-2650",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        builder.AddGeneral(new(WeiziId, "卫兹", "ol-wei-zi", null, "wei", 3, [])
        {
            InitialHp = 3, CharacterId = "character:wei-zi", VariantId = "shanhe-s5-config-2650",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
