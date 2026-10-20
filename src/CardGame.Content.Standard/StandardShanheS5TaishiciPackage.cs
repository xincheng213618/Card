using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete base form shared by S5 groups 2632 and 22632.</summary>
public sealed class StandardShanheS5TaishiciPackage : IGameContentPackage
{
    public const string TaishiciId = "shanhe:s5-taishici-dulie-2632";
    public const string DulieId = "shanhe:s5-dulie-3419";
    public const string DouchanId = "shanhe:s5-douchan-14103";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-taishici-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-taishici",
            skill => skill.Id == DouchanId ? skill with { Tags = SkillTag.Locked } : skill);
        // Raw groups agree on 4/4 and these two skills. Faction/gender are local
        // mappings. Fixed Iron Armor has no initial-zone evidence, so add none.
        builder.AddGeneral(new(TaishiciId, "太史慈·笃烈之士", "taishi_ci", DulieId, "wu", 4, [DouchanId])
        {
            InitialHp = 4, CharacterId = "character:taishi-ci", VariantId = "shanhe-s5-dulie-2632",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
