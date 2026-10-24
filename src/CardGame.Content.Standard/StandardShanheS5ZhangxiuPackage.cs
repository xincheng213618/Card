using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete base Gun King Zhang Xiu encounter, including its fixed weapon.</summary>
public sealed class StandardShanheS5ZhangxiuPackage : IGameContentPackage
{
    public const string ZhangxiuId = "shanhe:s5-zhang-xiu-2638";
    public const string XiongluanId = "shanhe:s5-xiongluan-14116";
    public const string CongjianId = "classic:congjian";
    public const string LeizhenCardId = "shanhe:leizhen-20856";
    public const string LeizhenSkillId = "shanhe:leizhen";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-zhangxiu-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-zhangxiu");
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-leizhen", s => s with { Tags = SkillTag.Locked });
        var weapon = EquipmentCatalog.Get(CardKind.Leizhen);
        builder.AddCard(new(LeizhenCardId, weapon.DisplayName, "装备牌", weapon.Description, CardKind.Leizhen));
        // S5 chapter2 Guangcheng, boss5028 -> main battle/group2638, general2081.
        // Raw4/4, Draw2/random hand4/Slash1/Armor0/Gain0, no tactics.
        // Base20856 is an extra Spade Ace hand entity, never auto-equipped;
        // Hard20618 and the separate newcomer group22638 are excluded.
        builder.AddGeneral(new(ZhangxiuId, "张绣", "zhang_xiu", XiongluanId, "qun", 4, [CongjianId])
        {
            InitialHp = 4, CharacterId = "character:zhang-xiu", VariantId = "shanhe-s5-config-2638",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
