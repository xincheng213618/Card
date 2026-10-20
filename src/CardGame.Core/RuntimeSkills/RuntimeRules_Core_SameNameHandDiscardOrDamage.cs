using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_SameNameHandDiscardOrDamage
{
    internal static bool SameNameHandChoicesEqual(PromptChoice a, PromptChoice b) => a.Id == b.Id && a.Cards.SequenceEqual(b.Cards) &&
        a.Targets.SequenceEqual(b.Targets) && a.Parameters.OrderBy(p => p.Key).SequenceEqual(b.Parameters.OrderBy(p => p.Key));
}
