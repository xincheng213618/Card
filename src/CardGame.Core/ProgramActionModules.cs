using System.Reflection;

namespace CardGame.Core;

internal enum ProgramActionSourcePhase { ProgramTail, Cardless }
internal enum ProgramActionHandlerKind { Submit, Execute, Contribution }
internal enum ProgramCardActionPhase { BeforeProgram, AfterProgram }
[AttributeUsage(AttributeTargets.Method)]
internal sealed class ProgramCardActionAttribute(ProgramCardActionPhase phase, int order) : Attribute
{
    internal ProgramCardActionPhase Phase { get; } = phase;
    internal int Order { get; } = order;
}
[AttributeUsage(AttributeTargets.Method)]
internal sealed class ProgramActionSourceAttribute(ProgramActionSourcePhase phase, int order) : Attribute
{
    internal ProgramActionSourcePhase Phase { get; } = phase;
    internal int Order { get; } = order;
}
[AttributeUsage(AttributeTargets.Method)]
internal sealed class ProgramActionHandlerAttribute(ProgramActionHandlerKind kind, int order) : Attribute
{
    internal ProgramActionHandlerKind Kind { get; } = kind;
    internal int Order { get; } = order;
}

internal delegate bool ProgramActionSubmission(UseProgramSkillCommand command, out CommandResult result);
internal delegate bool ProgramActionExecution(CharacterState owner, LegalAction action, IReadOnlyList<int> cards, IReadOnlyList<int> targets);
internal delegate bool ProgramActionContribution(CharacterState provider, CharacterState owner, LegalAction action, int cardId);

// Sources and command execution have independent precedence: their historical
// order differs. Only trusted command-session code can invoke execution hooks.
internal sealed class ProgramActionCatalog
{
    internal IReadOnlyDictionary<ProgramActionSourcePhase, IReadOnlyList<MethodInfo>> Sources { get; }
    internal IReadOnlyDictionary<ProgramActionHandlerKind, IReadOnlyList<MethodInfo>> Handlers { get; }
    internal IReadOnlyDictionary<ProgramCardActionPhase, IReadOnlyList<MethodInfo>> Augmentations { get; }
    internal ProgramActionCatalog(IEnumerable<MethodInfo> methods)
    {
        var sources = Enum.GetValues<ProgramActionSourcePhase>().ToDictionary(phase => phase, _ => new SortedDictionary<int, MethodInfo>());
        var handlers = Enum.GetValues<ProgramActionHandlerKind>().ToDictionary(kind => kind, _ => new SortedDictionary<int, MethodInfo>());
        var augmentations = Enum.GetValues<ProgramCardActionPhase>().ToDictionary(phase => phase, _ => new SortedDictionary<int, MethodInfo>());
        foreach (var method in methods.OrderBy(method => method.Name, StringComparer.Ordinal))
        {
            var source = method.GetCustomAttribute<ProgramActionSourceAttribute>();
            var handler = method.GetCustomAttribute<ProgramActionHandlerAttribute>();
            var augmentation = method.GetCustomAttribute<ProgramCardActionAttribute>();
            if (source is null && handler is null && augmentation is null) continue;
            if (method.IsStatic || method.IsGenericMethod || method.ContainsGenericParameters) throw Invalid(method);
            if (source is not null)
            {
                if (!Enum.IsDefined(source.Phase) || source.Order < 0 || method.ReturnType != typeof(IEnumerable<LegalAction>) ||
                    !method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([typeof(CharacterState)]) ||
                    !sources[source.Phase].TryAdd(source.Order, method)) throw Invalid(method);
            }
            if (handler is not null)
            {
                if (!Enum.IsDefined(handler.Kind) || handler.Order < 0) throw Invalid(method);
                var signature = DelegateType(handler.Kind).GetMethod("Invoke")!;
                if (method.ReturnType != signature.ReturnType ||
                    !method.GetParameters().Select(parameter => (parameter.ParameterType, parameter.IsOut))
                        .SequenceEqual(signature.GetParameters().Select(parameter => (parameter.ParameterType, parameter.IsOut))) ||
                    !handlers[handler.Kind].TryAdd(handler.Order, method)) throw Invalid(method);
            }
            if (augmentation is not null)
            {
                var parameters = method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
                var supported = new[] { typeof(List<LegalAction>), typeof(CharacterState), typeof(IReadOnlyList<Card>), typeof(int?) };
                if (!Enum.IsDefined(augmentation.Phase) || augmentation.Order < 0 || method.ReturnType != typeof(void) ||
                    parameters.Length < 2 || !parameters.Take(2).SequenceEqual(supported.Take(2)) ||
                    parameters.Length == 4 && !parameters.SequenceEqual(supported) ||
                    parameters.Distinct().Count() != parameters.Length || parameters.Any(type => !supported.Contains(type)) ||
                    !augmentations[augmentation.Phase].TryAdd(augmentation.Order, method)) throw Invalid(method);
            }
        }
        if (sources.Values.Any(items => items.Count == 0) || handlers.Values.Any(items => items.Count == 0))
            throw new InvalidOperationException("Program action registration requires every source and command phase.");
        Sources = new System.Collections.ObjectModel.ReadOnlyDictionary<ProgramActionSourcePhase, IReadOnlyList<MethodInfo>>(
            sources.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<MethodInfo>)Array.AsReadOnly(pair.Value.Values.ToArray())));
        Handlers = new System.Collections.ObjectModel.ReadOnlyDictionary<ProgramActionHandlerKind, IReadOnlyList<MethodInfo>>(
            handlers.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<MethodInfo>)Array.AsReadOnly(pair.Value.Values.ToArray())));
        Augmentations = new System.Collections.ObjectModel.ReadOnlyDictionary<ProgramCardActionPhase, IReadOnlyList<MethodInfo>>(
            augmentations.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<MethodInfo>)Array.AsReadOnly(pair.Value.Values.ToArray())));
    }
    internal static Type DelegateType(ProgramActionHandlerKind kind) => kind switch
    {
        ProgramActionHandlerKind.Submit => typeof(ProgramActionSubmission),
        ProgramActionHandlerKind.Execute => typeof(ProgramActionExecution),
        ProgramActionHandlerKind.Contribution => typeof(ProgramActionContribution),
        _ => throw new InvalidOperationException("Unknown program command registration phase.")
    };
    private static InvalidOperationException Invalid(MethodInfo method) => new($"Invalid or duplicate program action registration '{method.Name}'.");
}

