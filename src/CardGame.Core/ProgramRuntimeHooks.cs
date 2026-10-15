using System.Collections.ObjectModel;
using System.Reflection;

namespace CardGame.Core;

// These phases are positions in the existing scheduler, not interchangeable
// priorities. In particular, paid response-health cursors stay between the two
// subtree phases, and selection validation stays between the invariant phases.
internal enum ProgramRuntimePhase
{
    BeforeDeclaration,
    BeforeResponseHealth,
    AfterResponseHealth,
    AfterOwnerDeath,
    AfterSkillLoss
}

internal enum ProgramFrameInvariantPhase { BeforeSelectionState, AfterSelectionState }

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ProgramRuntimeHookAttribute(ProgramRuntimePhase phase, int order) : Attribute
{
    internal ProgramRuntimePhase Phase { get; } = phase;
    internal int Order { get; } = order;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ProgramFrameInvariantAttribute(ProgramFrameInvariantPhase phase, int order) : Attribute
{
    internal ProgramFrameInvariantPhase Phase { get; } = phase;
    internal int Order { get; } = order;
}

internal sealed record ProgramRuntimeRegistration(MethodInfo Method, Type DelegateType);

// Static discovery retains immutable metadata only. All match-bound delegates
// are created below, once per engine, and never enter a frame or a checkpoint.
internal sealed class ProgramRuntimeHookCatalog
{
    internal IReadOnlyDictionary<ProgramRuntimePhase, IReadOnlyList<ProgramRuntimeRegistration>> Hooks { get; }
    internal IReadOnlyDictionary<ProgramFrameInvariantPhase, IReadOnlyList<ProgramRuntimeRegistration>> Invariants { get; }

    private ProgramRuntimeHookCatalog(
        Dictionary<ProgramRuntimePhase, SortedDictionary<int, ProgramRuntimeRegistration>> hooks,
        Dictionary<ProgramFrameInvariantPhase, SortedDictionary<int, ProgramRuntimeRegistration>> invariants)
    {
        Hooks = Freeze(hooks);
        Invariants = Freeze(invariants);
    }

    internal static ProgramRuntimeHookCatalog Discover(IEnumerable<MethodInfo> methods)
    {
        var hooks = Enum.GetValues<ProgramRuntimePhase>().ToDictionary(p => p,
            _ => new SortedDictionary<int, ProgramRuntimeRegistration>());
        var invariants = Enum.GetValues<ProgramFrameInvariantPhase>().ToDictionary(p => p,
            _ => new SortedDictionary<int, ProgramRuntimeRegistration>());
        foreach (var method in methods.OrderBy(m => m.DeclaringType?.FullName, StringComparer.Ordinal)
                     .ThenBy(m => m.Name, StringComparer.Ordinal))
        {
            foreach (var attribute in method.GetCustomAttributes<ProgramRuntimeHookAttribute>())
            {
                if (!Enum.IsDefined(attribute.Phase) || attribute.Order <= 0)
                    throw new InvalidOperationException($"Runtime hook '{method.Name}' requires a valid phase and positive order.");
                var permission = IsPermission(attribute.Phase);
                var parameterTypes = permission ? new[] { typeof(ProgramSkillFrame) } : Type.EmptyTypes;
                if (method.IsStatic || !ValidSignature(method, typeof(bool), parameterTypes)) throw Invalid(method);
                var registration = new ProgramRuntimeRegistration(method,
                    permission ? typeof(Func<ProgramSkillFrame, bool>) : typeof(Func<bool>));
                if (!hooks[attribute.Phase].TryAdd(attribute.Order, registration))
                    throw new InvalidOperationException($"Duplicate runtime hook order '{attribute.Phase}:{attribute.Order}'.");
            }
            foreach (var attribute in method.GetCustomAttributes<ProgramFrameInvariantAttribute>())
            {
                if (!Enum.IsDefined(attribute.Phase) || attribute.Order <= 0)
                    throw new InvalidOperationException($"Frame invariant '{method.Name}' requires a valid phase and positive order.");
                var withPlan = method.GetParameters().Length == 2;
                var parameterTypes = withPlan
                    ? new[] { typeof(ProgramSkillFrame), typeof(ProgramExecutionPlan) }
                    : new[] { typeof(ProgramSkillFrame) };
                if (!ValidSignature(method, typeof(void), parameterTypes)) throw Invalid(method);
                var registration = new ProgramRuntimeRegistration(method, withPlan
                    ? typeof(Action<ProgramSkillFrame, ProgramExecutionPlan>) : typeof(Action<ProgramSkillFrame>));
                if (!invariants[attribute.Phase].TryAdd(attribute.Order, registration))
                    throw new InvalidOperationException($"Duplicate frame invariant order '{attribute.Phase}:{attribute.Order}'.");
            }
        }
        foreach (var pair in hooks)
            if (pair.Value.Count == 0) throw new InvalidOperationException($"Runtime hook phase '{pair.Key}' has no registrations.");
        foreach (var pair in invariants)
            if (pair.Value.Count == 0) throw new InvalidOperationException($"Frame invariant phase '{pair.Key}' has no registrations.");
        return new(hooks, invariants);
    }

    internal static bool IsPermission(ProgramRuntimePhase phase) =>
        phase is ProgramRuntimePhase.AfterOwnerDeath or ProgramRuntimePhase.AfterSkillLoss;

    private static bool ValidSignature(MethodInfo method, Type result, Type[] parameterTypes) =>
        !method.IsGenericMethod && !method.ContainsGenericParameters && method.ReturnType == result &&
        method.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameterTypes) &&
        method.GetParameters().All(p => !p.IsOptional && !p.IsOut && !p.ParameterType.IsByRef);

    private static InvalidOperationException Invalid(MethodInfo method) =>
        new($"Runtime registration '{method.DeclaringType?.Name}.{method.Name}' has an invalid handler signature.");

    private static IReadOnlyDictionary<TPhase, IReadOnlyList<ProgramRuntimeRegistration>> Freeze<TPhase>(
        Dictionary<TPhase, SortedDictionary<int, ProgramRuntimeRegistration>> values) where TPhase : notnull =>
        new ReadOnlyDictionary<TPhase, IReadOnlyList<ProgramRuntimeRegistration>>(values.ToDictionary(
            p => p.Key, p => (IReadOnlyList<ProgramRuntimeRegistration>)Array.AsReadOnly(p.Value.Values.ToArray())));
}

