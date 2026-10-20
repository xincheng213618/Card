using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The separately configured Gaoshun skill form containing source skill 14055.</summary>
public sealed class StandardShanheS5ChengxiongPackage : IGameContentPackage
{
    public const string GaoshunId = "shanhe:s5-gaoshun-chengxiong";
    public const string ChengxiongId = "shanhe:s5-chengxiong-14055";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-chengxiong-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-chengxiong", skill => skill with { Tags = SkillTag.Locked });
        // Groups 2680 / 22680 both give general 604 these base values. The
        // extra skill is labelled nightmare / hard respectively, not a local
        // three-tier growth formula. Qiongmu and an initial equipment zone
        // are not inferred from the separate higher-difficulty entries.
        builder.AddGeneral(new(GaoshunId, "高顺·逞凶", "gao_shun",
            "boundary:xianzhen-current", "qun", 4, ["boundary:jinjiu-current", ChengxiongId])
        {
            InitialHp = 4, CharacterId = "character:gao-shun", VariantId = "shanhe-s5-config-2680-chengxiong",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
