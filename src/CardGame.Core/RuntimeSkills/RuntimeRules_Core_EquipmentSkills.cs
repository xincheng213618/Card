namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_EquipmentSkills
{
    internal static string? EquipmentSkillGrantId(Card card) => card.Kind switch
    {
        CardKind.XingtianAxe => $"equipment:xingtian:{card.Id}",
        _ => CardGame.Core.RuntimeSkills.RuntimeRules_Core_HulaoEquipment.HulaoEquipmentSkillGrantId(card) ?? CardGame.Core.RuntimeSkills.RuntimeRules_Core_ShenwuEquipment.ShenwuEquipmentSkillGrantId(card) ?? CardGame.Core.RuntimeSkills.RuntimeRules_Core_GuanduEquipment.GuanduEquipmentSkillGrantId(card)
    };
}
