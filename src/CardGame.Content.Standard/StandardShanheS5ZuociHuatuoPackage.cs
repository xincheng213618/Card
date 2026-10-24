using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete base roster of S5 main battle/group 2682.</summary>
public sealed class StandardShanheS5ZuociHuatuoPackage : IGameContentPackage
{
    public const string ZuociId = "shanhe:s5-zuo-ci-2682";
    public const string HuatuoId = "shanhe:s5-hua-tuo-2682";
    public const string HuashenId = "shanhe:s5-huashen-881";
    public const string XinshengId = "shanhe:s5-xinsheng-14200";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-zuoci-huatuo-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-zuoci-huatuo");
        // S5 chapter13 荥阳 / boss5138 -> main battle/group2682 奇人异士 (not novice22682).
        // Original order: boss Zuo Ci447, Hua Tuo318; both raw3/3, D2/H4/S1/Armor0.
        // All listed skills are base; neither has fixed cards, tactics, or higher-difficulty additions.
        builder.AddGeneral(new(ZuociId, "左慈", "zuo-ci", HuashenId, "qun", 3, [XinshengId])
        { InitialHp = 3, CharacterId = "character:zuo-ci", VariantId = "shanhe-s5-config-2682", RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male });
        builder.AddGeneral(new(HuatuoId, "华佗", "hua-tuo", "classic:jijiu", "qun", 3, ["boundary:chuli"])
        { InitialHp = 3, CharacterId = "character:hua-tuo", VariantId = "shanhe-s5-config-2682", RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male });
    }
}
