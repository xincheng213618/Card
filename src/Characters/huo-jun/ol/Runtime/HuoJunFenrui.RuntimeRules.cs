using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_huo_jun_ol_HuoJunFenrui
{
    internal static string HuoJunSlotName(EquipmentSlot s) => s switch {
        EquipmentSlot.Weapon => "武器栏", EquipmentSlot.Armor => "防具栏", EquipmentSlot.OffensiveHorse => "进攻坐骑栏", EquipmentSlot.DefensiveHorse => "防御坐骑栏", _ => "宝物栏" };
}
