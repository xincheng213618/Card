using System.Text.Json.Serialization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhuge_guo_le_LeChengyanEffects
{
    // CardKind and Action remain the paid declaration. This query is used only
    // by native effect continuations, never by legality, quotas or Use ledgers.
    internal static CardKind LeChengyanNativeEffectKind(CardUseFrame use) => use.LeChengyanEffect is { Started: true, Finished: false } p ? p.EffectKind : use.CardKind;

    internal static IReadOnlyList<int> LeChengyanNativeTargets(CardUseFrame use) => use.LeChengyanEffect is { Started: true, Finished: false } p ? p.Targets : use.TargetSeats;
}
