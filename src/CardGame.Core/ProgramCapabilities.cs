using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace CardGame.Core;

/// <summary>Registers one private, independently constructed program capability.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class ProgramCapabilityAttribute : Attribute { }

/// <summary>
/// Immutable capability metadata. Factories accept a game when invoked and never
/// capture a game; only the match-local registry retains capability instances.
/// </summary>
internal sealed class ProgramCapabilityCatalog
{
    internal sealed record Registration(
        Type Implementation,
        IReadOnlyList<Type> Contracts,
        Func<GameEngine, object> Create);

    private readonly IReadOnlyDictionary<Type, Registration> _byContract;
    internal IReadOnlyList<Registration> Registrations { get; }

    internal ProgramCapabilityCatalog(IEnumerable<Type> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var declaredTypes = types.ToArray();
        if (declaredTypes.Any(type => type is null))
            throw new InvalidOperationException("A program capability type cannot be null.");
        var registrations = new List<Registration>();
        var byContract = new Dictionary<Type, Registration>();
        foreach (var type in declaredTypes.Where(type => type.IsDefined(typeof(ProgramCapabilityAttribute), false))
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            if (!type.IsClass || !type.IsSealed || type.IsAbstract || type.IsGenericType || type.ContainsGenericParameters)
                throw Invalid(type, "must be a sealed, non-generic class");
            var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (constructors is not [var constructor] ||
                !constructor.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([typeof(GameEngine)]))
                throw Invalid(type, "requires exactly one constructor accepting only GameEngine");
            var contracts = type.GetInterfaces().Where(contract => contract != typeof(IProgramSelectionQuery))
                .OrderBy(contract => contract.FullName, StringComparer.Ordinal).ToArray();
            if (contracts.Length == 0 || contracts.Any(contract => contract.ContainsGenericParameters ||
                    contract == typeof(ISkillProgramExecutionHost) || contract == typeof(ISkillProgramEffectHost)))
                throw Invalid(type, "requires dedicated capability interfaces, separate from the shared program host");
            var registration = new Registration(type, Array.AsReadOnly(contracts), engine => Construct(constructor, engine));
            foreach (var contract in contracts)
                if (!byContract.TryAdd(contract, registration))
                    throw new InvalidOperationException($"Program capability '{contract.FullName}' is registered by both " +
                        $"'{byContract[contract].Implementation.FullName}' and '{type.FullName}'.");
            registrations.Add(registration);
        }
        if (registrations.Count == 0)
            throw new InvalidOperationException("No program capabilities were registered.");
        Registrations = Array.AsReadOnly(registrations.ToArray());
        _byContract = new ReadOnlyDictionary<Type, Registration>(byContract);
    }

    internal bool TryResolve(Type contract, [NotNullWhen(true)] out Registration? registration)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (!contract.IsInterface)
            throw new ArgumentException("A program capability contract must be an interface.", nameof(contract));
        return _byContract.TryGetValue(contract, out registration);
    }

    private static object Construct(ConstructorInfo constructor, GameEngine engine)
    {
        try { return constructor.Invoke([engine]); }
        catch (TargetInvocationException failure) when (failure.InnerException is { } cause)
        {
            ExceptionDispatchInfo.Capture(cause).Throw();
            throw;
        }
    }

    private static InvalidOperationException Invalid(Type type, string reason) =>
        new($"Program capability '{type.FullName}' {reason}.");
}

/// <summary>Resolves and caches dedicated adapters for exactly one game.</summary>
internal sealed class ProgramCapabilityRegistry(GameEngine engine, ProgramCapabilityCatalog catalog)
{
    private readonly Dictionary<Type, object> _instances = new();

    internal T Get<T>() where T : class => TryGet<T>(out var capability)
        ? capability
        : throw new NotSupportedException($"No program capability is registered for '{typeof(T).FullName}'.");

    internal bool TryGet<T>([NotNullWhen(true)] out T? capability) where T : class
    {
        if (!catalog.TryResolve(typeof(T), out var registration))
        {
            capability = null;
            return false;
        }
        if (!_instances.TryGetValue(registration.Implementation, out var instance))
            _instances.Add(registration.Implementation, instance = registration.Create(engine));
        capability = (T)instance;
        return true;
    }
}

public sealed partial class GameEngine
{
    // Discovery is limited to explicit registrations in this engine's own type.
    // Nested adapters retain the private rules boundary without exposing state.
    private static readonly Lazy<ProgramCapabilityCatalog> ProgramCapabilityMetadata = new(() =>
        new(typeof(GameEngine).GetNestedTypes(BindingFlags.NonPublic)), true);
    private ProgramCapabilityRegistry? _programCapabilities;
    private ProgramCapabilityRegistry ProgramCapabilities =>
        _programCapabilities ??= new(this, ProgramCapabilityMetadata.Value);

    [ProgramCapability]
    private sealed class ConfiguredConversionProgramCapability(GameEngine engine) : IConfiguredConversionProgramHost
    {
        public SkillProgramStepOutcome DeclareBoundCardName(ProgramSkillFrame frame, string sourceBind, string stateId) =>
            engine.BeginConfiguredCardDeclaration(frame, sourceBind, stateId);
        public void UpgradeConversionTier(ProgramSkillFrame frame, string stateId) =>
            engine.UpgradeConfiguredConversionTier(frame, stateId);
    }

    [ProgramCapability]
    private sealed class ActionCategoryGiftProgramCapability(GameEngine engine) : IActionCategoryGiftProgramHost
    {
        public SkillProgramStepOutcome ChooseDifferentActionCategoryGift(ProgramSkillFrame frame, int targetSeat,
            string resultBind, IReadOnlyList<CardZoneKind> zones) =>
            engine.BeginDifferentActionCategoryGift(frame, targetSeat, resultBind, zones);
    }
}
