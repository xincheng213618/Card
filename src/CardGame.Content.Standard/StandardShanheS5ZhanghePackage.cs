using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The uncapped, persistent Whirl skill of S5 main group 2673.</summary>
public sealed class StandardShanheS5ZhanghePackage : IGameContentPackage
{
    public const string ZhangheId = "shanhe:s5-zhang-he-2673";
    public const string ZhouxuanId = "shanhe:s5-zhouxuan-14183";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-zhanghe-boss", CurrentVersion,
        [new("standard", StandardContentPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-zhanghe");
        // S5 chapter 11 / boss 5118 / battle and main group 2673 俊乂张郃:
        // general 357, raw 4/4, Draw 2 / hand 4 / Slash 1 / Armor 0.
        // Base skill 14183 has neither the OL five-card cap nor Play expiry.
        // The Hard fixed weapon 20631 is excluded. Qun/Male and the ordinary
        // SP portrait are local interaction/art mappings.
        builder.AddGeneral(new(ZhangheId, "张郃·俊乂", "ol-sp-zhang-he", ZhouxuanId, "qun", 4)
        {
            InitialHp = 4, CharacterId = "character:zhang-he", VariantId = "shanhe-s5-config-2673",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
