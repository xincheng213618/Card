using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete two-general base roster of S5 main group 2658.</summary>
public sealed class StandardShanheS5SunJianWuguotaiPackage : IGameContentPackage
{
    public const string SunjianId = "shanhe:s5-sun-jian-2658";
    public const string WuguotaiId = "shanhe:s5-wu-guo-tai-2658";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-sunjian-wuguotai-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        // S5 chapter 7 / order 2 Yingchuan / boss 5078 -> battle/group 2658
        // Xianlv Hujiang, enemyCount 2 (new-player group 22658 is separate).
        // Original order: 458 Sun Jian raw 4/4, then non-boss 640 Wu Guo Tai
        // raw 3/3. Both Draw 2 / random hand 4 / Slash 1 / Armor and gain 0;
        // no base fixed cards or tactics. Exclude Hard Shangshi 419 and
        // Nightmare Buqu 466 on Sun Jian.
        // Base 108/931 and 3505/3506 exactly reuse ordinary boundary skills.
        // Preserve the source Sun Jian body instead of ordinary boundary's
        // maximum-five/initial-four body. Portraits use existing local forms.
        builder.AddGeneral(new(SunjianId, "孙坚", "boundary_sun_jian", "boundary:yinghun", "wu", 4,
            ["boundary:wulie"])
        {
            InitialHp = 4, CharacterId = "character:sun-jian", VariantId = "shanhe-s5-config-2658",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        builder.AddGeneral(new(WuguotaiId, "吴国太", "boundary_wu_guo_tai", "boundary:ganlu-current", "wu", 3,
            ["boundary:buyi-current"])
        {
            InitialHp = 3, CharacterId = "character:wu-guo-tai", VariantId = "shanhe-s5-config-2658",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Female
        });
    }
}
