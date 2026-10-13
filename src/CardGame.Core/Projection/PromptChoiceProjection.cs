using System.Collections.ObjectModel;

namespace CardGame.Core;

internal static class PromptChoiceProjection
{
    // Every projection owns a fresh choice and detached, read-only collections.
    internal static PromptChoice Freeze(PromptChoice choice) => choice with
    {
        Cards = Array.AsReadOnly(choice.Cards.ToArray()),
        Targets = Array.AsReadOnly(choice.Targets.ToArray()),
        ContentIds = Array.AsReadOnly(choice.ContentIds.ToArray()),
        Parameters = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(choice.Parameters))
    };
}
