using System.Reflection;

namespace CardGame.Core;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class ProgramPhaseUsedProjectionAttribute(int order) : Attribute
{
    internal int Order { get; } = order;
}

internal sealed record ProgramPhaseUsedProjectionRegistration(int Order, MethodInfo Method);

// Shared metadata contains no match state. A projection receives the previous
// value, including zero, and every registered projection runs in explicit order.
internal sealed class ProgramPhaseUsedProjectionCatalog
{
    internal IReadOnlyList<ProgramPhaseUsedProjectionRegistration> Registrations { get; }

    private ProgramPhaseUsedProjectionCatalog(IEnumerable<ProgramPhaseUsedProjectionRegistration> registrations) =>
        Registrations = Array.AsReadOnly(registrations.ToArray());

    internal static ProgramPhaseUsedProjectionCatalog Discover(IEnumerable<MethodInfo> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        var registrations = new SortedDictionary<int, ProgramPhaseUsedProjectionRegistration>();
        foreach (var method in methods)
        {
            var attribute = method.GetCustomAttribute<ProgramPhaseUsedProjectionAttribute>();
            if (attribute is null) continue;
            if (attribute.Order <= 0)
                throw new InvalidOperationException($"Phase-used projection '{method.Name}' requires a positive order.");
            if (method.IsAbstract || method.IsGenericMethod || method.ContainsGenericParameters || method.ReturnType != typeof(int) ||
                !method.GetParameters().Select(p => p.ParameterType).SequenceEqual([typeof(int), typeof(string), typeof(int)]) ||
                method.GetParameters().Any(p => p.IsOptional || p.IsOut || p.ParameterType.IsByRef))
                throw new InvalidOperationException($"Phase-used projection '{method.Name}' has an invalid handler signature.");
            if (!registrations.TryAdd(attribute.Order, new(attribute.Order, method)))
                throw new InvalidOperationException($"Duplicate phase-used projection order '{attribute.Order}' at '{method.Name}'.");
        }
        return new(registrations.Values);
    }
}

internal sealed class ProgramPhaseUsedProjections
{
    private readonly Func<int, string, int, int>[] _projections;

    internal ProgramPhaseUsedProjections(ProgramPhaseUsedProjectionCatalog catalog,
        Func<MethodInfo, Func<int, string, int, int>> bind) =>
        _projections = catalog.Registrations.Select(registration => bind(registration.Method)).ToArray();

    internal int Apply(int ownerSeat, string skillId, int used)
    {
        foreach (var project in _projections) used = project(ownerSeat, skillId, used);
        return used;
    }
}

public sealed partial class GameEngine
{
    private static readonly Lazy<ProgramPhaseUsedProjectionCatalog> PhaseUsedProjectionMetadata = new(() =>
        ProgramPhaseUsedProjectionCatalog.Discover(typeof(GameEngine).GetMethods(
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)), true);
    private ProgramPhaseUsedProjections? _phaseUsedProjections;
    private ProgramPhaseUsedProjections PhaseUsedProjections => _phaseUsedProjections ??= new(PhaseUsedProjectionMetadata.Value,
        method => method.IsStatic ? method.CreateDelegate<Func<int, string, int, int>>() :
            method.CreateDelegate<Func<int, string, int, int>>(this));
}
