using CardGame.Core;

internal static class CardSubsetSelectorChecks
{
    public static void EnumeratesEveryLegalSubsetExactlyOnce()
    {
        CardSubsetCandidate[] cards = [new(7, 1, Suit.Club), new(11, 5, Suit.Club), new(20, 7, Suit.Club), new(41, 13, Suit.Club)];
        var options = CardSubsetSelector.Enumerate(cards, new(0, 4, 13));
        Require(options.Select(option => option.SelectionMask).SequenceEqual([0, 1, 2, 3, 4, 5, 6, 7, 8]),
            "All nine valid subsets must be offered once in deterministic source order.");
        Require(options[7].CardIds.SequenceEqual([7, 11, 20]) && options[7].RankSum == 13 &&
                options[8].CardIds.SequenceEqual([41]) && options[8].RankSum == 13,
            "The rank boundary includes both a three-card subset and a single king.");
        var pairs = CardSubsetSelector.Enumerate(cards, new(2, 2, 13));
        Require(pairs.Select(option => option.SelectionMask).SequenceEqual([3, 5, 6]),
            "Card-count constraints must exclude empty, single-card and three-card choices.");
    }

    public static void ImpossibleAndEmptySelectionDoNotInventChoices()
    {
        var optionalEmpty = CardSubsetSelector.Enumerate([], new(0, 4, 13));
        Require(optionalEmpty.Count == 1 && optionalEmpty[0].CardIds.Count == 0 &&
                optionalEmpty[0].SelectionMask == 0 && optionalEmpty[0].RankSum == 0,
            "An optional selection has one valid empty choice even when its source is empty.");
        Require(CardSubsetSelector.Enumerate([], new(1, 4, 13)).Count == 0 &&
                CardSubsetSelector.Enumerate([new(9, 13, Suit.Spade)], new(1, 1, 12)).Count == 0 &&
                CardSubsetSelector.Enumerate([new(9, 1, Suit.Spade)], new(2, 4, 13)).Count == 0,
            "Scarce cards and impossible rank constraints must return no choices, not bypass the minimum.");
    }

    public static void OnePerSuitSelectsExactlyOneCardOfEachDistinctSuit()
    {
        CardSubsetCandidate[] cards =
        [
            new(1, 4, Suit.Heart), new(2, 5, Suit.Heart),
            new(3, 6, Suit.Spade), new(4, 7, Suit.Club), new(5, 8, Suit.Club)
        ];
        var options = CardSubsetSelector.Enumerate(cards, new(3, 3, 208, AtMostOnePerSuit: true));
        Require(options.Select(option => (option.SelectionMask, option.RankSum)).SequenceEqual(
                new[] { (0b001101, 17), (0b010101, 18), (0b011010, 18), (0b100110, 19) }),
            "One-per-suit must pair every spade with one heart and one club, in mask order.");
        Require(options.Select(option => option.CardIds).All(cardIds =>
                cardIds.Contains(3) && cardIds.Count(cardId => cardId is 1 or 2) == 1 &&
                cardIds.Count(cardId => cardId is 4 or 5) == 1),
            "Each one-per-suit option takes the only spade plus one heart and one club.");
        Require(CardSubsetSelector.Enumerate(cards, new(0, 4, 208, AtMostOnePerSuit: true)).Count == 18,
            "Without an exact count the constraint admits every distinct-suit subset, including empty.");
    }

    public static void RejectsOversizedOrAmbiguousSourcesAndFreezesChoices()
    {
        var maximum = Enumerable.Range(1, CardSubsetSelector.MaximumCandidateCount)
            .Select(id => new CardSubsetCandidate(id, 1, Suit.Diamond)).ToList();
        var options = CardSubsetSelector.Enumerate(maximum, new(0, 8, 104));
        Require(options.Count == CardSubsetSelector.MaximumOptionCount && options.Count == 256,
            "The maximum accepted source must have a bounded 256-option worst case.");
        maximum.Clear();
        Require(options[^1].CardIds.Count == 8 && options[^1].RankSum == 8,
            "Published options must be detached from the caller's mutable candidate collection.");
        Throws<NotSupportedException>(() => ((ICollection<CardSubsetOption>)options).Clear());
        Throws<NotSupportedException>(() => ((ICollection<int>)options[^1].CardIds).Clear());
        Throws<ArgumentOutOfRangeException>(() => CardSubsetSelector.ValidateDefinition(9, new(0, 9, 117)));
        Throws<ArgumentException>(() => CardSubsetSelector.Enumerate([new(1, 3, Suit.Heart), new(1, 5, Suit.Club)], new(0, 2, 13)));
        Throws<ArgumentException>(() => CardSubsetSelector.Enumerate([new(1, 0, Suit.Heart)], new(0, 1, 13)));
        Throws<ArgumentException>(() => CardSubsetSelector.Enumerate([new(1, 14, Suit.Heart)], new(0, 1, 13)));
        Throws<ArgumentException>(() => CardSubsetSelector.ValidateDefinition(4, new(2, 1, 13)));
        Throws<ArgumentException>(() => CardSubsetSelector.ValidateDefinition(4, new(0, 1, -1)));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
