using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete two-general base roster of S5 main group 2661.</summary>
public sealed class StandardShanheS5XuyouJushouPackage : IGameContentPackage
{
    public const string XuyouId = "shanhe:s5-xu-you-2661";
    public const string JushouId = "shanhe:s5-ju-shou-2661";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-xuyou-jushou-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        // S5 chapter 8 / boss 5088 / battle and main group 2661 谋臣双璧:
        // source general 406 Xu You and 330 Ju Shou, both raw 3/3,
        // Draw 2 / hand 4 / Slash 1 / Armor 0, no fixed cards or tactics.
        // 814/815/816 match classic Chenglue/Shicai/Cunmu; 491/492 match
        // classic Jianying/Shibei. Boundary Jianying's extra conversion is
        // deliberately absent. Nightmare Cangxin 3314 and Guanxing 869
        // are excluded. Qun/Male and portraits reuse the ordinary mappings.
        builder.AddGeneral(new(XuyouId, "许攸", "xu_you", "classic:chenglue", "qun", 3,
            ["classic:shicai", "classic:cunmu"])
        {
            InitialHp = 3, CharacterId = "character:xu-you", VariantId = "shanhe-s5-config-2661",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        builder.AddGeneral(new(JushouId, "沮授", "ju_shou", "classic:jianying", "qun", 3,
            ["classic:shibei"])
        {
            InitialHp = 3, CharacterId = "character:ju-shou", VariantId = "shanhe-s5-config-2661",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
