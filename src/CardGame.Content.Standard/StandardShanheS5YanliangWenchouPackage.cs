using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The basic form of S5 main group 2665.</summary>
public sealed class StandardShanheS5YanliangWenchouPackage : IGameContentPackage
{
    public const string YanliangWenchouId = "shanhe:s5-yan-liang-wen-chou-2665";
    public const string ShuangxiongId = "shanhe:s5-shuangxiong-14169";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-yanliang-wenchou-boss", CurrentVersion,
        [new("standard", StandardContentPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-yanliang-wenchou");
        // Group 2665/general 550: 4/4, Draw2/Hand4/Slash1/Armor0.
        // Fixed card 20866 is nightmare-only. Qun/Male and the existing
        // boundary portrait are local interaction/art mappings.
        builder.AddGeneral(new(YanliangWenchouId, "河北双雄", "boundary_yan_liang_wen_chou",
            ShuangxiongId, "qun", 4, [])
        {
            InitialHp = 4, CharacterId = "character:yan-liang-wen-chou", VariantId = "shanhe-s5-config-2665",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
