using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>S5 group 2639 base form, sharing the source-identical OL programs.</summary>
public sealed class StandardShanheS5CaoxingPackage : IGameContentPackage
{
    public const string CaoxingId = "shanhe:s5-caoxing-duomu";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-caoxing-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        // S5 chapter 2 / boss 5028 / battle and group 2639 (parallel new-player
        // group 22639): one enemy, raw 4/4, Draw 2 / hand 4 / Slash 1 / Armor 0.
        // The base form excludes higher-difficulty additions. Faction and gender
        // are explicit local mappings from the source-identical ordinary general.
        builder.AddGeneral(new(CaoxingId, "曹性", "ol-cao-xing", "ol:liu-shi", "qun", 4, ["ol:zhan-wan"])
        {
            InitialHp = 4, CharacterId = "character:cao-xing", VariantId = "shanhe-s5-config-2639",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
