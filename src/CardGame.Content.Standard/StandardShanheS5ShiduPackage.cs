using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>Source skills 7003/792 on explicitly local S5 Cao Xing combinations.</summary>
public sealed class StandardShanheS5ShiduPackage : IGameContentPackage
{
    public const string ShiduId = "shanhe:s5-shidu-7003";
    public const string CaoxingShiduId = "shanhe:s5-caoxing-shidu";
    public const string CaoxingTianbianId = "shanhe:s5-caoxing-shidu-tianbian";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-shidu-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-shidu");
        // Groups 2639/22639 list these extra skills with individual difficulty
        // labels. The source does not establish cumulative difficulty loadouts;
        // the two combinations below are explicitly local, user-approved recipes.
        Add(CaoxingShiduId, "曹性·识度", ["ol:zhan-wan", ShiduId], "shidu");
        Add(CaoxingTianbianId, "曹性·识度天辩", ["ol:zhan-wan", ShiduId, "classic:tianbian"], "shidu-tianbian");

        void Add(string id, string name, string[] skills, string variant) => builder.AddGeneral(
            new(id, name, "ol-cao-xing", "ol:liu-shi", "qun", 4, skills)
            {
                InitialHp = 4, CharacterId = "character:cao-xing", VariantId = "shanhe-s5-local-2639-" + variant,
                RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
            });
    }
}
