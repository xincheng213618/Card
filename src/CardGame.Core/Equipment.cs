namespace CardGame.Core;

/// <summary>
/// The five public equipment slots used by the K6 foundation. A player may
/// have at most one card in each slot; replacing a card moves the old physical
/// card to the discard pile before the new card enters the slot.
/// </summary>
public enum EquipmentSlot
{
    Weapon,
    Armor,
    OffensiveHorse,
    DefensiveHorse,
    Treasure
}

/// <summary>
/// Data-only modifiers for an equipment card. The engine consumes these values
/// through explicit rules queries; content does not mutate runtime state.
/// </summary>
public sealed record EquipmentDefinition(
    CardKind Kind,
    string DisplayName,
    EquipmentSlot Slot,
    string Description,
    int? WeaponAttackRange = null,
    int AttackRangeBonus = 0,
    int SlashLimitBonus = 0,
    int OutgoingDistanceModifier = 0,
    int IncomingDistanceModifier = 0,
    int DrawCountBonus = 0,
    bool IgnoresArmor = false,
    bool BlocksBlackSlash = false);

public static class EquipmentCatalog
{
    private static readonly IReadOnlyDictionary<CardKind, EquipmentDefinition> Definitions =
        new Dictionary<CardKind, EquipmentDefinition>
        {
            [CardKind.Crossbow] = new(
                CardKind.Crossbow,
                "诸葛连弩",
                EquipmentSlot.Weapon,
                "装备至武器槽；攻击范围 1，出牌阶段可使用任意数量的杀。",
                WeaponAttackRange: 1,
                AttackRangeBonus: 1,
                SlashLimitBonus: int.MaxValue),
            [CardKind.QinggangSword] = new(
                CardKind.QinggangSword,
                "青釭剑",
                EquipmentSlot.Weapon,
                "装备至武器槽；攻击范围 2，你使用杀指定目标后无视其防具。",
                WeaponAttackRange: 2,
                IgnoresArmor: true),
            [CardKind.StoneAxe] = new(
                CardKind.StoneAxe,
                "贯石斧",
                EquipmentSlot.Weapon,
                "装备至武器槽；攻击范围 3，当你的杀被闪抵消后，你可以弃置两张牌，令此杀仍造成伤害。",
                WeaponAttackRange: 3),
            [CardKind.ZhangbaSerpentSpear] = new(
                CardKind.ZhangbaSerpentSpear,
                "丈八蛇矛",
                EquipmentSlot.Weapon,
                "装备至武器槽；攻击范围 3，你可以将两张手牌当一张杀使用或打出。",
                WeaponAttackRange: 3),
            [CardKind.CixiongDoubleSwords] = new(
                CardKind.CixiongDoubleSwords,
                "雌雄双股剑",
                EquipmentSlot.Weapon,
                "装备至武器槽；攻击范围 2，使用杀指定异性目标后，可令其弃一张手牌或令你摸一张牌。",
                WeaponAttackRange: 2),
            [CardKind.QinglongCrescentBlade] = new(
                CardKind.QinglongCrescentBlade,
                "青龙偃月刀",
                EquipmentSlot.Weapon,
                "装备至武器槽；攻击范围 3，当你使用的杀被闪抵消后，你可以对同一目标再使用一张杀（无距离限制）。",
                WeaponAttackRange: 3),
            [CardKind.IceSword] = new(
                CardKind.IceSword,
                "寒冰剑",
                EquipmentSlot.Weapon,
                "装备至武器槽；攻击范围 2，当你使用杀即将造成伤害且目标有手牌或装备时，可防止此伤害并依次弃置其至多两张牌。",
                WeaponAttackRange: 2),
            [CardKind.QilinBow] = new(
                CardKind.QilinBow,
                "麒麟弓",
                EquipmentSlot.Weapon,
                "装备至武器槽；攻击范围 5，当你使用杀对目标角色造成伤害时，可弃置其装备区的一张坐骑牌。",
                WeaponAttackRange: 5),
            [CardKind.FangtianHalberd] = new(
                CardKind.FangtianHalberd,
                "方天画戟",
                EquipmentSlot.Weapon,
                "装备至武器槽；攻击范围 4，当你使用最后的手牌杀时，可额外指定至多两个合法目标。",
                WeaponAttackRange: 4),
            [CardKind.GudingBlade] = new(
                CardKind.GudingBlade,
                "古锭刀",
                EquipmentSlot.Weapon,
                "装备至武器槽；攻击范围 2，锁定技，当你使用杀对没有手牌的目标造成伤害时，此伤害 +1。",
                WeaponAttackRange: 2),
            [CardKind.ZhuqueFan] = new(
                CardKind.ZhuqueFan,
                "朱雀羽扇",
                EquipmentSlot.Weapon,
                "装备至武器槽；攻击范围 4，你可以将使用的普通杀改为火杀。",
                WeaponAttackRange: 4),
            [CardKind.BaguaFormation] = new(
                CardKind.BaguaFormation,
                "八卦阵",
                EquipmentSlot.Armor,
                "装备至防具槽；每当需要使用或打出闪时，可进行一次判定，红色判定牌视为打出闪。"),
            [CardKind.RenwangShield] = new(
                CardKind.RenwangShield,
                "仁王盾",
                EquipmentSlot.Armor,
                "装备至防具槽；黑色杀对你无效。",
                BlocksBlackSlash: true),
            [CardKind.Tengjia] = new(
                CardKind.Tengjia,
                "藤甲",
                EquipmentSlot.Armor,
                "锁定技，南蛮入侵、万箭齐发和普通杀对你无效；你受到的火焰伤害 +1。"),
            [CardKind.SilverLion] = new(
                CardKind.SilverLion,
                "白银狮子",
                EquipmentSlot.Armor,
                "锁定技，你受到大于1点的伤害时将伤害值改为1；失去装备区里的白银狮子后回复1点体力。"),
            [CardKind.OffensiveHorse] = new(
                CardKind.OffensiveHorse,
                "赤兔",
                EquipmentSlot.OffensiveHorse,
                "装备至进攻坐骑槽；你到其他角色的战斗距离 -1。",
                OutgoingDistanceModifier: -1),
            [CardKind.DefensiveHorse] = new(
                CardKind.DefensiveHorse,
                "绝影",
                EquipmentSlot.DefensiveHorse,
                "装备至防御坐骑槽；其他角色到你的战斗距离 +1。",
                IncomingDistanceModifier: 1),
            [CardKind.JadeSeal] = new(
                CardKind.JadeSeal,
                "玉玺",
                EquipmentSlot.Treasure,
                "装备至宝物槽；摸牌阶段额外摸一张牌。",
                DrawCountBonus: 1)
        };

    public static IReadOnlyList<EquipmentDefinition> Implemented { get; } =
        Definitions.Values
            .OrderBy(definition => definition.Slot)
            .ThenBy(definition => definition.Kind)
            .ToArray();

    public static bool IsEquipment(CardKind kind) => Definitions.ContainsKey(kind);

    public static EquipmentDefinition Get(CardKind kind) =>
        Definitions.TryGetValue(kind, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "No equipment content is registered for this card kind.");

    public static string GetSlotName(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Weapon => "武器",
        EquipmentSlot.Armor => "防具",
        EquipmentSlot.OffensiveHorse => "进攻坐骑",
        EquipmentSlot.DefensiveHorse => "防御坐骑",
        EquipmentSlot.Treasure => "宝物",
        _ => slot.ToString()
    };
}