internal sealed class ProgramActionRouter
{
    private readonly IReadOnlyDictionary<ProgramActionSourcePhase, Func<CharacterState, IEnumerable<LegalAction>>[]> _sources;
    private readonly ProgramActionSubmission[] _submit;
    private readonly ProgramActionExecution[] _execute;
    private readonly ProgramActionContribution[] _contributions;
    private readonly IReadOnlyDictionary<ProgramCardActionPhase, Action<List<LegalAction>, CharacterState, IReadOnlyList<Card>, int?>[]> _augmentations;
    internal ProgramActionRouter(ProgramActionCatalog catalog, Func<MethodInfo, Type, Delegate> bind)
    {
        _sources = catalog.Sources.ToDictionary(pair => pair.Key, pair => pair.Value
            .Select(method => (Func<CharacterState, IEnumerable<LegalAction>>)bind(method, typeof(Func<CharacterState, IEnumerable<LegalAction>>))).ToArray());
        T[] Bind<T>(ProgramActionHandlerKind kind) where T : Delegate => catalog.Handlers[kind].Select(method => (T)bind(method, typeof(T))).ToArray();
        _submit = Bind<ProgramActionSubmission>(ProgramActionHandlerKind.Submit);
        _execute = Bind<ProgramActionExecution>(ProgramActionHandlerKind.Execute);
        _contributions = Bind<ProgramActionContribution>(ProgramActionHandlerKind.Contribution);
        _augmentations = catalog.Augmentations.ToDictionary(pair => pair.Key,
            pair => pair.Value.Select(method => BindAugmentation(method, bind)).ToArray());
    }
    private static Action<List<LegalAction>, CharacterState, IReadOnlyList<Card>, int?> BindAugmentation(
        MethodInfo method, Func<MethodInfo, Type, Delegate> bind)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 4)
            return (Action<List<LegalAction>, CharacterState, IReadOnlyList<Card>, int?>)bind(method,
                typeof(Action<List<LegalAction>, CharacterState, IReadOnlyList<Card>, int?>));
        if (parameters.Length == 2)
        {
            var handler = (Action<List<LegalAction>, CharacterState>)bind(method, typeof(Action<List<LegalAction>, CharacterState>));
            return (actions, actor, _, _) => handler(actions, actor);
        }
        if (parameters[2].ParameterType == typeof(IReadOnlyList<Card>))
        {
            var handler = (Action<List<LegalAction>, CharacterState, IReadOnlyList<Card>>)bind(method,
                typeof(Action<List<LegalAction>, CharacterState, IReadOnlyList<Card>>));
            return (actions, actor, cards, _) => handler(actions, actor, cards);
        }
        var selected = (Action<List<LegalAction>, CharacterState, int?>)bind(method, typeof(Action<List<LegalAction>, CharacterState, int?>));
        return (actions, actor, _, cardId) => selected(actions, actor, cardId);
    }
    internal void Augment(ProgramCardActionPhase phase, List<LegalAction> actions, CharacterState actor,
        IReadOnlyList<Card> cards, int? selectedCardId)
    {
        foreach (var handler in _augmentations[phase]) handler(actions, actor, cards, selectedCardId);
    }
    internal IEnumerable<LegalAction> Build(ProgramActionSourcePhase phase, CharacterState owner)
    {
        foreach (var source in _sources[phase])
            foreach (var action in source(owner)) yield return action;
    }
    internal bool TrySubmit(UseProgramSkillCommand command, out CommandResult result)
    {
        foreach (var handler in _submit) if (handler(command, out result)) return true;
        result = null!; return false;
    }
    internal bool TryExecute(CharacterState owner, LegalAction action, IReadOnlyList<int> cards, IReadOnlyList<int> targets)
    {
        foreach (var handler in _execute) if (handler(owner, action, cards, targets)) return true;
        return false;
    }
    internal bool TryContribute(CharacterState provider, CharacterState owner, LegalAction action, int cardId)
    {
        foreach (var handler in _contributions) if (handler(provider, owner, action, cardId)) return true;
        return false;
    }
}

public sealed partial class GameEngine
{
    private static readonly Lazy<ProgramActionCatalog> ProgramActionMetadata = new(() => new(typeof(GameEngine).GetMethods(
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)), true);
    private ProgramActionRouter? _programActions;
    private ProgramActionRouter ProgramActions => _programActions ??= new(ProgramActionMetadata.Value,
        (method, type) => method.CreateDelegate(type, this));
}
