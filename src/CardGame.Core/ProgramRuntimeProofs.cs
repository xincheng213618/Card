using System.Collections.ObjectModel;
using System.Reflection;

namespace CardGame.Core;

// These are the original admission positions, not new gameplay timings. Keeping
// them separate preserves the checks before/after native debt and payment rules.
internal enum ProgramObserverProofPhase { PaidDamage, NestedDamage, BeforeDamagePrelude, BeforeDamage }
internal enum ProgramNestedDamageProofPhase { BeforeFactionDebts, AfterFactionDebts }
internal enum ProgramMovementProofPhase { NativeCost, BeforePrivatePayments, PrivatePayments, EffectPayments, BoundEquipment }
internal enum ProgramDyingProofPhase { Invariant, Runtime }

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ProgramObserverProofAttribute(ProgramObserverProofPhase phase, int order) : Attribute
{
    internal ProgramObserverProofPhase Phase { get; } = phase;
    internal int Order { get; } = order;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ProgramNestedDamageProofAttribute(ProgramNestedDamageProofPhase phase, int order) : Attribute
{
    internal ProgramNestedDamageProofPhase Phase { get; } = phase;
    internal int Order { get; } = order;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ProgramMovementProofAttribute(ProgramMovementProofPhase phase, int order) : Attribute
{
    internal ProgramMovementProofPhase Phase { get; } = phase;
    internal int Order { get; } = order;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ProgramDyingProofAttribute(ProgramDyingProofPhase phase, int order, int group = 200) : Attribute
{
    internal ProgramDyingProofPhase Phase { get; } = phase;
    internal int Order { get; } = order;
    internal int Group { get; } = group;
}

internal delegate bool ProgramNestedDamageProof(ProgramSkillFrame frame, int targetSeat, int amount,
    ProgramParticipantReference? source, DamageNature? nature, bool sourceLess);

internal sealed class ProgramRuntimeProofCatalog
{
    internal IReadOnlyDictionary<ProgramObserverProofPhase, IReadOnlyList<ProgramRuntimeRegistration>> Observers { get; }
    internal IReadOnlyDictionary<ProgramNestedDamageProofPhase, IReadOnlyList<ProgramRuntimeRegistration>> NestedDamage { get; }
    internal IReadOnlyDictionary<ProgramMovementProofPhase, IReadOnlyList<ProgramRuntimeRegistration>> Movements { get; }
    internal IReadOnlyDictionary<ProgramDyingProofPhase, IReadOnlyList<IReadOnlyList<ProgramRuntimeRegistration>>> Dying { get; }

    private ProgramRuntimeProofCatalog(
        Dictionary<ProgramObserverProofPhase, SortedDictionary<int, ProgramRuntimeRegistration>> observers,
        Dictionary<ProgramNestedDamageProofPhase, SortedDictionary<int, ProgramRuntimeRegistration>> nestedDamage,
        Dictionary<ProgramMovementProofPhase, SortedDictionary<int, ProgramRuntimeRegistration>> movements,
        Dictionary<ProgramDyingProofPhase, SortedDictionary<int, SortedDictionary<int, ProgramRuntimeRegistration>>> dying)
    {
        Observers = Freeze(observers);
        NestedDamage = Freeze(nestedDamage);
        Movements = Freeze(movements);
        Dying = new ReadOnlyDictionary<ProgramDyingProofPhase, IReadOnlyList<IReadOnlyList<ProgramRuntimeRegistration>>>(
            dying.ToDictionary(p => p.Key, p => (IReadOnlyList<IReadOnlyList<ProgramRuntimeRegistration>>)Array.AsReadOnly(
                p.Value.Values.Select(group => (IReadOnlyList<ProgramRuntimeRegistration>)Array.AsReadOnly(group.Values.ToArray())).ToArray())));
    }

    internal static ProgramRuntimeProofCatalog Discover(IEnumerable<MethodInfo> methods)
    {
        var observers = Empty<ProgramObserverProofPhase>();
        var nestedDamage = Empty<ProgramNestedDamageProofPhase>();
        var movements = Empty<ProgramMovementProofPhase>();
        var dying = Enum.GetValues<ProgramDyingProofPhase>().ToDictionary(p => p,
            _ => new SortedDictionary<int, SortedDictionary<int, ProgramRuntimeRegistration>>());
        foreach (var method in methods)
        {
            foreach (var attribute in method.GetCustomAttributes<ProgramObserverProofAttribute>())
                Add(observers, attribute.Phase, attribute.Order, method, typeof(Func<long, bool>), [typeof(long)]);
            foreach (var attribute in method.GetCustomAttributes<ProgramNestedDamageProofAttribute>())
                Add(nestedDamage, attribute.Phase, attribute.Order, method, typeof(ProgramNestedDamageProof),
                    [typeof(ProgramSkillFrame), typeof(int), typeof(int), typeof(ProgramParticipantReference), typeof(DamageNature?), typeof(bool)]);
            foreach (var attribute in method.GetCustomAttributes<ProgramMovementProofAttribute>())
                Add(movements, attribute.Phase, attribute.Order, method,
                    typeof(Func<ProgramSkillFrame, SkillProgramEffect?, ProgramMovementContinuation, bool>),
                    [typeof(ProgramSkillFrame), typeof(SkillProgramEffect), typeof(ProgramMovementContinuation)]);
            foreach (var attribute in method.GetCustomAttributes<ProgramDyingProofAttribute>())
            {
                if (!Enum.IsDefined(attribute.Phase) || attribute.Group <= 0)
                    throw new InvalidOperationException($"Runtime proof '{method.Name}' requires a valid phase and positive group.");
                if (!dying[attribute.Phase].TryGetValue(attribute.Group, out var group))
                    dying[attribute.Phase].Add(attribute.Group, group = new());
                Add(group, attribute.Order, method, typeof(Func<bool>), []);
            }
        }
        return new(observers, nestedDamage, movements, dying);
    }

    private static Dictionary<T, SortedDictionary<int, ProgramRuntimeRegistration>> Empty<T>() where T : struct, Enum =>
        Enum.GetValues<T>().ToDictionary(phase => phase, _ => new SortedDictionary<int, ProgramRuntimeRegistration>());

    private static void Add<T>(Dictionary<T, SortedDictionary<int, ProgramRuntimeRegistration>> groups,
        T phase, int order, MethodInfo method, Type delegateType, Type[] parameters) where T : struct, Enum
    {
        if (!Enum.IsDefined(phase)) throw new InvalidOperationException($"Runtime proof '{method.Name}' requires a valid phase.");
        Add(groups[phase], order, method, delegateType, parameters);
    }

    private static void Add(SortedDictionary<int, ProgramRuntimeRegistration> group, int order,
        MethodInfo method, Type delegateType, Type[] parameters)
    {
        if (order <= 0) throw new InvalidOperationException($"Runtime proof '{method.Name}' requires a positive order.");
        if (method.IsAbstract || method.IsGenericMethod || method.ContainsGenericParameters || method.ReturnType != typeof(bool) ||
            !method.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters) ||
            method.GetParameters().Any(p => p.IsOptional || p.IsOut || p.ParameterType.IsByRef))
            throw new InvalidOperationException($"Runtime proof '{method.Name}' has an invalid handler signature.");
        if (!group.TryAdd(order, new(method, delegateType)))
            throw new InvalidOperationException($"Duplicate runtime proof order '{order}' at '{method.Name}'.");
    }

    private static IReadOnlyDictionary<T, IReadOnlyList<ProgramRuntimeRegistration>> Freeze<T>(
        Dictionary<T, SortedDictionary<int, ProgramRuntimeRegistration>> groups) where T : notnull =>
        new ReadOnlyDictionary<T, IReadOnlyList<ProgramRuntimeRegistration>>(groups.ToDictionary(p => p.Key,
            p => (IReadOnlyList<ProgramRuntimeRegistration>)Array.AsReadOnly(p.Value.Values.ToArray())));
}

// Bind only the proof phases reached by this match. The shared catalog contains
// method metadata; these delegates and their cache belong to one engine only.
internal sealed class ProgramRuntimeProofs(ProgramRuntimeProofCatalog catalog, Func<ProgramRuntimeRegistration, Delegate> bind)
{
    private readonly Dictionary<ProgramObserverProofPhase, Func<long, bool>[]> _observers = new();
    private readonly Dictionary<ProgramNestedDamageProofPhase, ProgramNestedDamageProof[]> _nestedDamage = new();
    private readonly Dictionary<ProgramMovementProofPhase, Func<ProgramSkillFrame, SkillProgramEffect?, ProgramMovementContinuation, bool>[]> _movements = new();
    private readonly Dictionary<ProgramDyingProofPhase, Func<bool>[][]> _dying = new();

    internal bool MatchesObserver(ProgramObserverProofPhase phase, long windowId)
    {
        if (!_observers.TryGetValue(phase, out var handlers))
            _observers.Add(phase, handlers = catalog.Observers[phase].Select(row => (Func<long, bool>)bind(row)).ToArray());
        foreach (var handler in handlers) if (handler(windowId)) return true;
        return false;
    }

    internal bool AllowsNestedDamage(ProgramNestedDamageProofPhase phase, ProgramSkillFrame frame,
        int targetSeat, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (!_nestedDamage.TryGetValue(phase, out var handlers))
            _nestedDamage.Add(phase, handlers = catalog.NestedDamage[phase].Select(row => (ProgramNestedDamageProof)bind(row)).ToArray());
        foreach (var handler in handlers) if (handler(frame, targetSeat, amount, source, nature, sourceLess)) return true;
        return false;
    }

    internal bool MatchesMovement(ProgramMovementProofPhase phase, ProgramSkillFrame frame,
        SkillProgramEffect? effect, ProgramMovementContinuation movement)
    {
        if (!_movements.TryGetValue(phase, out var handlers))
            _movements.Add(phase, handlers = catalog.Movements[phase]
                .Select(row => (Func<ProgramSkillFrame, SkillProgramEffect?, ProgramMovementContinuation, bool>)bind(row)).ToArray());
        foreach (var handler in handlers) if (handler(frame, effect, movement)) return true;
        return false;
    }

    internal bool IsDying(ProgramDyingProofPhase phase)
    {
        if (!_dying.TryGetValue(phase, out var groups))
            _dying.Add(phase, groups = catalog.Dying[phase].Select(group => group.Select(row => (Func<bool>)bind(row)).ToArray()).ToArray());
        var result = false;
        foreach (var group in groups)
        {
            var matched = false;
            foreach (var handler in group) if (handler()) { matched = true; break; }
            // Each old |= statement evaluated its RHS even after an earlier
            // group matched; only the OR inside that RHS short-circuited.
            result |= matched;
        }
        return result;
    }
}

internal sealed partial class ProgramRuntimeHooks
{
    private readonly ProgramRuntimeProofs _proofs;
    internal bool MatchesObserver(ProgramObserverProofPhase phase, long windowId) => _proofs.MatchesObserver(phase, windowId);
    internal bool AllowsNestedDamage(ProgramNestedDamageProofPhase phase, ProgramSkillFrame frame, int targetSeat,
        int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess) =>
        _proofs.AllowsNestedDamage(phase, frame, targetSeat, amount, source, nature, sourceLess);
    internal bool MatchesMovement(ProgramMovementProofPhase phase, ProgramSkillFrame frame,
        SkillProgramEffect? effect, ProgramMovementContinuation movement) => _proofs.MatchesMovement(phase, frame, effect, movement);
    internal bool IsDying(ProgramDyingProofPhase phase) => _proofs.IsDying(phase);
}
