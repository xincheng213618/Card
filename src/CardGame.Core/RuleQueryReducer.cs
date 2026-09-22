namespace CardGame.Core;

public readonly record struct RuleQueryBounds(int Minimum, int Maximum);

public sealed record RuleQueryBaseTerm(string SourceId, int Value);

public sealed record RuleQueryStageSnapshot(
    string Name,
    long InputValue,
    bool IsUnlimited,
    long? OutputValue,
    IReadOnlyList<RuleQueryContribution> Contributions);

public sealed record RuleQueryEvaluation
{
    internal RuleQueryEvaluation(
        RuleQueryValue value,
        IReadOnlyList<RuleQueryBaseTerm> baseTerms,
        IReadOnlyList<RuleQueryStageSnapshot>? stages = null)
    {
        Value = value;
        BaseTerms = baseTerms;
        Stages = Array.AsReadOnly((stages ?? Array.Empty<RuleQueryStageSnapshot>()).ToArray());
    }

    public RuleQueryValue Value { get; }
    public IReadOnlyList<RuleQueryBaseTerm> BaseTerms { get; }
    public IReadOnlyList<RuleQueryStageSnapshot> Stages { get; }
    public bool IsUnlimited => Value.IsUnlimited;
}

/// <summary>
/// Describes one modifier contribution. <see cref="SourceId"/> is the stable, unique identity of
/// that contribution, so it must distinguish its source, runtime instance, and modifier node;
/// a skill ID alone is insufficient when the same skill can contribute more than one modifier.
/// </summary>
public abstract record RuleQueryContribution
{
    protected RuleQueryContribution(string sourceId) => SourceId = sourceId;

    public string SourceId { get; }
    public abstract SkillRuleOperation Operation { get; }
}

public sealed record FiniteRuleQueryContribution : RuleQueryContribution
{
    public FiniteRuleQueryContribution(
        string sourceId,
        SkillRuleOperation operation,
        int value,
        int priority = 0) : base(sourceId)
    {
        Operation = operation;
        Value = value;
        Priority = priority;
    }

    public override SkillRuleOperation Operation { get; }
    public int Value { get; }
    public int Priority { get; }
}

public sealed record UnlimitedRuleQueryContribution : RuleQueryContribution
{
    public UnlimitedRuleQueryContribution(string sourceId) : base(sourceId) { }

    public override SkillRuleOperation Operation => SkillRuleOperation.Unlimited;
}

public abstract record RuleQueryValue
{
    protected RuleQueryValue(IReadOnlyList<RuleQueryContribution> contributions) =>
        Contributions = contributions;

    /// <summary>
    /// Gets an immutable snapshot of every validated input contribution. Entries here are not
    /// necessarily effective: lower-priority Set values and all finite values under Unlimited remain visible.
    /// </summary>
    public IReadOnlyList<RuleQueryContribution> Contributions { get; }
    public abstract bool IsUnlimited { get; }
}

public sealed record FiniteRuleQueryValue : RuleQueryValue
{
    internal FiniteRuleQueryValue(int value, IReadOnlyList<RuleQueryContribution> contributions)
        : base(contributions) => Value = value;

    public int Value { get; }
    public override bool IsUnlimited => false;
}

public sealed record UnlimitedRuleQueryValue : RuleQueryValue
{
    internal UnlimitedRuleQueryValue(IReadOnlyList<RuleQueryContribution> contributions)
        : base(contributions) { }

    public override bool IsUnlimited => true;
}

public static class RuleQueryReducer
{
    internal sealed record WideResult(
        bool IsUnlimited,
        long Value,
        IReadOnlyList<RuleQueryContribution> Contributions);

    /// <summary>
    /// Reduces a finite base value and validated contributions without consulting game state.
    /// The highest-priority Set supplies the finite starting value, all Add values are summed in
    /// a wide accumulator, and the combined value is clamped once at the end. Equal highest-priority
    /// Set values merge; different values conflict. Unlimited wins over finite arithmetic and bounds,
    /// while validation, including detection of a highest-priority Set conflict, still runs first.
    /// </summary>
    public static RuleQueryValue Reduce(
        long baseValue,
        RuleQueryBounds bounds,
        IReadOnlyList<RuleQueryContribution> contributions)
    {
        if (bounds.Minimum > bounds.Maximum)
            throw new ArgumentException("The query minimum cannot exceed its maximum.", nameof(bounds));
        var wide = ReduceWide(baseValue, contributions);
        return Materialize(wide, bounds);
    }

