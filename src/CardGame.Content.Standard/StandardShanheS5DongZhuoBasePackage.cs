using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The identical basic Dong Zhuo roster in S5 main groups 2670 and 2671.</summary>
public sealed class StandardShanheS5DongZhuoBasePackage : IGameContentPackage
{
    public const string DongzhuoId = "shanhe:s5-dong-zhuo-2670-2671";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-dong-zhuo-base-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        // Both source groups contain only general 470, raw 4/4, Draw2/Hand4/Slash1,
        // Armor0/Gain0, and skills 949/110/111/950. No fixed cards or tactics.
        // These match the four current boundary skills exactly; ordinary Dong Zhuo
        // has eight maximum HP, so preserve the source body independently.
        builder.AddGeneral(new(DongzhuoId, "董卓", "boundary-dong-zhuo", "boundary:jiuchi-current", "qun", 4,
            ["boundary:roulin-current", "boundary:benghuai-current", "boundary:baonue-current"])
        {
            InitialHp = 4, CharacterId = "character:dong-zhuo", VariantId = "shanhe-s5-config-2670-2671",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
