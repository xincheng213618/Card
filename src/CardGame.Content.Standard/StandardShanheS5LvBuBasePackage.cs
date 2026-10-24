using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The two independent basic Wushuang Fengxian bodies in S5 main groups 2644 and 2645.</summary>
public sealed class StandardShanheS5LvBuBasePackage : IGameContentPackage
{
    public const string LvBuFiveId = "shanhe:s5-lv-bu-2644";
    public const string LvBuFourId = "shanhe:s5-lv-bu-2645";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-lvbu-base-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        // S5 Tiger Prison / boss5048 lists both battles independently, with
        // startGeneralId=0. Both print skills66/728, D2/H4/Slash1/Armor0,
        // no tactics, and the same base fixed Fangtian Halberd20546.
        // The two raw bodies are 5/5 and 4/4; neither is a transformation.
        // Nightmare14128 and Hard SilverLion20571 are not base resources.
        foreach (var (id, hp, group) in new[] { (LvBuFiveId, 5, 2644), (LvBuFourId, 4, 2645) })
            builder.AddGeneral(new(id, "吕布", "boundary_lu_bu", "classic:wushuang", "qun", hp,
                ["boundary:liyu-current"])
            {
                InitialHp = hp, CharacterId = "character:lu-bu", VariantId = "shanhe-s5-config-" + group,
                RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
            });
    }
}
