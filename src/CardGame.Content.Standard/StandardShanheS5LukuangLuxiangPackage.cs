using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The S5 group 2667 base form reuses the source-identical OL skills.</summary>
public sealed class StandardShanheS5LukuangLuxiangPackage : IGameContentPackage
{
    public const string LukuangLuxiangId = "shanhe:s5-lu-kuang-lu-xiang-2667";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-lukuang-luxiang-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        // S5 main group 2667: one general, raw 4/4, Draw 2 / hand 4 /
        // Slash 1 / Armor 0, no fixed cards or tactics. Source skills 932/933
        // exactly match OL Qigong/Liehou. Hard 939 and nightmare 1324 are excluded.
        // Qun/Male and the existing portrait are explicit local mappings.
        builder.AddGeneral(new(LukuangLuxiangId, "吕旷吕翔", "ol-lv-kuang-lv-xiang", "ol:qigong", "qun", 4, ["ol:liehou"])
        {
            InitialHp = 4, CharacterId = "character:lv-kuang-lv-xiang", VariantId = "shanhe-s5-config-2667",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
