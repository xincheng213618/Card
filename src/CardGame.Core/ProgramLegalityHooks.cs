using System.Linq.Expressions;
using System.Reflection;

namespace CardGame.Core;

internal enum ProgramActivationLegalityPhase
{
    BeforeOwnerFacts,
    BeforeCardCount,
    BeforeCondition,
    AfterCommonStart
}

internal enum ProgramTriggerLegalityPhase
{
    Collection,
    BeforeGainQualification,
    BeforeAdvancedPrerequisites,
    BeforeCondition,
    AfterDyingCheck,
    AfterCondition,
    AfterInitialPayments
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ProgramActivationLegalityAttribute(
    ProgramActivationLegalityPhase phase, int order, params SkillProgramEffectOp[] operations) : Attribute
{
    internal ProgramActivationLegalityPhase Phase { get; } = phase;
    internal int Order { get; } = order;
    internal IReadOnlyList<SkillProgramEffectOp> Operations { get; } = Array.AsReadOnly(operations);
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ProgramTriggerLegalityAttribute(
    ProgramTriggerLegalityPhase phase, int order, params SkillProgramEffectOp[] operations) : Attribute
{
    internal ProgramTriggerLegalityPhase Phase { get; } = phase;
    internal int Order { get; } = order;
    internal IReadOnlyList<SkillProgramEffectOp> Operations { get; } = Array.AsReadOnly(operations);
}

internal readonly record struct ProgramActivationLegalityContext(
    CharacterState Owner, SkillProgram Program, ProgramExecutionPlan Plan, PlayerSkillContext Context)
{
    internal SkillProgramActivation Activation => Plan.Activation!;
    internal ProgramInstructionFeatures Features => Plan.Features;
    internal int OwnerSeat => Owner.Seat;
    internal string SkillId => Program.Id;
}

internal readonly record struct ProgramTriggerLegalityContext(
    CharacterState Owner, SkillProgram Program, ProgramExecutionPlan Plan,
    ProgramTriggerCandidate Candidate, ProgramSkillWindowContext? WindowContext)
{
    internal SkillProgramTrigger Trigger => Plan.Trigger!;
    internal ProgramInstructionFeatures Features => Plan.Features;
    internal ProgramSkillWindowContext Context => WindowContext ??
        throw new InvalidOperationException("A collection gate cannot access a resolution window.");
    internal int OwnerSeat => Candidate.OwnerSeat;
    internal string SkillId => Candidate.SkillId;
}

// A capability declares its eligibility beside its implementation. Explicit
// phases preserve the shared condition/payment boundaries; order preserves
// short-circuit semantics independently of reflection enumeration order.
internal sealed class ProgramLegalityCatalog
{
    internal sealed record Gate<TContext>(
        MethodInfo Method, int Order, IReadOnlyList<SkillProgramEffectOp> Operations,
        Func<object?, TContext, bool> Invoke);

    private readonly IReadOnlyDictionary<ProgramActivationLegalityPhase,
        IReadOnlyList<Gate<ProgramActivationLegalityContext>>> _activations;
    private readonly IReadOnlyDictionary<ProgramTriggerLegalityPhase,
        IReadOnlyList<Gate<ProgramTriggerLegalityContext>>> _triggers;

    private ProgramLegalityCatalog(
        Dictionary<ProgramActivationLegalityPhase, SortedDictionary<int, Gate<ProgramActivationLegalityContext>>> activations,
        Dictionary<ProgramTriggerLegalityPhase, SortedDictionary<int, Gate<ProgramTriggerLegalityContext>>> triggers)
    {
        _activations = activations.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<Gate<ProgramActivationLegalityContext>>)Array.AsReadOnly(pair.Value.Values.ToArray()));
        _triggers = triggers.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<Gate<ProgramTriggerLegalityContext>>)Array.AsReadOnly(pair.Value.Values.ToArray()));
    }

    internal static ProgramLegalityCatalog Discover(IEnumerable<MethodInfo> methods)
    {
        var activations = new Dictionary<ProgramActivationLegalityPhase,
            SortedDictionary<int, Gate<ProgramActivationLegalityContext>>>();
        var triggers = new Dictionary<ProgramTriggerLegalityPhase,
            SortedDictionary<int, Gate<ProgramTriggerLegalityContext>>>();
        foreach (var method in methods)
        {
            foreach (var registration in method.GetCustomAttributes<ProgramActivationLegalityAttribute>())
            {
                ValidateRegistration(registration.Phase, registration.Order, registration.Operations, method);
                Add(activations, registration.Phase, registration.Order, registration.Operations, method);
            }
            foreach (var registration in method.GetCustomAttributes<ProgramTriggerLegalityAttribute>())
            {
                ValidateRegistration(registration.Phase, registration.Order, registration.Operations, method);
                if (registration.Phase == ProgramTriggerLegalityPhase.Collection &&
                    method.GetParameters().Any(parameter => parameter.ParameterType == typeof(ProgramSkillWindowContext)))
                    throw InvalidSignature(method);
                Add(triggers, registration.Phase, registration.Order, registration.Operations, method);
            }
        }
        return new(activations, triggers);
    }

    internal IReadOnlyList<Gate<ProgramActivationLegalityContext>> Activations(ProgramActivationLegalityPhase phase) =>
        _activations.GetValueOrDefault(phase) ?? [];

    internal IReadOnlyList<Gate<ProgramTriggerLegalityContext>> Triggers(ProgramTriggerLegalityPhase phase) =>
        _triggers.GetValueOrDefault(phase) ?? [];

    private static void ValidateRegistration<TPhase>(TPhase phase, int order,
        IReadOnlyList<SkillProgramEffectOp> operations, MethodInfo method) where TPhase : struct, Enum
    {
        if (!Enum.IsDefined(phase) || order < 0 || operations.Distinct().Count() != operations.Count ||
            operations.Any(operation => !Enum.IsDefined(operation))) throw InvalidSignature(method);
    }

    private static void Add<TPhase, TContext>(
        Dictionary<TPhase, SortedDictionary<int, Gate<TContext>>> phases,
        TPhase phase, int order, IReadOnlyList<SkillProgramEffectOp> operations, MethodInfo method)
        where TPhase : struct, Enum
    {
        if (!phases.TryGetValue(phase, out var gates)) phases.Add(phase, gates = new());
        if (!gates.TryAdd(order, new(method, order, operations, Compile<TContext>(method))))
            throw new InvalidOperationException($"Duplicate program legality precedence '{phase}:{order}'.");
    }

    // Supported legacy signatures use only unambiguous capability inputs. A
    // special signature uses a small context adapter beside that capability.
    private static Func<object?, TContext, bool> Compile<TContext>(MethodInfo method)
    {
        if (method.IsGenericMethod || method.ContainsGenericParameters || method.ReturnType != typeof(bool))
            throw InvalidSignature(method);
        var instance = Expression.Parameter(typeof(object), "session");
        var context = Expression.Parameter(typeof(TContext), "gate");
        var properties = typeof(TContext).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(property => property.Name != nameof(ProgramTriggerLegalityContext.WindowContext)).ToArray();
        var parameters = method.GetParameters();
        if (parameters.Length == 0 || parameters.Select(parameter => parameter.ParameterType).Distinct().Count() != parameters.Length)
            throw InvalidSignature(method);
        var arguments = parameters.Select(parameter =>
        {
            if (parameter.ParameterType == typeof(TContext)) return (Expression)context;
            var property = properties.SingleOrDefault(property => property.PropertyType == parameter.ParameterType);
            return property is not null ? Expression.Property(context, property) : throw InvalidSignature(method);
        });
        var call = Expression.Call(method.IsStatic ? null : Expression.Convert(instance, method.DeclaringType!), method, arguments);
        return Expression.Lambda<Func<object?, TContext, bool>>(call, instance, context).Compile();
    }

    private static InvalidOperationException InvalidSignature(MethodInfo method) =>
        new($"Program legality '{method.DeclaringType?.Name}.{method.Name}' has an invalid registration or signature.");
}

internal sealed class ProgramLegalityRouter(ProgramLegalityCatalog catalog, object session)
{
    internal bool CanActivate(ProgramActivationLegalityPhase phase, ProgramActivationLegalityContext context) =>
        Evaluate(catalog.Activations(phase), context, context.Features);

    internal bool CanTrigger(ProgramTriggerLegalityPhase phase, ProgramTriggerLegalityContext context) =>
        Evaluate(catalog.Triggers(phase), context, context.Features);

    private bool Evaluate<TContext>(IReadOnlyList<ProgramLegalityCatalog.Gate<TContext>> gates,
        TContext context, ProgramInstructionFeatures features)
    {
        for (var index = 0; index < gates.Count; index++)
        {
            var gate = gates[index];
            var applies = gate.Operations.Count == 0;
            for (var operation = 0; !applies && operation < gate.Operations.Count; operation++)
                applies = features.HasOperation(gate.Operations[operation]);
            if (applies && !gate.Invoke(session, context)) return false;
        }
        return true;
    }
}
