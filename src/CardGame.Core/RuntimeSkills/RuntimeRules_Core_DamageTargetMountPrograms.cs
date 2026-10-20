using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_DamageTargetMountPrograms
{
    internal static bool IsPrintedMount(CardKind kind) => EquipmentCatalog.IsEquipment(kind) &&
        EquipmentCatalog.Get(kind).Slot is EquipmentSlot.OffensiveHorse or EquipmentSlot.DefensiveHorse;
}
