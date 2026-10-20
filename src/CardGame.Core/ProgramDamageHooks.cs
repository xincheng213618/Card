using System.Collections.ObjectModel;
using System.Reflection;

namespace CardGame.Core;

// Each phase denotes a pre-existing point in actual damage resolution. Prediction
// never calls these hooks: some amount providers issue frame-owned receipts.
internal enum ProgramDamagePhase
{
    FactModifiers,
    AdditiveAmount,
    BeforeProgramWindow,
    AfterEquipmentChoices,
    AppliedBeforeDying,
    AttackCompletionPrelude,
    IssuedAttackCompletion,
    BeforeChainAdvance,
    AfterChainAdvance
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
internal sealed class ProgramDamageHookAttribute(ProgramDamagePhase phase, int order) : Attribute
{
    internal ProgramDamagePhase Phase { get; } = phase;
    internal int Order { get; } = order;
}

internal enum ProgramDamageInterception { Continue, Prevented, Paused }
internal delegate bool ProgramDamageCompletion<TAttack>(TAttack attack, out bool paused);
internal sealed record ProgramDamageRegistration(int Order, MethodInfo Method, Type DelegateType);

internal sealed class ProgramDamageHookCatalog
{
    internal IReadOnlyDictionary<ProgramDamagePhase, IReadOnlyList<ProgramDamageRegistration>> Phases { get; }
    private readonly IReadOnlyDictionary<ProgramDamagePhase, Type> _delegateTypes;

    private ProgramDamageHookCatalog(Dictionary<ProgramDamagePhase, SortedDictionary<int, ProgramDamageRegistration>> phases,
        Dictionary<ProgramDamagePhase, Type> delegateTypes)
    {
        Phases = new ReadOnlyDictionary<ProgramDamagePhase, IReadOnlyList<ProgramDamageRegistration>>(
            phases.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<ProgramDamageRegistration>)Array.AsReadOnly(pair.Value.Values.ToArray())));
        _delegateTypes = new ReadOnlyDictionary<ProgramDamagePhase, Type>(delegateTypes);
    }

    internal Type DelegateType(ProgramDamagePhase phase) => _delegateTypes[phase];

    internal static ProgramDamageHookCatalog Discover(IEnumerable<MethodInfo> methods, Func<ProgramDamagePhase, Type> delegateType)
    {
        var delegateTypes = Enum.GetValues<ProgramDamagePhase>().ToDictionary(phase => phase, delegateType);
        var phases = delegateTypes.Keys.ToDictionary(phase => phase, _ => new SortedDictionary<int, ProgramDamageRegistration>());
        foreach (var method in methods)
        foreach (var attribute in method.GetCustomAttributes<ProgramDamageHookAttribute>())
        {
            if (!Enum.IsDefined(attribute.Phase) || attribute.Order <= 0)
                throw new InvalidOperationException($"Damage hook '{method.Name}' requires a valid phase and positive order.");
            var type = delegateTypes[attribute.Phase];
            var signature = type.GetMethod("Invoke")!;
            if (method.IsAbstract || method.IsGenericMethod || method.ContainsGenericParameters ||
                !signature.ReturnType.IsAssignableFrom(method.ReturnType) ||
                !method.GetParameters().Select(parameter => (parameter.ParameterType, parameter.IsOut))
                    .SequenceEqual(signature.GetParameters().Select(parameter => (parameter.ParameterType, parameter.IsOut))) ||
                method.GetParameters().Any(parameter => parameter.IsOptional))
                throw new InvalidOperationException($"Damage hook '{method.Name}' has an invalid handler signature.");
            var phase = phases[attribute.Phase];
            if (phase.Values.Any(row => row.Method == method))
                throw new InvalidOperationException($"Duplicate damage hook '{attribute.Phase}:{method.Name}'.");
            if (!phase.TryAdd(attribute.Order, new(attribute.Order, method, type)))
                throw new InvalidOperationException($"Duplicate damage hook order '{attribute.Phase}:{attribute.Order}'.");
        }
        return new(phases, delegateTypes);
    }
}

// Shared metadata is immutable and contains no engine. Delegates are bound once
// per reached phase per engine; handlers run synchronously, once, in source order.
internal sealed class ProgramDamageHooks(ProgramDamageHookCatalog catalog, Func<ProgramDamageRegistration, Delegate> bind)
{
    private readonly Dictionary<ProgramDamagePhase, Array> _handlers = new();

