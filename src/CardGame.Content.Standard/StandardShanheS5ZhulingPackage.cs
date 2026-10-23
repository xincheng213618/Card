using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The two base skills of S5 main group 2664.</summary>
public sealed class StandardShanheS5ZhulingPackage : IGameContentPackage
{
    public const string ZhulingId = "shanhe:s5-zhu-ling-2664";
    public const string JixianId = "shanhe:s5-jixian-3056";
    public const string BaoliId = "shanhe:s5-baoli-3327";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-zhuling-boss", CurrentVersion,
        [new("standard", StandardContentPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-zhuling",
            tagOverrides: new Dictionary<string, SkillTag> { [BaoliId] = SkillTag.Locked });
        // Main group 2664/general 489: 4/4, Draw2/Hand4/Slash1/Armor0.
        // Nightmare skill 374 and fixed card 20871 are excluded. Wei/Male and
        // the existing ordinary portrait are local interaction/art mappings.
        builder.AddGeneral(new(ZhulingId, "朱灵·悍将", "ol-zhu-ling", JixianId, "wei", 4, [BaoliId])
        {
            InitialHp = 4, CharacterId = "character:zhu-ling", VariantId = "shanhe-s5-config-2664",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