    internal static WideResult ReduceWide(
        long baseValue,
        IReadOnlyList<RuleQueryContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        var inputs = contributions.ToArray();
        Validate(inputs);
        var ordered = Array.AsReadOnly(inputs.OrderBy(item => item.SourceId, StringComparer.Ordinal).ToArray());

        var sets = inputs.OfType<FiniteRuleQueryContribution>()
            .Where(item => item.Operation == SkillRuleOperation.Set)
            .ToArray();
        var startingValue = baseValue;
        if (sets.Length > 0)
        {
            var highestPriority = sets.Max(item => item.Priority);
            var highestSets = sets.Where(item => item.Priority == highestPriority).ToArray();
            var distinctValues = highestSets.Select(item => item.Value).Distinct().ToArray();
            if (distinctValues.Length != 1)
            {
                var sources = string.Join(", ", highestSets
                    .OrderBy(item => item.SourceId, StringComparer.Ordinal)
                    .Select(item => $"{item.SourceId}={item.Value}"));
                throw new InvalidOperationException(
                    $"Conflicting Set contributions at priority {highestPriority}: {sources}.");
            }

            startingValue = distinctValues[0];
        }

        if (inputs.Any(item => item.Operation == SkillRuleOperation.Unlimited))
            return new WideResult(true, 0, ordered);

        var addTotal = inputs.OfType<FiniteRuleQueryContribution>()
            .Where(item => item.Operation == SkillRuleOperation.Add)
            .Aggregate(0L, (total, item) => checked(total + item.Value));
        var combined = checked(startingValue + addTotal);
        return new WideResult(false, combined, ordered);
    }

    internal static RuleQueryValue Materialize(WideResult result, RuleQueryBounds bounds)
    {
        if (bounds.Minimum > bounds.Maximum)
            throw new ArgumentException("The query minimum cannot exceed its maximum.", nameof(bounds));
        if (result.IsUnlimited) return new UnlimitedRuleQueryValue(result.Contributions);
        var clamped = Math.Clamp(result.Value, bounds.Minimum, bounds.Maximum);
        return new FiniteRuleQueryValue((int)clamped, result.Contributions);
    }

    private static void Validate(IReadOnlyList<RuleQueryContribution> inputs)
    {
        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            if (input is null)
                throw new ArgumentException("A rule query contribution cannot be null.", nameof(inputs));
            if (string.IsNullOrWhiteSpace(input.SourceId) ||
                !string.Equals(input.SourceId, input.SourceId.Trim(), StringComparison.Ordinal))
                throw new ArgumentException("A contribution source must be non-empty and trimmed.", nameof(inputs));
            if (!sources.Add(input.SourceId))
                throw new ArgumentException($"Duplicate contribution source '{input.SourceId}'.", nameof(inputs));

            switch (input)
            {
                case FiniteRuleQueryContribution finite when
                    finite.Operation is SkillRuleOperation.Add or SkillRuleOperation.Set:
                    break;
                case UnlimitedRuleQueryContribution when input.Operation == SkillRuleOperation.Unlimited:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(inputs), input.Operation, "Unknown or mismatched rule query operation.");
            }
        }
    }
}

/// <summary>
/// Pure assembly service for public numeric rules. Base terms are deliberately
/// reduced before Set contributions so legacy pre-Set semantics stay explicit.
/// Directional distance uses two reducer stages and clamps only after the
/// target's incoming modifiers have run.
/// </summary>
public static class RuleQueryService
{
    public static RuleQueryEvaluation Evaluate(
        SkillRuleQuery query,
        RuleQueryBounds bounds,
        IReadOnlyList<RuleQueryBaseTerm> baseTerms,
        IReadOnlyList<RuleQueryContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(baseTerms);
        ArgumentNullException.ThrowIfNull(contributions);
        ValidateUnlimited(query, contributions);
        var frozenBaseTerms = FreezeBaseTerms(baseTerms, contributions);
        var baseValue = frozenBaseTerms.Aggregate(0L, (total, term) => checked(total + term.Value));
        var reduced = RuleQueryReducer.ReduceWide(baseValue, contributions);
        return new RuleQueryEvaluation(
            RuleQueryReducer.Materialize(reduced, bounds),
            frozenBaseTerms,
            [CreateStage("final", baseValue, reduced)]);
    }