    private T[] Handlers<T>(ProgramDamagePhase phase) where T : Delegate
    {
        if (catalog.DelegateType(phase) != typeof(T)) throw new ArgumentOutOfRangeException(nameof(phase));
        if (!_handlers.TryGetValue(phase, out var handlers))
            _handlers.Add(phase, handlers = catalog.Phases[phase].Select(row => (T)bind(row)).ToArray());
        return (T[])handlers;
    }

    internal IEnumerable<TModifier> AppendModifiers<TAttack, TModifier>(TAttack attack, IEnumerable<TModifier> initial)
    {
        // Invoke providers now, enumerate their results later, matching the old
        // Concat expression's eager calls and lazy sequence enumeration.
        foreach (var provider in Handlers<Func<TAttack, IEnumerable<TModifier>>>(ProgramDamagePhase.FactModifiers))
            initial = initial.Concat(provider(attack));
        return initial;
    }

    internal int AddAmounts<TAttack>(TAttack attack, int initial)
    {
        // Enumerable.Sum remains at the caller and retains its checked sum.
        // The old subsequent + expression was left-associative and unchecked.
        foreach (var provider in Handlers<Func<TAttack, int>>(ProgramDamagePhase.AdditiveAmount))
            initial = unchecked(initial + provider(attack));
        return initial;
    }

    internal ProgramDamageInterception Intercept<TAttack>(ProgramDamagePhase phase, TAttack attack, int amount, DamageNature nature)
    {
        foreach (var handler in Handlers<Func<TAttack, int, DamageNature, ProgramDamageInterception>>(phase))
        {
            var result = handler(attack, amount, nature);
            if (result != ProgramDamageInterception.Continue) return result;
        }
        return ProgramDamageInterception.Continue;
    }

    internal bool TryBeginApplied<TAttack>(TAttack attack, long damageFrameId)
    {
        foreach (var handler in Handlers<Func<TAttack, long, bool>>(ProgramDamagePhase.AppliedBeforeDying))
            if (handler(attack, damageFrameId)) return true;
        return false;
    }

    internal bool TryComplete<TAttack>(ProgramDamagePhase phase, TAttack attack, out bool paused)
    {
        foreach (var handler in Handlers<ProgramDamageCompletion<TAttack>>(phase))
            if (handler(attack, out paused)) return true;
        paused = false;
        return false;
    }

    internal void AdvanceChain<TAttack>(ProgramDamagePhase phase, TAttack attack)
    {
        foreach (var handler in Handlers<Action<TAttack>>(phase)) handler(attack);
    }
}

public sealed partial class GameEngine
{
    private static Type DamageHookDelegateType(ProgramDamagePhase phase) => phase switch
    {
        ProgramDamagePhase.FactModifiers => typeof(Func<IDamageAttempt, IEnumerable<(CardUseEffectSource Source, int Amount)>>),
        ProgramDamagePhase.AdditiveAmount => typeof(Func<IDamageAttempt, int>),
        ProgramDamagePhase.BeforeProgramWindow or ProgramDamagePhase.AfterEquipmentChoices =>
            typeof(Func<IDamageAttempt, int, DamageNature, ProgramDamageInterception>),
        ProgramDamagePhase.AppliedBeforeDying => typeof(Func<IDamageAttempt, long, bool>),
        ProgramDamagePhase.AttackCompletionPrelude or ProgramDamagePhase.IssuedAttackCompletion => typeof(ProgramDamageCompletion<CardAttackHandle>),
        ProgramDamagePhase.BeforeChainAdvance or ProgramDamagePhase.AfterChainAdvance => typeof(Action<IDamageAttempt>),
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    private static readonly Lazy<ProgramDamageHookCatalog> DamageHookMetadata = new(() =>
        ProgramDamageHookCatalog.Discover(RuntimeModuleMethods, DamageHookDelegateType), true);
    private ProgramDamageHooks? _damageHooks;
    private ProgramDamageHooks DamageHooks => _damageHooks ??= new(DamageHookMetadata.Value,
        row => BindRuntimeHandler(row.Method, row.DelegateType));
}
