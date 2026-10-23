using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete two-general base roster of S5 main group 2678.</summary>
public sealed class StandardShanheS5CaorenZhaoyunPackage : IGameContentPackage
{
    public const string CaorenId = "shanhe:s5-cao-ren-2678";
    public const string ZhaoyunId = "shanhe:s5-zhao-yun-2678";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-caoren-zhaoyun-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        // S5 chapter 12 / boss 5128 / battle and main group 2678 双雄际会:
        // source general 111 Cao Ren is raw 4/4 and 108 Zhao Yun is raw 3/3;
        // both Draw 2 / hand 4 / Slash 1 / Armor 0, no fixed cards or tactics.
        // Base 372/373 match OL SP Weikui/Lizhan, and 37/208 match the
        // complete SP Longdan/Chongzhen pair. Chongzhen requires sp:longdan,
        // not the identically described classic conversion.
        // Exclude Cao Ren's Hard Yimie 7005 and Nightmare Eyi 3296,
        // and Zhao Yun's Hard Paoxiao 34 and Nightmare Wushen 301.
        // Wei/Qun, Male and portraits reuse their ordinary form mappings.
        builder.AddGeneral(new(CaorenId, "曹仁", "ol-sp-cao-ren", "ol:weikui", "wei", 4,
            ["ol:lizhan"])
        {
            InitialHp = 4, CharacterId = "character:cao-ren", VariantId = "shanhe-s5-config-2678",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        builder.AddGeneral(new(ZhaoyunId, "赵云", "zhao_yun", "sp:longdan", "qun", 3,
            ["sp:chongzhen"])
        {
            InitialHp = 3, CharacterId = "character:zhao-yun", VariantId = "shanhe-s5-config-2678",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
