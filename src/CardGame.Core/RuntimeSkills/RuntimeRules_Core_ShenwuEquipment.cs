namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ShenwuEquipment
{
    internal static string? ShenwuEquipmentSkillId(CardKind kind) => kind switch
    {
        CardKind.ShuraInfernoHalberd => "shenwu:shura-inferno-halberd",
        CardKind.DelusionCrown => "shenwu:delusion-crown",
        _ => null
    };

    internal static string? ShenwuEquipmentSkillGrantId(Card card) =>
        CardGame.Core.RuntimeSkills.RuntimeRules_Core_ShenwuEquipment.ShenwuEquipmentSkillId(card.Kind) is null ? null : $"equipment:shenwu:{card.Id}";

    internal static (CardKind Kind, CardKind? Replaces)? ShenwuGodEquipment(string? family) => family switch
    {
        "character:guan-yu" => (CardKind.GhostDragonCrescentBlade, CardKind.QinglongCrescentBlade),
        "character:lu-meng" => (CardKind.NationalStyleRobe, null),
        "character:zhou-yu" => (CardKind.CrimsonSoulZither, CardKind.ZhuqueFan),
        "character:zhuge-liang" => (CardKind.QimenEightTrigrams, CardKind.BaguaFormation),
        "character:cao-cao" => (CardKind.DustlessGoldenSpear, CardKind.DefensiveHorse),
        "character:lu-bu" => (CardKind.ShuraInfernoHalberd, CardKind.FangtianHalberd),
        "character:sima-yi" => (CardKind.DelusionCrown, null),
        "character:zhao-yun" => (CardKind.ScarletBloodSword, CardKind.QinggangSword),
        _ => null
    };

    internal static string? ShenwuLegacyGodFamily(string id) => id switch
    {
        "classic:shen-guan-yu" => "character:guan-yu", "classic:shen-lu-meng" => "character:lu-meng",
        "classic:shen-zhou-yu" => "character:zhou-yu", "classic:shen-cao-cao" => "character:cao-cao",
        "classic:shen-lu-bu" => "character:lu-bu", "classic:shen-sima-yi" => "character:sima-yi",
        _ => null
    };
}
