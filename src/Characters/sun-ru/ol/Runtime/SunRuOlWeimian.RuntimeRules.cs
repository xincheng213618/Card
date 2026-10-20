using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_sun_ru_ol_SunRuOlWeimian
{
    internal static int SunRuOlBitCount(int mask) => System.Numerics.BitOperations.PopCount((uint)mask);

    internal static string SunRuOlSlotName(EquipmentSlot s) => s switch
    { EquipmentSlot.Weapon => "武器栏", EquipmentSlot.Armor => "防具栏", EquipmentSlot.OffensiveHorse => "进攻坐骑栏", EquipmentSlot.DefensiveHorse => "防御坐骑栏", _ => "宝物栏" };

    internal static string SunRuOlBenefitName(int b) => b switch { 1 => "恢复一个装备栏", 2 => "回复1点体力", 3 => "弃置所有手牌，摸四张牌", _ => throw new InvalidOperationException() };
}
