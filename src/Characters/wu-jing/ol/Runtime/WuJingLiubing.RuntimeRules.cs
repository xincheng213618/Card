namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_wu_jing_ol_WuJingLiubing
{
    internal static bool WuJingPhysicalSlash(Card card, CardKind kind, IReadOnlyList<int> ids, IReadOnlyList<CardConversionSource>? conversions) =>
        card.Id > 0 && ids is [var id] && id == card.Id && CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsSlashCard(card.Kind) && kind == card.Kind && conversions is null or { Count: 0 };
}