    public static RuleQueryEvaluation EvaluateDirectionalDistance(
        IReadOnlyList<RuleQueryBaseTerm> baseTerms,
        IReadOnlyList<RuleQueryContribution> outgoingContributions,
        IReadOnlyList<RuleQueryContribution> incomingContributions)
    {
        ArgumentNullException.ThrowIfNull(outgoingContributions);
        ArgumentNullException.ThrowIfNull(incomingContributions);
        var allContributions = outgoingContributions.Concat(incomingContributions).ToArray();
        var frozenBaseTerms = FreezeBaseTerms(baseTerms, allContributions);
        ValidateUnlimited(SkillRuleQuery.OutgoingDistance, outgoingContributions);
        ValidateUnlimited(SkillRuleQuery.IncomingDistance, incomingContributions);
        var baseValue = frozenBaseTerms.Aggregate(0L, (total, term) => checked(total + term.Value));
        var outgoing = RuleQueryReducer.ReduceWide(baseValue, outgoingContributions);
        if (outgoing.IsUnlimited)
            throw new InvalidOperationException("Directional distance cannot be unlimited.");
        var incoming = RuleQueryReducer.ReduceWide(outgoing.Value, incomingContributions);
        if (incoming.IsUnlimited)
            throw new InvalidOperationException("Directional distance cannot be unlimited.");
        var finalValue = (int)Math.Clamp(incoming.Value, 1, int.MaxValue);
        var combined = Array.AsReadOnly(allContributions
            .OrderBy(item => item.SourceId, StringComparer.Ordinal)
            .ToArray());
        return new RuleQueryEvaluation(
            new FiniteRuleQueryValue(finalValue, combined),
            frozenBaseTerms,
            Array.AsReadOnly(new[]
            {
                CreateStage("outgoing", baseValue, outgoing),
                CreateStage("incoming", outgoing.Value, incoming)
            }));
    }

    private static IReadOnlyList<RuleQueryBaseTerm> FreezeBaseTerms(
        IReadOnlyList<RuleQueryBaseTerm> baseTerms,
        IReadOnlyList<RuleQueryContribution> contributions)
    {
        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var term in baseTerms)
        {
            if (term is null)
                throw new ArgumentException("A rule query base term cannot be null.", nameof(baseTerms));
            if (string.IsNullOrWhiteSpace(term.SourceId) ||
                !string.Equals(term.SourceId, term.SourceId.Trim(), StringComparison.Ordinal))
                throw new ArgumentException("A base-term source must be non-empty and trimmed.", nameof(baseTerms));
            if (!sources.Add(term.SourceId))
                throw new ArgumentException($"Duplicate rule query source '{term.SourceId}'.", nameof(baseTerms));
        }
        foreach (var contribution in contributions)
        {
            if (contribution is not null && !sources.Add(contribution.SourceId))
                throw new ArgumentException(
                    $"Duplicate rule query source '{contribution.SourceId}' across base and modifier stages.",
                    nameof(contributions));
        }
        return Array.AsReadOnly(baseTerms.OrderBy(item => item.SourceId, StringComparer.Ordinal).ToArray());
    }

    private static void ValidateUnlimited(
        SkillRuleQuery query,
        IReadOnlyList<RuleQueryContribution> contributions)
    {
        if (contributions.Any(item => item is not null && item.Operation == SkillRuleOperation.Unlimited) &&
            query is not (SkillRuleQuery.AttackRange or SkillRuleQuery.SlashLimit or
                SkillRuleQuery.SlashDistanceLimit))
            throw new ArgumentException($"Query '{query}' cannot be unlimited.", nameof(contributions));
    }

    private static RuleQueryStageSnapshot CreateStage(
        string name,
        long inputValue,
        RuleQueryReducer.WideResult result) =>
        new(name, inputValue, result.IsUnlimited, result.IsUnlimited ? null : result.Value,
            result.Contributions);
}
