using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>Verified 2026 Shanhe forms, independent from the cooperative Hulao activity.</summary>
public sealed class StandardShanheBossPackage : IGameContentPackage
{
    public const string ShenguiLvbuId = "shanhe:lvbu-shengui-2026";
    public const string FenghuangId = "shanhe:ally-fenghuang-2026";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-bosses-2026", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-boss", skill => skill with
        { Tags = skill.Id == "shanhe:shengui-wushuang" ? SkillTag.Locked : SkillTag.None });

        // Six is the printed form's HP in the verified client panel. Encounter
        // scaling and choosing to spawn this form directly are local scenario data.
        builder.AddGeneral(new(ShenguiLvbuId, "吕布·神鬼无前", "shen_lu_bu",
            "shanhe:shengui-wushuang", "qun", 6, ["shanhe:shengui-shenqu"])
        {
            CharacterId = "character:lu-bu", VariantId = "shanhe-shengui-2026",
            RulesetId = "shanhe-2026", Gender = GeneralGender.Male
        });
        builder.AddGeneral(new(FenghuangId, "风篁", "", "ol:fudao", "qun", 4, ["ol:xiuhao"])
        {
            CharacterId = "character:shanhe-fenghuang", VariantId = "shanhe-companion-2026",
            RulesetId = "shanhe-2026", Gender = GeneralGender.Female
        });
    }
}
