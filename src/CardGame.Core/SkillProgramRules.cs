namespace CardGame.Core;

public sealed record SkillProgramRuleContext(
    PlayerSkillContext Owner,
    int LivingFactionCount,
    Func<CardZoneKind, int>? OwnedZoneCount = null,
    CardKind? EffectiveCardKind = null);

public sealed record SkillProgramRuleSource(
    string SkillId,
    string SkillInstanceId,
    SkillProgram Program);

/// <summary>
/// Evaluates compiled numeric contributions from current program definitions.
/// </summary>
public static class SkillProgramRules
{
    public static IReadOnlyList<RuleQueryContribution> CollectContributions(
        SkillRuleQuery query,
        SkillProgramRuleContext context,
        IReadOnlyList<SkillProgramRuleSource> sources)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sources);
        if (context.LivingFactionCount < 0)
            throw new ArgumentOutOfRangeException(
                nameof(context), context.LivingFactionCount, "Living faction count cannot be negative.");

        var unique = new Dictionary<(string SkillId, string SkillInstanceId), SkillProgramRuleSource>();
        foreach (var source in sources)
        {
            if (source is null)
                throw new ArgumentException("A program rule source cannot be null.", nameof(sources));
            ArgumentException.ThrowIfNullOrWhiteSpace(source.SkillId);
            ArgumentException.ThrowIfNullOrWhiteSpace(source.SkillInstanceId);
            ArgumentNullException.ThrowIfNull(source.Program);
            if (!string.Equals(source.SkillId, source.Program.Id, StringComparison.Ordinal))
                throw new ArgumentException(
                    $"Rule source skill '{source.SkillId}' does not match program '{source.Program.Id}'.",
                    nameof(sources));
            var key = (source.SkillId, source.SkillInstanceId);
            if (unique.TryGetValue(key, out var existing))
            {
                if (!string.Equals(existing.Program.GameplayHash, source.Program.GameplayHash,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Skill instance '{source.SkillId}/{source.SkillInstanceId}' has conflicting programs.");
                continue;
            }
            unique.Add(key, source);
        }

        var contributions = unique.Values
            .OrderBy(source => source.SkillId, StringComparer.Ordinal)
            .ThenBy(source => source.SkillInstanceId, StringComparer.Ordinal)
            .SelectMany(source => source.Program.Modifiers
                .Where(modifier => modifier.Query == query &&
                                   MatchesEffectiveCardKind(modifier, context) &&
                                   modifier.Condition.Evaluate(context.Owner))
                .Select(modifier => ToContribution(
                    context.Owner.Seat,
                    source,
                    modifier,
                    context)))
            .ToArray();
        return Array.AsReadOnly(contributions);
    }

    internal static IReadOnlyList<RuleQueryContribution> CollectIndexedContributions(
        SkillRuleQuery query,
        SkillProgramRuleContext context,
        IReadOnlyList<IndexedSkillProgramModifier> bindings)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(bindings);
        if (context.LivingFactionCount < 0)
            throw new ArgumentOutOfRangeException(
                nameof(context), context.LivingFactionCount, "Living faction count cannot be negative.");

        var contributions = new List<RuleQueryContribution>(bindings.Count);
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            if (binding.Modifier.Query != query)
                throw new ArgumentException(
                    $"Indexed modifier '{binding.Source.SkillId}/{binding.Modifier.Id}' belongs to " +
                    $"{binding.Modifier.Query}, not {query}.", nameof(bindings));
            if (!MatchesEffectiveCardKind(binding.Modifier, context) ||
                !binding.Modifier.Condition.Evaluate(context.Owner)) continue;
            contributions.Add(ToContribution(
                context.Owner.Seat,
                binding.Source,
                binding.Modifier,
                context));
        }
        return Array.AsReadOnly(contributions.ToArray());
    }

    private static bool MatchesEffectiveCardKind(
        SkillProgramModifier modifier,
        SkillProgramRuleContext context) =>
        !(modifier.Query == SkillRuleQuery.SlashLimit && modifier.SourceCardIdentityId is not null) && (modifier.CardKinds.Count == 0 ||
        context.EffectiveCardKind is { } effectiveKind && modifier.CardKinds.Contains(effectiveKind));

    public static void ValidateSetModifierConflicts(IEnumerable<SkillProgram> programs)
    {
        ArgumentNullException.ThrowIfNull(programs);
        var sets = programs
            .Where(program => program is not null)
            .SelectMany(program => program.Modifiers
                .Where(modifier => modifier.Operation == SkillRuleOperation.Set)
                .Select(modifier => new { Program = program, Modifier = modifier }))
            .ToArray();
        foreach (var group in sets.GroupBy(item => (item.Modifier.Query, item.Modifier.Priority)))
        {
            var entries = group.OrderBy(item => item.Program.Id, StringComparer.Ordinal)
                .ThenBy(item => item.Modifier.Id, StringComparer.Ordinal).ToArray();
            for (var leftIndex = 0; leftIndex < entries.Length; leftIndex++)
            for (var rightIndex = leftIndex + 1; rightIndex < entries.Length; rightIndex++)
            {
                var left = entries[leftIndex];
                var right = entries[rightIndex];
                if (left.Modifier.Value == right.Modifier.Value ||
                    AreProvablyMutuallyExclusive(left.Modifier.Condition, right.Modifier.Condition))
                    continue;
                throw new InvalidOperationException(
                    $"Conflicting {group.Key.Query} Set modifiers at priority {group.Key.Priority}: " +
                    $"'{left.Program.Id}/{left.Modifier.Id}'={left.Modifier.Value} and " +
                    $"'{right.Program.Id}/{right.Modifier.Id}'={right.Modifier.Value}. " +
                    "Use distinct priorities or provably exclusive conditions.");
            }
        }
    }

    private static RuleQueryContribution ToContribution(
        int ownerSeat,
        SkillProgramRuleSource source,
        SkillProgramModifier modifier,
        SkillProgramRuleContext context)
    {
        var sourceId = CreateContributionSourceId(ownerSeat, source, modifier);
        return modifier.Operation switch
        {
            SkillRuleOperation.Add or SkillRuleOperation.Set => new FiniteRuleQueryContribution(
                sourceId,
                modifier.Operation,
                modifier.EvaluateValue(context),
                modifier.Priority),
            SkillRuleOperation.Unlimited => new UnlimitedRuleQueryContribution(sourceId),
            _ => throw new InvalidOperationException(
                $"Unsupported modifier operation '{modifier.Operation}'.")
        };
    }

    private static string CreateContributionSourceId(
        int ownerSeat,
        SkillProgramRuleSource source,
        SkillProgramModifier modifier)
    {
        static string Segment(string value) => $"{value.Length}:{value}";
        return $"skill-owner:{ownerSeat}|skill:{Segment(source.SkillId)}|" +
               $"instance:{Segment(source.SkillInstanceId)}|modifier:{Segment(modifier.Id)}";
    }

    private static bool AreProvablyMutuallyExclusive(
        SkillProgramCondition left,
        SkillProgramCondition right) =>
        (left.Kind, right.Kind) is
            (SkillProgramConditionKind.OwnTurn, SkillProgramConditionKind.NotOwnTurn) or
            (SkillProgramConditionKind.NotOwnTurn, SkillProgramConditionKind.OwnTurn) ||
        IsExactNegation(left, right) || IsExactNegation(right, left);

    private static bool IsExactNegation(SkillProgramCondition possibleNot, SkillProgramCondition other) =>
        possibleNot.Kind == SkillProgramConditionKind.Not &&
        possibleNot.Children.Count == 1 &&
        ConditionsEqual(possibleNot.Children[0], other);

    private static bool ConditionsEqual(SkillProgramCondition left, SkillProgramCondition right) =>
        left.Kind == right.Kind && left.Value == right.Value &&
        left.Children.Count == right.Children.Count &&
        left.Children.Zip(right.Children).All(pair => ConditionsEqual(pair.First, pair.Second));

}
