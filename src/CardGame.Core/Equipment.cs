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
            [CardKind.DagongChe] = new(CardKind.DagongChe, "大攻车", EquipmentSlot.Treasure,
                "出牌阶段开始时，你可视为使用一张杀（不计入次数）；此杀造成伤害后，你弃置目标一张牌。升级选项：无视距离和防具；目标数+1；弃牌数+1，每个大攻车最多升级5次。未升级时不能被弃置，离开装备区时销毁。"),
            [CardKind.QiBaoDao] = new(CardKind.QiBaoDao, "七宝刀", EquipmentSlot.Weapon, "锁定技，你使用【杀】无视目标防具；你使用【杀】对未受伤目标造成伤害+1。", WeaponAttackRange: 2, IgnoresArmor: true),
            [CardKind.BaiBiDao] = new(CardKind.BaiBiDao, "百辟刀", EquipmentSlot.Weapon, "当你使用杀造成伤害后，可以获得目标一张手牌。", WeaponAttackRange: 2),
            [CardKind.ThunderChariot] = new(CardKind.ThunderChariot, "霹雳车", EquipmentSlot.Weapon,
                "造成伤害后可弃置目标防具及防御坐骑；离开装备区销毁。", WeaponAttackRange: 9),
            [CardKind.NationalStyleRobe] = new(CardKind.NationalStyleRobe, "国风玉袍", EquipmentSlot.Armor,
                "不能成为其他角色使用普通锦囊牌的目标。"),
            [CardKind.CrimsonSoulZither] = new(CardKind.CrimsonSoulZither, "赤焰镇魂琴", EquipmentSlot.Weapon,
                "造成的伤害均视为火属性。", WeaponAttackRange: 4),
            [CardKind.QimenEightTrigrams] = new(CardKind.QimenEightTrigrams, "奇门八卦", EquipmentSlot.Armor,
                "其他角色使用的杀对你无效。"),
            [CardKind.DustlessGoldenSpear] = new(CardKind.DustlessGoldenSpear, "绝尘金戈", EquipmentSlot.DefensiveHorse,
                "敌方角色计算与己方其他角色的距离+1。", IncomingDistanceModifier: 1),
            [CardKind.ShuraInfernoHalberd] = new(CardKind.ShuraInfernoHalberd, "修罗炼狱戟", EquipmentSlot.Weapon,
                "杀可额外指定任意名范围内其他角色；杀伤害+1，然后令受伤角色回复1。", WeaponAttackRange: 4),
            [CardKind.DelusionCrown] = new(CardKind.DelusionCrown, "虚妄之冕", EquipmentSlot.Treasure,
                "摸牌阶段额外摸两张牌；手牌上限-1。"),
            [CardKind.WushuangHalberd] = new(CardKind.WushuangHalberd, "无双方天戟", EquipmentSlot.Weapon,
                "杀造成伤害后，可摸1张牌或弃置目标1张牌。", WeaponAttackRange: 4),
            [CardKind.PurpleGoldCrown] = new(CardKind.PurpleGoldCrown, "束发紫金冠", EquipmentSlot.Treasure,
                "准备阶段，可对一名其他角色造成1点伤害。"),
            [CardKind.RedCottonRobe] = new(CardKind.RedCottonRobe, "红棉百花袍", EquipmentSlot.Armor,
                "防止受到的属性伤害。"),
            [CardKind.LionBelt] = new(CardKind.LionBelt, "玲珑狮蛮带", EquipmentSlot.Armor,
                "成为其他角色用牌的唯一目标后，可判定：红桃则此牌对你无效。"),
            [CardKind.Xuanjian] = new(CardKind.Xuanjian, "玄剑", EquipmentSlot.Weapon,
                "你可以将一种花色的所有手牌当普通杀使用。", WeaponAttackRange: 3),
            [CardKind.GeneralWeapon] = new(CardKind.GeneralWeapon, "武将武器", EquipmentSlot.Weapon,
                "无花色点数，攻击范围为武将体力上限，具有牌上描述含杀的非觉醒、限定、转换、主公技能。离开装备区销毁。", WeaponAttackRange: 1),
            [CardKind.RedBloodBlade] = new(CardKind.RedBloodBlade, "赤血刃", EquipmentSlot.Weapon,
                "攻击范围3（若你未拥有神御，改为0）。每回合首次你的杀对攻击范围内最远角色造成伤害时，此伤害+1。首次替换其他武器牌后，获得其技能。", WeaponAttackRange: 3),
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
            [CardKind.GhostDragonCrescentBlade] = new(
                CardKind.GhostDragonCrescentBlade,
                "鬼龙斩月刀",
                EquipmentSlot.Weapon,
                "装备至武器槽；攻击范围 3，你使用的红色杀不能被闪响应。",
                WeaponAttackRange: 3),
            [CardKind.ScarletBloodSword] = new(
                CardKind.ScarletBloodSword,
                "赤血青锋",
                EquipmentSlot.Weapon,
                "装备至武器槽；攻击范围 2，你的杀无视目标防具，目标不能使用或打出手牌响应。",
                WeaponAttackRange: 2,
                IgnoresArmor: true),
            [CardKind.ZhanhunDao] = new(
                CardKind.ZhanhunDao,
                "斩魂刀",
                EquipmentSlot.Weapon,
                "装备至武器槽；攻击范围 3。对已受伤角色造成伤害时，可防止此伤害并令其体力值上限-1。",
                WeaponAttackRange: 3),
            [CardKind.XingtianAxe] = new(
                CardKind.XingtianAxe,
                "刑天破军斧",
                EquipmentSlot.Weapon,
                "装备至武器槽；攻击范围 4，你对单一目标用牌后可弃两张牌，令其本回合不能使用或打出手牌且防具失效。",
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
            [CardKind.Dawan] = new(
                CardKind.Dawan,
                "大宛",
                EquipmentSlot.OffensiveHorse,
                "装备至进攻坐骑槽；你到其他角色的战斗距离 -1。",
                OutgoingDistanceModifier: -1),
            [CardKind.Zixing] = new(
                CardKind.Zixing,
                "紫骍",
                EquipmentSlot.OffensiveHorse,
                "装备至进攻坐骑槽；你到其他角色的战斗距离 -1。",
                OutgoingDistanceModifier: -1),
            [CardKind.Dilu] = new(
                CardKind.Dilu,
                "的卢",
                EquipmentSlot.DefensiveHorse,
                "装备至防御坐骑槽；其他角色到你的战斗距离 +1。",
                IncomingDistanceModifier: 1),
            [CardKind.Zhaohuangfeidian] = new(
                CardKind.Zhaohuangfeidian,
                "爪黄飞电",
                EquipmentSlot.DefensiveHorse,
                "装备至防御坐骑槽；其他角色到你的战斗距离 +1。",
                IncomingDistanceModifier: 1),
            [CardKind.Hualiu] = new(
                CardKind.Hualiu,
                "骅骝",
                EquipmentSlot.DefensiveHorse,
                "装备至防御坐骑槽；其他角色到你的战斗距离 +1。",
                IncomingDistanceModifier: 1),
            [CardKind.JadeSeal] = new(
                CardKind.JadeSeal,
                "玉玺",
                EquipmentSlot.Treasure,
                "装备至宝物槽；摸牌阶段额外摸一张牌。",
                DrawCountBonus: 1),
            [CardKind.WoodenOx] = new(
                CardKind.WoodenOx,
                "木牛流马",
                EquipmentSlot.Treasure,
                "出牌阶段限一次，你可以将一张手牌扣置于此牌下；若如此做，你可以将此装备移动至一名其他角色的装备区。你可使用或打出此牌下的牌。")
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
