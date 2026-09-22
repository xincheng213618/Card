namespace CardGame.Core;

/// <summary>Only the physical identity and rank needed by a subset constraint.</summary>
public readonly record struct CardSubsetCandidate(int CardId, int Rank);

public sealed record CardSubsetConstraint(int MinimumCards, int MaximumCards, int MaximumRankSum);

/// <summary>The mask identifies a choice in the frozen, ordered candidate list.</summary>
public sealed record CardSubsetOption(int SelectionMask, IReadOnlyList<int> CardIds, int RankSum);

/// <summary>
/// Pure, bounded subset calculation. It neither reads game state nor creates a
/// prompt. No options means the constraint cannot be satisfied; the host owns
/// cancellation and card cleanup. Larger selections need a constraint-based UI.
/// </summary>
public static class CardSubsetSelector
{
    public const int MaximumCandidateCount = 8;
    public const int MaximumOptionCount = 1 << MaximumCandidateCount;

    /// <summary>Also used at content load time, before any cards are revealed.</summary>
    public static void ValidateDefinition(int maximumSourceCount, CardSubsetConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        if (maximumSourceCount is < 0 or > MaximumCandidateCount)
            throw new ArgumentOutOfRangeException(nameof(maximumSourceCount), maximumSourceCount,
                $"Enumerated subset selection supports at most {MaximumCandidateCount} candidates.");
        if (constraint.MinimumCards < 0 || constraint.MaximumCards < constraint.MinimumCards)
            throw new ArgumentException("Card counts must satisfy 0 <= minimum <= maximum.", nameof(constraint));
        if (constraint.MaximumRankSum < 0)
            throw new ArgumentException("The maximum rank sum must be non-negative.", nameof(constraint));
    }

    public static IReadOnlyList<CardSubsetOption> Enumerate(
        IReadOnlyList<CardSubsetCandidate> candidates,
        CardSubsetConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ValidateDefinition(candidates.Count, constraint);
        var source = candidates.ToArray();
        if (source.Select(card => card.CardId).Distinct().Count() != source.Length)
            throw new ArgumentException("Physical card identities must be unique.", nameof(candidates));
        if (source.Any(card => card.Rank is < 1 or > 13))
            throw new ArgumentException("Card ranks must be between 1 and 13.", nameof(candidates));

        var options = new List<CardSubsetOption>();
        for (var mask = 0; mask < 1 << source.Length; mask++)
        {
            var selected = new List<int>();
            var rankSum = 0;
            for (var index = 0; index < source.Length; index++)
            {
                if ((mask & (1 << index)) == 0) continue;
                selected.Add(source[index].CardId);
                rankSum += source[index].Rank;
            }
            if (selected.Count < constraint.MinimumCards || selected.Count > constraint.MaximumCards ||
                rankSum > constraint.MaximumRankSum)
                continue;
            options.Add(new CardSubsetOption(mask, Array.AsReadOnly(selected.ToArray()), rankSum));
        }
        return Array.AsReadOnly(options.ToArray());
    }
}
