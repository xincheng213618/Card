using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace CardGame.Core;

// Selection modules can query facts, but cannot obtain command/effect hosts.
internal interface IProgramSelectionQuery { }
internal interface IProgramSelectionQueries
{
    T Get<T>() where T : class, IProgramSelectionQuery;
}

internal readonly record struct ProgramSelectionContext(int OwnerSeat, SkillProgram Program, ProgramExecutionPlan Plan)
{
    internal SkillProgramActivation Activation => Plan.Activation!;
    internal ProgramInstructionFeatures Features => Plan.Features;
    internal string SkillId => Program.Id;
    internal PlayerSkillContext? PlayerContext { get; init; }
}

internal sealed record ProgramMultiCardViewAsSelection(
    IReadOnlyList<Card> Cards, CardConversionSource Source, CardKind OutputKind);

internal static class ProgramSelectionProjection
{
    internal static Card Freeze(Card card) => card with
    { GrantedSkillIds = Array.AsReadOnly(card.GrantedSkillIds.ToArray()) };
    internal static ProgramMultiCardViewAsSelection? Freeze(ProgramMultiCardViewAsSelection? selection) =>
        selection is null ? null : selection with
        { Cards = Array.AsReadOnly(selection.Cards.Select(Freeze).ToArray()) };
}

// This draft belongs only to one action query. It is never stored on a frame or
// exposed to observers; the engine freezes the finished legal action as before.
internal sealed class ProgramSelectionCandidates
{
    internal int[] Cards { get; set; } = [];
    internal int[] Targets { get; set; } = [];
    internal IReadOnlyList<ProgramMultiCardViewAsSelection> MultiCardUses { get; set; } = [];
    internal bool VariableInput { get; set; }
    internal bool AllHandInput { get; set; }
    internal IProgramSelectionSourceProvider? Source { get; set; }
    internal int MinCardCount { get; set; }
    internal int MaxCardCount { get; set; }
    internal int MaxTargetCount { get; set; }
    internal bool SelectedCardsSameNameOrAllEquipment { get; set; }
    internal IReadOnlyList<ActiveSkillCardTarget>? CardTargets { get; set; }
    internal IReadOnlyList<IReadOnlyList<int>>? TargetSequences { get; set; }
}

internal enum ProgramSelectionStage
{
    BeforeSources, Sources, CardBeforeDiscard, CardAfterDiscard, CardAfterDeclaration, CardSuit,
    OrdinaryTarget, AfterCards, AfterTargets, Availability, AfterAvailability,
    FinalTargets, Relationships, Projection, PhaseLimit, AiHint
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ProgramSelectionModuleAttribute(ProgramSelectionStage stage, int order,
    params SkillProgramEffectOp[] operations) : Attribute
{
    internal ProgramSelectionStage Stage { get; } = stage;
    internal int Order { get; } = order;
    internal IReadOnlyList<SkillProgramEffectOp> Operations { get; } = Array.AsReadOnly(operations);
}

internal interface IProgramSelectionCardFilter
{ bool Allows(ProgramSelectionContext context, Card card, IProgramSelectionQueries queries); }
internal interface IProgramSelectionTargetFilter
{ bool Allows(ProgramSelectionContext context, int targetSeat, IProgramSelectionQueries queries); }
internal interface IProgramSelectionSuitProvider
{ Suit GetSuit(ProgramSelectionContext context, Card card, IProgramSelectionQueries queries); }
internal interface IProgramSelectionSourceProvider
{
    bool TryBuild(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries);
    void BuildTargets(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries);
}
internal interface IProgramSelectionCandidateFilter
{ bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries); }
internal interface IProgramSelectionPhaseLimitProvider
{ int? GetLimit(ProgramSelectionContext context, IProgramSelectionQueries queries); }
internal interface IProgramSelectionAiDecorator
{ SkillProgramAiHint Decorate(ProgramSelectionContext context, SkillProgramAiHint hint, IProgramSelectionQueries queries); }

internal sealed class ProgramSelectionCatalog
{
    internal sealed record Registration(ProgramSelectionStage Stage, int Order, Type Type,
        IReadOnlyList<SkillProgramEffectOp> Operations, object Module);
    internal IReadOnlyList<Registration> Registrations { get; }
    private readonly ConditionalWeakTable<ProgramExecutionPlan, PlanModules> _plans = new();
    private sealed record PlanModules(IReadOnlyDictionary<ProgramSelectionStage, IReadOnlyList<object>> Stages);

    internal ProgramSelectionCatalog(IEnumerable<Type> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var registrations = new SortedDictionary<(ProgramSelectionStage, int), Registration>();
        foreach (var type in types.Select(type => type ?? throw new InvalidOperationException("A selection module type is null."))
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var attributes = type.GetCustomAttributes<ProgramSelectionModuleAttribute>().ToArray();
            if (attributes.Length == 0) continue;
            var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (!type.IsClass || !type.IsSealed || type.IsAbstract || type.IsGenericType ||
                constructors is not [var constructor] || constructor.GetParameters().Length != 0 ||
                type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Length != 0)
                throw new InvalidOperationException($"Selection module '{type.FullName}' must be a stateless sealed class with one parameterless constructor.");
            object module;
            try { module = constructor.Invoke(null); }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
            foreach (var attribute in attributes)
            {
                if (!Enum.IsDefined(attribute.Stage) || attribute.Order < 0 ||
                    attribute.Operations.Any(op => !Enum.IsDefined(op)) ||
                    attribute.Operations.Distinct().Count() != attribute.Operations.Count ||
                    !Contract(attribute.Stage).IsInstanceOfType(module))
                    throw new InvalidOperationException($"Selection module '{type.FullName}' has an invalid stage, order, operation or contract.");
                var registration = new Registration(attribute.Stage, attribute.Order, type,
                    Array.AsReadOnly(attribute.Operations.ToArray()), module);
                if (!registrations.TryAdd((attribute.Stage, attribute.Order), registration))
                    throw new InvalidOperationException($"Duplicate selection module order '{attribute.Stage}:{attribute.Order}'.");
            }
        }
        if (registrations.Count == 0) throw new InvalidOperationException("No selection modules were registered.");
        Registrations = Array.AsReadOnly(registrations.Values.ToArray());
    }

