using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete base form of S5 main group 2666.</summary>
public sealed class StandardShanheS5QuyiPackage : IGameContentPackage
{
    public const string QuyiId = "shanhe:s5-qu-yi-2666";
    public const string FuqiId = "shanhe:s5-fuqi-14170";
    public const string JiaoziId = "shanhe:s5-jiaozi-14171";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-quyi-boss", CurrentVersion,
        [new("standard", StandardContentPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-quyi",
            tagOverrides: new Dictionary<string, SkillTag> { [FuqiId] = SkillTag.Locked, [JiaoziId] = SkillTag.Locked });
        // Main group 2666/general 374: 4/4, Draw2/Hand4/Slash1/Armor0.
        // Fixed card 20611 belongs to Hard and is excluded. The ordinary
        // faction, gender and portrait are local interaction/art mappings.
        builder.AddGeneral(new(QuyiId, "麹义", "qu_yi", FuqiId, "qun", 4, [JiaoziId])
        {
            InitialHp = 4, CharacterId = "character:qu-yi", VariantId = "shanhe-s5-config-2666",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
