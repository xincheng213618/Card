using System.Collections.ObjectModel;
using System.Reflection;

namespace CardGame.Core;

// Stages retain the native query's short-circuit boundaries. In particular,
// pre-category subscribers run before any CardCatalog classification is read.
internal enum ProgramTargetWindowStage
{
    FinalizedBeforeActivity,
    FinalizedAfterCategory,
    FinalizedInstantTrickRequirement,
    ImplicitSelfDrawTwo,
    NormalizeImplicitDrawTwo,
    SoleTargetRedirectSource,
    SoleTargetReplacementFact
}

internal readonly record struct ProgramTargetCardWindowQuery(CardKind Kind, SkillProgramCardCategory? Category = null);

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
internal sealed class ProgramTargetWindowAttribute(ProgramTargetWindowStage stage, int order) : Attribute
{
    internal ProgramTargetWindowStage Stage { get; } = stage;
    internal int Order { get; } = order;
}

internal sealed record ProgramTargetWindowRegistration(int Order, MethodInfo Method, Type DelegateType);

internal sealed class ProgramTargetWindowCatalog
{
    internal IReadOnlyDictionary<ProgramTargetWindowStage, IReadOnlyList<ProgramTargetWindowRegistration>> Stages { get; }

    private ProgramTargetWindowCatalog(Dictionary<ProgramTargetWindowStage, SortedDictionary<int, ProgramTargetWindowRegistration>> entries) =>
        Stages = new ReadOnlyDictionary<ProgramTargetWindowStage, IReadOnlyList<ProgramTargetWindowRegistration>>(
            entries.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<ProgramTargetWindowRegistration>)Array.AsReadOnly(pair.Value.Values.ToArray())));

    internal static ProgramTargetWindowCatalog Discover(IEnumerable<MethodInfo> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        var entries = Enum.GetValues<ProgramTargetWindowStage>().ToDictionary(stage => stage,
            _ => new SortedDictionary<int, ProgramTargetWindowRegistration>());
        foreach (var method in methods)
        foreach (var attribute in method.GetCustomAttributes<ProgramTargetWindowAttribute>())
        {
            if (!Enum.IsDefined(attribute.Stage) || attribute.Order <= 0)
                throw new InvalidOperationException($"Target-window protocol '{method.Name}' requires a valid stage and positive order.");
            var type = DelegateType(attribute.Stage);
            var signature = type.GetMethod("Invoke")!;
            if (method.IsAbstract || method.IsGenericMethod || method.ContainsGenericParameters || method.ReturnType != signature.ReturnType ||
                !method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(signature.GetParameters().Select(parameter => parameter.ParameterType)) ||
                method.GetParameters().Any(parameter => parameter.IsOptional || parameter.IsOut || parameter.ParameterType.IsByRef))
                throw new InvalidOperationException($"Target-window protocol '{method.Name}' has an invalid handler signature.");
            if (!entries[attribute.Stage].TryAdd(attribute.Order, new(attribute.Order, method, type)))
                throw new InvalidOperationException($"Duplicate target-window protocol order '{attribute.Stage}:{attribute.Order}'.");
        }
        return new(entries);
    }

    private static Type DelegateType(ProgramTargetWindowStage stage) => stage switch
    {
        ProgramTargetWindowStage.FinalizedBeforeActivity or ProgramTargetWindowStage.FinalizedAfterCategory or
        ProgramTargetWindowStage.FinalizedInstantTrickRequirement or ProgramTargetWindowStage.ImplicitSelfDrawTwo or
        ProgramTargetWindowStage.NormalizeImplicitDrawTwo => typeof(Func<ProgramTargetCardWindowQuery, bool>),
        ProgramTargetWindowStage.SoleTargetRedirectSource => typeof(Func<CardConversionSource, bool>),
        ProgramTargetWindowStage.SoleTargetReplacementFact => typeof(Func<CardUseFrame, SoleTargetReplacedEvent, IReadOnlyList<int>, bool>),
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };
}

internal sealed class ProgramTargetWindowProtocols
{
    private readonly Dictionary<ProgramTargetWindowStage, Delegate[]> _callbacks;

    internal ProgramTargetWindowProtocols(ProgramTargetWindowCatalog catalog, Func<MethodInfo, Type, Delegate> bind)
    {
        var bound = new Dictionary<MethodInfo, Delegate>();
        _callbacks = catalog.Stages.ToDictionary(pair => pair.Key, pair => pair.Value.Select(registration =>
        {
            if (!bound.TryGetValue(registration.Method, out var callback))
                bound.Add(registration.Method, callback = bind(registration.Method, registration.DelegateType));
            return callback;
        }).ToArray());
    }

    internal bool HasCardWindow(ProgramTargetWindowStage stage, ProgramTargetCardWindowQuery query)
    {
        if (stage is not (ProgramTargetWindowStage.FinalizedBeforeActivity or ProgramTargetWindowStage.FinalizedAfterCategory or
            ProgramTargetWindowStage.FinalizedInstantTrickRequirement or ProgramTargetWindowStage.ImplicitSelfDrawTwo or
            ProgramTargetWindowStage.NormalizeImplicitDrawTwo)) throw new ArgumentOutOfRangeException(nameof(stage));
        foreach (var callback in _callbacks[stage])
            if (((Func<ProgramTargetCardWindowQuery, bool>)callback)(query)) return true;
        return false;
    }

    internal bool AllowsRedirectSource(CardConversionSource source)
    {
        foreach (var callback in _callbacks[ProgramTargetWindowStage.SoleTargetRedirectSource])
            if (((Func<CardConversionSource, bool>)callback)(source)) return true;
        return false;
    }

    internal bool AcceptsReplacementFact(CardUseFrame use, SoleTargetReplacedEvent fact, IReadOnlyList<int> before)
    {
        foreach (var callback in _callbacks[ProgramTargetWindowStage.SoleTargetReplacementFact])
            if (((Func<CardUseFrame, SoleTargetReplacedEvent, IReadOnlyList<int>, bool>)callback)(use, fact, before)) return true;
        return false;
    }
}

public sealed partial class GameEngine
{
    private static readonly Lazy<ProgramTargetWindowCatalog> TargetWindowMetadata = new(() =>
        ProgramTargetWindowCatalog.Discover(typeof(GameEngine).GetMethods(
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)), true);
    private ProgramTargetWindowProtocols? _targetWindowProtocols;
    private ProgramTargetWindowProtocols TargetWindowProtocols => _targetWindowProtocols ??= new(TargetWindowMetadata.Value,
        (method, type) => method.IsStatic ? method.CreateDelegate(type) : method.CreateDelegate(type, this));
}
