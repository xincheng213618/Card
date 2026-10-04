using System.Collections.Frozen;

namespace CardGame.Core;

/// <summary>
/// Whole-catalog opt-ins compiled once after registry freezing. They deliberately
/// include programs not owned or currently enabled in a match: narrowing that
/// scope would change historical nullable facts and emitted event shapes.
/// </summary>
internal sealed class SkillProgramDependencies
{
    private readonly FrozenSet<SkillProgramTriggerConditionKind> _conditions;
    private readonly FrozenSet<SkillProgramTriggerValueKind> _values;
    private readonly FrozenSet<SkillProgramEffectOp> _triggerOperations;
    private readonly FrozenSet<SkillProgramEffectOp> _activationOperations;
    private readonly FrozenDictionary<SkillProgramEffectOp, IReadOnlyList<string>> _triggerOperationSkillIds;
    private readonly FrozenSet<SkillProgramTriggerWindow> _windows;
    private readonly FrozenSet<(CardKind Kind, SkillProgramCardCategory Category)> _finalizedCards;
    private readonly int? _maximumCardPolicyKind;

    internal SkillProgramDependencies(IEnumerable<ContentSkillDefinition> skills)
    {
        var definitions = skills.ToArray();
        var programs = definitions.Select(skill => skill.Program).OfType<SkillProgram>().ToArray();
        var triggers = programs.SelectMany(program => program.Triggers).ToArray();
        UsesOwnerMarkerCount = programs.Any(program => program.Modifiers.Any(modifier => modifier.ValueExpression == SkillRuleValueExpression.OwnerMarkerCount));
        TracksCurrentTurnUseKinds = programs.Any(program =>
            program.Modifiers.Any(modifier => modifier.ValueExpression == SkillRuleValueExpression.CurrentTurnUsedHandSuitCount) ||
            program.Triggers.SelectMany(trigger => trigger.Effects).Any(effect => effect.NumberExpression == SkillProgramNumberExpression.CurrentTurnUsedCardCategoryCount));
        var resolver = ProgramInstructionResolver.Default;
        _conditions = triggers.SelectMany(trigger => resolver.Features(trigger).ConditionKinds).ToFrozenSet();
        _values = triggers.SelectMany(trigger => resolver.Features(trigger).ValueKinds).ToFrozenSet();
        _triggerOperations = triggers.SelectMany(trigger => trigger.Effects).Select(effect => effect.Op).ToFrozenSet();
        _activationOperations = programs.SelectMany(program => program.Activations)
            .SelectMany(activation => activation.Effects).Select(effect => effect.Op).ToFrozenSet();
        _triggerOperationSkillIds = definitions.Where(skill => skill.Program is not null)
            .SelectMany(skill => skill.Program!.Triggers.SelectMany(trigger => trigger.Effects)
                .Select(effect => (effect.Op, SkillId: skill.Id)))
            .GroupBy(entry => entry.Op).ToFrozenDictionary(group => group.Key,
                group => (IReadOnlyList<string>)Array.AsReadOnly(group.Select(entry => entry.SkillId)
                    .Distinct(StringComparer.Ordinal).ToArray()));
        _windows = triggers.Select(trigger => trigger.Window).ToFrozenSet();
        UsesTieredRoundConversions = programs.Any(program => program.ViewAs.Any(rule => rule.TieredRoundConversion is not null));
        UsesDynamicRoundUsage = triggers.Any(trigger => trigger.DynamicUsageLimit is not null);
        UsesRoundTracking = UsesTieredRoundConversions || UsesDynamicRoundUsage || HasActivationOperation(SkillProgramEffectOp.ScheduleFirstRoundGameUsageRefund) || HasTriggerOperation(SkillProgramEffectOp.UseRoundPricedPileDyingAlcohol) || HasTriggerOperation(SkillProgramEffectOp.DrawEndingPairThenBlockRoundIfUnequal);
        _maximumCardPolicyKind = programs.SelectMany(program => program.CardPolicies)
            .Select(policy => (int?)policy.Kind).Max();
        var finalized = triggers.Where(trigger => trigger.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized).ToArray();
        _finalizedCards = (from kind in Enum.GetValues<CardKind>()
            from category in Enum.GetValues<SkillProgramCardCategory>()
            where finalized.Any(trigger =>
                (trigger.CardKinds.Count == 0 || trigger.CardKinds.Contains(kind)) &&
                (trigger.CardCategories.Count == 0 || trigger.CardCategories.Contains(category)))
            select (kind, category)).ToFrozenSet();
        CapturesCompletedResponseSuit = triggers.Any(trigger =>
            trigger.Window == SkillProgramTriggerWindow.CardUseCompleted && trigger.IncludeResponseUses &&
            resolver.Features(trigger).UsesCondition(SkillProgramTriggerConditionKind.CardActionSuitIs));
        TracksPlayCardHistory = HasTriggerOperation(SkillProgramEffectOp.ReplaceAllSlashTargets) ||
            HasTriggerOperation(SkillProgramEffectOp.GrantRandomSkillAndSuitShield) ||
            programs.Any(program => program.ViewAs.Any(rule => rule.InheritPreviousPlaySuit)) ||
            UsesTriggerCondition(SkillProgramTriggerConditionKind.CardActionMatchesPreviousPlayCard) ||
            UsesTriggerCondition(SkillProgramTriggerConditionKind.CardActionSuitIs);
    }

    internal bool UsesOwnerMarkerCount { get; }
    internal bool TracksCurrentTurnUseKinds { get; }
    internal bool UsesDynamicRoundUsage { get; }
    internal bool UsesTieredRoundConversions { get; }
    internal bool UsesRoundTracking { get; }
    internal bool CapturesCompletedResponseSuit { get; }
    internal bool TracksPlayCardHistory { get; }
    internal bool UsesTriggerCondition(SkillProgramTriggerConditionKind kind) => _conditions.Contains(kind);
    internal bool UsesTriggerValue(SkillProgramTriggerValueKind kind) => _values.Contains(kind);
    internal bool HasTriggerOperation(SkillProgramEffectOp op) => _triggerOperations.Contains(op);
    internal bool HasActivationOperation(SkillProgramEffectOp op) => _activationOperations.Contains(op);
    internal IReadOnlyList<string> GetTriggerOperationSkillIds(SkillProgramEffectOp op) =>
        _triggerOperationSkillIds.GetValueOrDefault(op) ?? Array.Empty<string>();
    internal bool HasTriggerWindow(SkillProgramTriggerWindow window) => _windows.Contains(window);
    internal bool HasFinalizedCardTrigger(CardKind kind, SkillProgramCardCategory category) => _finalizedCards.Contains((kind, category));
    internal bool HasCardPolicyKindAtOrAbove(int minimum) => _maximumCardPolicyKind >= minimum;
}
