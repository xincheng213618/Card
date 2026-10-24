using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete base Xu Rong and fixed weapon of S5 main group 2641.</summary>
public sealed class StandardShanheS5XuRongPackage : IGameContentPackage
{
    public const string XurongId = "shanhe:s5-xu-rong-2641";
    public const string XionghuoId = "ol:xionghuo";
    public const string ShajueId = "shanhe:s5-shajue-853";
    public const string YaodaoChitongCardId = "shanhe:yaodao-chitong-20876";
    public const string YaodaoChitongSkillId = "shanhe:yaodao-chitong";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-xurong-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-xurong", d => d with { Tags = SkillTag.Locked });
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-yaodao-chitong", d => d with { Tags = SkillTag.Locked });
        var weapon = EquipmentCatalog.Get(CardKind.YaodaoChitong);
        builder.AddCard(new(YaodaoChitongCardId, weapon.DisplayName, "装备牌", weapon.Description,
            LegacyKind: CardKind.YaodaoChitong));
        // S5 chapter 3 Hangu / boss 5038 / main battle and group 2641:
        // general417, raw4/4, D2/H4/S1/Armor0, no growth or tactics.
        // Exclude Hard939 and Nightmare875. Base20876 is an extra Spade6
        // hand entity in the encounter recipe, equipped only by native Use.
        builder.AddGeneral(new(XurongId, "徐荣", "ol-xu-rong", XionghuoId, "qun", 4, [ShajueId])
        {
            InitialHp = 4, CharacterId = "character:xu-rong", VariantId = "shanhe-s5-config-2641",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