    internal IReadOnlyList<object> For(ProgramSelectionStage stage, ProgramExecutionPlan plan) =>
        _plans.GetValue(plan, current => new(new ReadOnlyDictionary<ProgramSelectionStage, IReadOnlyList<object>>(
            Registrations.Where(item => Applies(item.Operations, current.Features)).GroupBy(item => item.Stage)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<object>)Array.AsReadOnly(group.Select(item => item.Module).ToArray())))))
            .Stages.GetValueOrDefault(stage) ?? [];

    private static bool Applies(IReadOnlyList<SkillProgramEffectOp> operations, ProgramInstructionFeatures features)
    {
        if (operations.Count == 0) return true;
        for (var index = 0; index < operations.Count; index++)
            if (features.HasOperation(operations[index])) return true;
        return false;
    }

    private static Type Contract(ProgramSelectionStage stage) => stage switch
    {
        ProgramSelectionStage.Sources => typeof(IProgramSelectionSourceProvider),
        ProgramSelectionStage.CardBeforeDiscard or ProgramSelectionStage.CardAfterDiscard or ProgramSelectionStage.CardAfterDeclaration => typeof(IProgramSelectionCardFilter),
        ProgramSelectionStage.CardSuit => typeof(IProgramSelectionSuitProvider),
        ProgramSelectionStage.OrdinaryTarget => typeof(IProgramSelectionTargetFilter),
        ProgramSelectionStage.PhaseLimit => typeof(IProgramSelectionPhaseLimitProvider),
        ProgramSelectionStage.AiHint => typeof(IProgramSelectionAiDecorator),
        _ => typeof(IProgramSelectionCandidateFilter)
    };
}

internal sealed class ProgramSelectionRegistry(ProgramSelectionCatalog catalog, IProgramSelectionQueries queries)
{
    internal bool AllowsCard(ProgramSelectionStage stage, ProgramSelectionContext context, Card card)
    {
        foreach (IProgramSelectionCardFilter module in catalog.For(stage, context.Plan))
            if (!module.Allows(context, card, queries)) return false;
        return true;
    }
    internal bool AllowsTarget(ProgramSelectionContext context, int targetSeat)
    {
        foreach (IProgramSelectionTargetFilter module in catalog.For(ProgramSelectionStage.OrdinaryTarget, context.Plan))
            if (!module.Allows(context, targetSeat, queries)) return false;
        return true;
    }
    internal Suit CardSuit(ProgramSelectionContext context, Card card)
    {
        var modules = catalog.For(ProgramSelectionStage.CardSuit, context.Plan);
        if (modules.Count > 1) throw new InvalidOperationException("Multiple selection suit providers apply to one activation.");
        return modules.Count == 0 ? card.Suit : ((IProgramSelectionSuitProvider)modules[0]).GetSuit(context, card, queries);
    }
    internal bool TryBuildSource(ProgramSelectionContext context, ProgramSelectionCandidates candidates)
    {
        foreach (IProgramSelectionSourceProvider module in catalog.For(ProgramSelectionStage.Sources, context.Plan))
            if (module.TryBuild(context, candidates, queries)) { candidates.Source = module; return true; }
        return false;
    }
    internal bool TryBuildSourceTargets(ProgramSelectionContext context, ProgramSelectionCandidates candidates)
    {
        if (candidates.Source is null) return false;
        candidates.Source.BuildTargets(context, candidates, queries);
        return true;
    }
    internal bool Apply(ProgramSelectionStage stage, ProgramSelectionContext context, ProgramSelectionCandidates candidates)
    {
        foreach (IProgramSelectionCandidateFilter module in catalog.For(stage, context.Plan))
            if (!module.Apply(context, candidates, queries)) return false;
        return true;
    }
    internal int? PhaseLimit(ProgramSelectionContext context)
    {
        var modules = catalog.For(ProgramSelectionStage.PhaseLimit, context.Plan);
        return modules.Count == 0 ? context.Activation.UsesPerPhase :
            ((IProgramSelectionPhaseLimitProvider)modules[0]).GetLimit(context, queries);
    }
    internal SkillProgramAiHint DecorateAi(ProgramSelectionContext context, SkillProgramAiHint hint)
    {
        foreach (IProgramSelectionAiDecorator module in catalog.For(ProgramSelectionStage.AiHint, context.Plan))
            hint = module.Decorate(context, hint, queries);
        return hint;
    }
}

public sealed partial class GameEngine
{
    private static readonly Lazy<ProgramSelectionCatalog> ProgramSelectionMetadata = new(() =>
        new(typeof(GameEngine).Assembly.GetTypes()), true);
    private ProgramSelectionRegistry? _programSelections;
    private ProgramSelectionRegistry ProgramSelections => _programSelections ??= new(ProgramSelectionMetadata.Value, ProgramSelectionQueries);
    private IProgramSelectionQueries? _programSelectionQueries;
    private IProgramSelectionQueries ProgramSelectionQueries => _programSelectionQueries ??= new SelectionQueries(this);
    private sealed class SelectionQueries(GameEngine engine) : IProgramSelectionQueries
    { public T Get<T>() where T : class, IProgramSelectionQuery => engine.ProgramCapabilities.Get<T>(); }
}
