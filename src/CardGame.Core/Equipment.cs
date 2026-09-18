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
