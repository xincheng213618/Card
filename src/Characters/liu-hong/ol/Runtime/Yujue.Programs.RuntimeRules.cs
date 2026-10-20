namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_liu_hong_ol_Yujue_Programs
{
    internal static string YujueSlotDisplayName(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Weapon => "武器栏",
        EquipmentSlot.Armor => "防具栏",
        EquipmentSlot.OffensiveHorse => "进攻坐骑栏",
        EquipmentSlot.DefensiveHorse => "防御坐骑栏",
        EquipmentSlot.Treasure => "宝物栏",
        _ => throw new InvalidOperationException($"Unknown equipment slot '{slot}'.")
    };
}
