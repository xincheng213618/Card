using System.Collections.ObjectModel;
using System.Reflection;

namespace CardGame.Core;

internal enum ProgramRulePhase
{
    BeforeEvent,
    AfterEvent,
    BeforeStatePublish
}

// Register next to the rule implementation. Orders are explicit because a rule
// can recursively raise another fact before the remaining observers run.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ProgramRuleObserverAttribute(ProgramRulePhase phase, int order) : Attribute
{
    internal ProgramRulePhase Phase { get; } = phase;
    internal int Order { get; } = order;
}

internal sealed record ProgramRuleObserverRegistration(
    MethodInfo Method, Type DelegateType, Func<Delegate, Action<IGameEvent?>> Adapt);

// Static metadata never retains a match or a callback bound to a match.
internal sealed class ProgramRuleObserverCatalog
{
    internal IReadOnlyDictionary<ProgramRulePhase, IReadOnlyList<ProgramRuleObserverRegistration>> Phases { get; }

    private ProgramRuleObserverCatalog(
        Dictionary<ProgramRulePhase, SortedDictionary<int, ProgramRuleObserverRegistration>> phases)
    {
        Phases = new ReadOnlyDictionary<ProgramRulePhase, IReadOnlyList<ProgramRuleObserverRegistration>>(
            phases.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<ProgramRuleObserverRegistration>)Array.AsReadOnly(pair.Value.Values.ToArray())));
    }

    internal static ProgramRuleObserverCatalog Discover(IEnumerable<MethodInfo> methods)
    {
        var phases = Enum.GetValues<ProgramRulePhase>().ToDictionary(phase => phase,
            _ => new SortedDictionary<int, ProgramRuleObserverRegistration>());
        foreach (var method in methods.OrderBy(method => method.DeclaringType?.FullName, StringComparer.Ordinal)
                     .ThenBy(method => method.Name, StringComparer.Ordinal))
        {
            var attributes = method.GetCustomAttributes<ProgramRuleObserverAttribute>().ToArray();
            if (attributes.Length == 0) continue;
            var parameters = method.GetParameters();
            if (method.IsStatic || method.IsGenericMethod || method.ContainsGenericParameters ||
                method.ReturnType != typeof(void) || parameters.Length > 1 ||
                parameters.Length == 1 && (!typeof(IGameEvent).IsAssignableFrom(parameters[0].ParameterType) ||
                    parameters[0].IsOptional || parameters[0].ParameterType.IsByRef))
                throw InvalidSignature(method);

            var registration = parameters.Length == 0
                ? new ProgramRuleObserverRegistration(method, typeof(Action), AdaptStateObserver)
                : new ProgramRuleObserverRegistration(method, typeof(Action<>).MakeGenericType(parameters[0].ParameterType),
                    typeof(ProgramRuleObserverCatalog).GetMethod(nameof(AdaptEventObserver), BindingFlags.Static | BindingFlags.NonPublic)!
                        .MakeGenericMethod(parameters[0].ParameterType)
                        .CreateDelegate<Func<Delegate, Action<IGameEvent?>>>());
            foreach (var attribute in attributes)
            {
                if (!Enum.IsDefined(attribute.Phase) || attribute.Order <= 0)
                    throw new InvalidOperationException($"Rule observer '{method.Name}' requires a valid phase and positive explicit order.");
                if (attribute.Phase == ProgramRulePhase.BeforeStatePublish && parameters.Length != 0)
                    throw InvalidSignature(method);
                if (!phases[attribute.Phase].TryAdd(attribute.Order, registration))
                    throw new InvalidOperationException($"Duplicate rule observer order '{attribute.Phase}:{attribute.Order}'.");
            }
        }
        foreach (var (phase, registrations) in phases)
            if (registrations.Count == 0)
                throw new InvalidOperationException($"Rule observer phase '{phase}' has no registrations.");
        return new(phases);
    }

    private static Action<IGameEvent?> AdaptStateObserver(Delegate handler)
    {
        var invoke = (Action)handler;
        return _ => invoke();
    }

    private static Action<IGameEvent?> AdaptEventObserver<TEvent>(Delegate handler) where TEvent : IGameEvent
    {
        var invoke = (Action<TEvent>)handler;
        return payload => { if (payload is TEvent typed) invoke(typed); };
    }

    private static InvalidOperationException InvalidSignature(MethodInfo method) =>
        new($"Rule observer '{method.DeclaringType?.Name}.{method.Name}' has an invalid handler signature; expected an instance void method with no parameters or one required game-event parameter.");
}

// Bind every callback once per engine. Invocation stays synchronous and depth
// first: nested rule facts fully run through this same pipeline before return.
internal sealed class ProgramRuleObservers
{
    private readonly Action<IGameEvent?>[] _beforeEvent;
    private readonly Action<IGameEvent?>[] _afterEvent;
    private readonly Action<IGameEvent?>[] _beforeStatePublish;

    internal ProgramRuleObservers(ProgramRuleObserverCatalog catalog, Func<MethodInfo, Type, Delegate> bind)
    {
        Action<IGameEvent?>[] BindPhase(ProgramRulePhase phase) => catalog.Phases[phase]
            .Select(registration => registration.Adapt(bind(registration.Method, registration.DelegateType))).ToArray();
        _beforeEvent = BindPhase(ProgramRulePhase.BeforeEvent);
        _afterEvent = BindPhase(ProgramRulePhase.AfterEvent);
        _beforeStatePublish = BindPhase(ProgramRulePhase.BeforeStatePublish);
    }

    internal void BeforeEvent(IGameEvent payload)
    {
        foreach (var observer in _beforeEvent) observer(payload);
    }

    internal void AfterEvent(IGameEvent payload)
    {
        foreach (var observer in _afterEvent) observer(payload);
    }

    internal void BeforeStatePublish()
    {
        foreach (var observer in _beforeStatePublish) observer(null);
    }
}
