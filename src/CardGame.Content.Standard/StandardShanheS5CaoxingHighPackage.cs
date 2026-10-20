using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>Locally composed S5 Cao Xing forms using source skills 375 and 14071.</summary>
public sealed class StandardShanheS5CaoxingHighPackage : IGameContentPackage
{
    public const string ShihunId = "shanhe:s5-shihun-14071";
    public const string CaoxingJiaoziId = "shanhe:s5-caoxing-jiaozi";
    public const string CaoxingShihunId = "shanhe:s5-caoxing-shihun";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-caoxing-high-boss", CurrentVersion,
        [new("shanhe-s5-shidu-boss", StandardShanheS5ShiduPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-shihun");
        // Group 2639 lists these individual skills. Their cumulative loadouts
        // and placement in the local three-tier campaign are local composition.
        Add(CaoxingJiaoziId, "曹性·骄恣", ["ol:zhan-wan", StandardShanheS5ShiduPackage.ShiduId,
            "classic:tianbian", "classic:jiaozi"], "jiaozi");
        Add(CaoxingShihunId, "曹性·噬魂", ["ol:zhan-wan", StandardShanheS5ShiduPackage.ShiduId,
            "classic:tianbian", "classic:jiaozi", ShihunId], "shihun");

        void Add(string id, string name, string[] skills, string variant) => builder.AddGeneral(
            new(id, name, "ol-cao-xing", "ol:liu-shi", "qun", 4, skills)
            {
                InitialHp = 4, CharacterId = "character:cao-xing", VariantId = "shanhe-s5-local-2639-" + variant,
                RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
            });
    }
}
