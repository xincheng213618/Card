using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The base general and skills of S5 main group 2637.</summary>
public sealed class StandardShanheS5GuosiPackage : IGameContentPackage
{
    public const string GuosiId = "shanhe:s5-guo-si-2637";
    public const string ZhanhunDaoCardId = "shanhe:zhanhun-dao-20871";
    public const string ZhanhunDaoSkillId = "shanhe:zhanhun-dao";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-guosi-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-zhanhun-dao");
        var weapon = EquipmentCatalog.Get(CardKind.ZhanhunDao);
        builder.AddCard(new ContentCardDefinition(ZhanhunDaoCardId, weapon.DisplayName, "装备牌", weapon.Description,
            LegacyKind: CardKind.ZhanhunDao));
        // S5 chapter 2 / boss 5028 / main battle/group 2637 祸国凶贼:
        // general 424, raw 4/4, Draw2/Hand4/Slash1/Armor0, no tactics.
        // 863 Tanbei and 864 Sidao exactly reuse the ordinary OL programs.
        // Hard 14058/20841 and Inferno 14075 are excluded. Base fixed
        // card 20871 is a separate encounter resource, not a general skill.
        builder.AddGeneral(new(GuosiId, "郭汜", "ol-guo-si", "ol:tanbei", "qun", 4, ["ol:sidao"])
        {
            InitialHp = 4, CharacterId = "character:guo-si", VariantId = "shanhe-s5-config-2637",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
