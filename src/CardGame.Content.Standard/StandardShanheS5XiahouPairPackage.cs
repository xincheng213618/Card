using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The entire base roster of S5 main battle/group 2651.</summary>
public sealed class StandardShanheS5XiahouPairPackage : IGameContentPackage
{
    public const string XiahoudunId = "shanhe:s5-xiahou-dun-2651";
    public const string XiahouyuanId = "shanhe:s5-xiahou-yuan-2651";
    public const string GanglieId = "shanhe:s5-ganglie-14143";
    public const string ShensuId = "shanhe:s5-shensu-14144";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-xiahou-pair-boss", CurrentVersion,
        [new("standard", StandardContentPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-xiahou-pair");
        // S5 chapter5 / order2 Huanyuan / boss5058 -> battle/group2651.
        // General313 (boss) and 453: each raw4/4, D2/H4/S1/Armor0/Gain0.
        // No base fixed cards/tactics. Exclude Medium721/905 and Hard20761/20766.
        builder.AddGeneral(new(XiahoudunId, "夏侯惇", "xiahou_dun", GanglieId, "wei", 4)
        {
            InitialHp = 4, CharacterId = "character:xiahou-dun", VariantId = "shanhe-s5-config-2651",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
        builder.AddGeneral(new(XiahouyuanId, "夏侯渊", "xiahou_yuan", ShensuId, "wei", 4)
        {
            InitialHp = 4, CharacterId = "character:xiahou-yuan", VariantId = "shanhe-s5-config-2651",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
