using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete two-skill base form of the S5 new-player group 22647.</summary>
public sealed class StandardShanheS5HuaxiongPackage : IGameContentPackage
{
    public const string HuaxiongId = "shanhe:s5-huaxiong-newplayer-22647";
    public const string YaowuId = "shanhe:s5-yaowu-14135";
    public const string ShizhanId = "shanhe:s5-shizhan-14136";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-huaxiong-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-huaxiong",
            skill => skill.Id == YaowuId ? skill with { Tags = SkillTag.Locked } : skill);
        // Group 22647, not the distinct raw 5/5 group 2647 or ordinary Hua Xiong.
        // Faction/gender are local mappings. The only fixed-card row is upper-band
        // Sanlue with no initial zone; the base form does not invent equipment.
        builder.AddGeneral(new(HuaxiongId, "华雄·虎牢双技", "boundary_hua_xiong", ShizhanId, "qun", 3, [YaowuId])
        {
            InitialHp = 3, CharacterId = "character:hua-xiong", VariantId = "shanhe-s5-newplayer-22647",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