internal sealed class ProgramRuntimeHooks
{
    private readonly Dictionary<ProgramRuntimePhase, Delegate[]> _hooks;
    private readonly Dictionary<ProgramFrameInvariantPhase, Action<ProgramSkillFrame, ProgramExecutionPlan>[]> _invariants;

    internal ProgramRuntimeHooks(ProgramRuntimeHookCatalog catalog, Func<MethodInfo, Type, Delegate> bind)
    {
        var bound = new Dictionary<MethodInfo, Delegate>();
        Delegate Bind(ProgramRuntimeRegistration registration)
        {
            if (!bound.TryGetValue(registration.Method, out var callback))
                bound.Add(registration.Method, callback = bind(registration.Method, registration.DelegateType));
            return callback;
        }
        _hooks = catalog.Hooks.ToDictionary(p => p.Key, p => p.Value.Select(Bind).ToArray());
        _invariants = catalog.Invariants.ToDictionary(p => p.Key, p => p.Value.Select(registration =>
        {
            var callback = Bind(registration);
            return callback is Action<ProgramSkillFrame, ProgramExecutionPlan> withPlan
                ? withPlan : Adapt((Action<ProgramSkillFrame>)callback);
        }).ToArray());
    }

    internal bool TryAdvance(ProgramRuntimePhase phase)
    {
        if (ProgramRuntimeHookCatalog.IsPermission(phase)) throw new ArgumentOutOfRangeException(nameof(phase));
        foreach (var callback in _hooks[phase]) if (((Func<bool>)callback)()) return true;
        return false;
    }

    internal bool CanContinue(ProgramRuntimePhase phase, ProgramSkillFrame frame)
    {
        if (!ProgramRuntimeHookCatalog.IsPermission(phase)) throw new ArgumentOutOfRangeException(nameof(phase));
        foreach (var callback in _hooks[phase]) if (((Func<ProgramSkillFrame, bool>)callback)(frame)) return true;
        return false;
    }

    internal void AssertFrame(ProgramFrameInvariantPhase phase, ProgramSkillFrame frame, ProgramExecutionPlan plan)
    {
        foreach (var callback in _invariants[phase]) callback(frame, plan);
    }

    private static Action<ProgramSkillFrame, ProgramExecutionPlan> Adapt(Action<ProgramSkillFrame> callback) =>
        (frame, _) => callback(frame);
}

public sealed partial class GameEngine
{
    private static readonly Lazy<ProgramRuntimeHookCatalog> RuntimeHookMetadata = new(() =>
        ProgramRuntimeHookCatalog.Discover(typeof(GameEngine).GetMethods(
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)), true);
    private ProgramRuntimeHooks? _runtimeHooks;
    private ProgramRuntimeHooks RuntimeHooks => _runtimeHooks ??= new(RuntimeHookMetadata.Value,
        (method, type) => method.IsStatic ? method.CreateDelegate(type) : method.CreateDelegate(type, this));
}
