using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>S5 group 2656 base form, with the published 14047/3388 skill pair.</summary>
public sealed class StandardShanheS5ZhangliangPackage : IGameContentPackage
{
    public const string ZhangliangId = "shanhe:s5-zhang-liang-2656";
    public const string BaoyuId = "shanhe:s5-baoyu-14047";
    public const string WuyingId = "shanhe:s5-wuying-3388";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-zhangliang-boss", CurrentVersion,
        [new("standard", StandardContentPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-zhangliang");
        // Published S5 chapter 7 / boss 5078 / battle and group 2656:
        // 隐士张梁, raw HP 3/4, hand 4 / Draw 2 / Slash 1 / Armor 0;
        // No fixed cards, equipment or tactics. Qun/Male are local mappings;
        // the portrait key is a placeholder until a verified asset is available.
        builder.AddGeneral(new(ZhangliangId, "隐士张梁", "zhang_liang", BaoyuId, "qun", 4, [WuyingId])
        {
            InitialHp = 3, CharacterId = "character:zhang-liang", VariantId = "shanhe-s5-config-2656",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
