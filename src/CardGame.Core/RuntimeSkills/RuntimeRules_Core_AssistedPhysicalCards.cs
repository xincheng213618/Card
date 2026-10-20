namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_AssistedPhysicalCards
{
    internal static bool AssistedChoicesEqual(IReadOnlyList<PromptChoice> actual, IReadOnlyList<PromptChoice> expected) =>
        actual.Count == expected.Count && actual.Zip(expected).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Description == pair.Second.Description &&
            pair.First.Cards.SequenceEqual(pair.Second.Cards) && pair.First.Targets.SequenceEqual(pair.Second.Targets) &&
            pair.First.ContentIds.SequenceEqual(pair.Second.ContentIds) &&
            pair.First.Parameters.OrderBy(item => item.Key).SequenceEqual(pair.Second.Parameters.OrderBy(item => item.Key)));
}
