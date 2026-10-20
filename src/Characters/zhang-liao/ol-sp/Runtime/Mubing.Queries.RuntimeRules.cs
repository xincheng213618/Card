namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhang_liao_ol_sp_Mubing_Queries
{
    internal static bool IsMubingMilitaryCard(Card card) => CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsSlashCard(card.Kind) ||
        card.Kind is CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.UnexpectedAssault ||
        EquipmentCatalog.IsEquipment(card.Kind) && EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon;
}
