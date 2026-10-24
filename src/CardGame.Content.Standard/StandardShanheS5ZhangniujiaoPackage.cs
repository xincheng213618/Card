using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The skillless base form of S5 main battle/group 2653.</summary>
public sealed class StandardShanheS5ZhangniujiaoPackage : IGameContentPackage
{
    public const string ZhangniujiaoId = "shanhe:s5-zhang-niu-jiao-2653";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-zhangniujiao-boss", CurrentVersion,
        [new("standard", StandardContentPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        // S5 chapter6 伊阙关 / boss5068 -> main battle/group2653 黄巾余匪.
        // General4871 skin1000367: raw3/3, Draw2/Hand4/Slash1/Armor0.
        // All source skills are above base: Medium3231/Hard51/Nightmare719.
        // Hard fixed card20776 is excluded. Do not invent an empty skill grant.
        builder.AddGeneral(new(ZhangniujiaoId, "张牛角", "supporter", null, "qun", 3)
        {
            InitialHp = 3, CharacterId = "character:zhang-niu-jiao", VariantId = "shanhe-s5-config-2653-skin-1000367",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
