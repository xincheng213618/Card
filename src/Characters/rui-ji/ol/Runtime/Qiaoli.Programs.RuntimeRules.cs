namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_rui_ji_ol_Qiaoli_Programs
{
    internal static int QiaoliWeaponRange(IReadOnlyList<Card> physicalCards)
    {
        foreach (var card in physicalCards)
        {
            if (!EquipmentCatalog.IsEquipment(card.Kind)) continue;
            var definition = EquipmentCatalog.Get(card.Kind);
            if (definition.Slot == EquipmentSlot.Weapon) return definition.WeaponAttackRange ?? 1;
        }
        return 1;
    }
}
