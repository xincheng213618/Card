using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete base form of main group 2647, distinct from new-player group 22647.</summary>
public sealed class StandardShanheS5HuaxiongBasePackage : IGameContentPackage
{
    public const string HuaxiongId = "shanhe:s5-hua-xiong-2647";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-huaxiong-base-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        // S5 chapter4 Tiger Prison / boss5048 / battle and main group2647:
        // general446, raw5/5, Draw2/Hand4/Slash1/Armor0, no base cards/tactics.
        // 495 Yaowu reuses the classic red-Slash source recovery/draw choice.
        // Exclude higher-band skills3159/877/14135/14136 and cards20836/20741.
        builder.AddGeneral(new(HuaxiongId, "华雄·耀武", "hua_xiong", "classic:yaowu", "qun", 5)
        {
            InitialHp = 5, CharacterId = "character:hua-xiong", VariantId = "shanhe-s5-config-2647",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
