using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhang_shi_ping_ol_ZhangShiPingXingGu
{
    internal static bool IsXingGuMount(Card card) => EquipmentCatalog.IsEquipment(card.Kind) &&
        EquipmentCatalog.Get(card.Kind).Slot is EquipmentSlot.OffensiveHorse or EquipmentSlot.DefensiveHorse;
}
