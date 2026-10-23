using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete two-general base roster of S5 main group 2634.</summary>
public sealed class StandardShanheS5XujingXuqiuPackage : IGameContentPackage
{
    public const string XujingId = "shanhe:s5-xu-jing-2634";
    public const string XuqiuId = "shanhe:s5-xu-qiu-2634";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-xujing-xuqiu-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        // S5 chapter 1 / boss 5018 / battle and main group 2634 其实难副:
        // original order 427 Xu Jing raw 3/3, 4871/skin1000307 Xu Qiu raw 4/4;
        // both Draw 2 / hand 4 / Slash 1 / Armor 0, no base cards or tactics.
        // 889/890 exactly match OL Yuxu/Shijian; 57 is classic Qixi and
        // 724 is boundary Yiji's per-point draw then optional hand transfer.
        // Exclude Hard Zishu 383 on Xu Jing, Xiahui 294 on Xu Qiu,
        // and Hard Sanlue 20741 on both bodies.
        builder.AddGeneral(new(XujingId, "许靖", "ol-xu-jing", "ol:yuxu", "shu", 3,
            ["ol:shijian"])
        {
            InitialHp = 3, CharacterId = "character:xu-jing", VariantId = "shanhe-s5-config-2634",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        // This source NPC reuses general 4871 under another skin and name.
        // Qun/Male are local interaction mappings; supporter is an explicit
        // portrait placeholder, not another named character's illustration.
        builder.AddGeneral(new(XuqiuId, "徐璆", "supporter", "classic:qixi", "qun", 4,
            ["boundary:yiji"])
        {
            InitialHp = 4, CharacterId = "character:xu-qiu", VariantId = "shanhe-s5-config-2634",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
