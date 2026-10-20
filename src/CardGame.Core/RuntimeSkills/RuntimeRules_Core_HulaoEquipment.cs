namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_HulaoEquipment
{
    internal static string? HulaoEquipmentSkillId(CardKind kind) => kind switch
    {
        CardKind.WushuangHalberd => "hulao:wushuang-halberd",
        CardKind.PurpleGoldCrown => "hulao:purple-gold-crown",
        CardKind.RedCottonRobe => "hulao:red-cotton-robe",
        CardKind.LionBelt => "hulao:lion-belt",
        _ => null
    };

    internal static string? HulaoEquipmentSkillGrantId(Card card) =>
        CardGame.Core.RuntimeSkills.RuntimeRules_Core_HulaoEquipment.HulaoEquipmentSkillId(card.Kind) is null ? null : $"equipment:hulao:{card.Id}";
}
