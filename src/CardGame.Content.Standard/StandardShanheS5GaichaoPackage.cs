using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The independently published single-boss group 2687, reusing the Fengyi skill programs.</summary>
public sealed class StandardShanheS5GaichaoPackage : IGameContentPackage
{
    public const string DongZhuoGaichaoId = "shanhe:s5-dongzhuo-gaichao";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-gaichao-boss", CurrentVersion,
        [new("shanhe-s5-fengyi-boss", StandardShanheS5FengyiPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        // S5 / chapter 14 / boss node 5148 / battle and group 2687.
        // This group has no fixed cards and starts at raw 10/15. The parallel
        // group 22687 has 5/10 and must not supply this form's initial values.
        // The imported dependency owns the four complete skill programs once.
        builder.AddGeneral(new(DongZhuoGaichaoId, "董卓·改朝罪魁", "dong_zhuo",
            StandardShanheS5BossPackage.JiuchiId, "qun", 15,
            [StandardShanheS5BossPackage.RoulinId, "boundary:benghuai-current", StandardShanheS5BossPackage.DaoshuId])
        {
            InitialHp = 10, CharacterId = "character:dong-zhuo", VariantId = "shanhe-s5-config-gaichao-2687",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
