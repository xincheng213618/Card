namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_GuanduEquipment
{
    internal static string? GuanduEquipmentSkillGrantId(Card card) => card.Kind == CardKind.ThunderChariot ? $"equipment:guandu:{card.Id}" : null;

    internal static bool IsGuanduThunderChariotDeparture(Card card, CardLocation from, CardLocation to) =>
        card.Kind == CardKind.ThunderChariot && from.Zone == CardZoneKind.Equipment && from != to;
}
