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
    private readonly FrozenSet<SkillProgramTriggerWindow> _windows;
    private readonly FrozenSet<(CardKind Kind, SkillProgramCardCategory Category)> _finalizedCards;
    private readonly int? _maximumCardPolicyKind;

    internal SkillProgramDependencies(IEnumerable<ContentSkillDefinition> skills)
    {
        var programs = skills.Select(skill => skill.Program).OfType<SkillProgram>().ToArray();
        var triggers = programs.SelectMany(program => program.Triggers).ToArray();
        var resolver = ProgramInstructionResolver.Default;
        _conditions = triggers.SelectMany(trigger => resolver.Features(trigger).ConditionKinds).ToFrozenSet();
        _values = triggers.SelectMany(trigger => resolver.Features(trigger).ValueKinds).ToFrozenSet();
        _triggerOperations = triggers.SelectMany(trigger => trigger.Effects).Select(effect => effect.Op).ToFrozenSet();
        _windows = triggers.Select(trigger => trigger.Window).ToFrozenSet();
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

    internal bool CapturesCompletedResponseSuit { get; }
    internal bool TracksPlayCardHistory { get; }
    internal bool UsesTriggerCondition(SkillProgramTriggerConditionKind kind) => _conditions.Contains(kind);
    internal bool UsesTriggerValue(SkillProgramTriggerValueKind kind) => _values.Contains(kind);
    internal bool HasTriggerOperation(SkillProgramEffectOp op) => _triggerOperations.Contains(op);
    internal bool HasTriggerWindow(SkillProgramTriggerWindow window) => _windows.Contains(window);
    internal bool HasFinalizedCardTrigger(CardKind kind, SkillProgramCardCategory category) => _finalizedCards.Contains((kind, category));
    internal bool HasCardPolicyKindAtOrAbove(int minimum) => _maximumCardPolicyKind >= minimum;
}
