using System.Collections.ObjectModel;
using System.Reflection;

namespace CardGame.Core;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class ProgramAnswerAttribute(params string[] actions) : Attribute
{
    internal IReadOnlyList<string> Actions { get; } = Array.AsReadOnly(actions);
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class ProgramAnswerPrefixAttribute(params string[] prefixes) : Attribute
{
    internal IReadOnlyList<string> Prefixes { get; } = Array.AsReadOnly(prefixes);
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class ProgramAnswerBeforeAttribute(int order) : Attribute
{
    internal int Order { get; } = order;
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class ProgramAiChoiceAttribute(params SkillProgramEffectOp[] operations) : Attribute
{
    internal IReadOnlyList<SkillProgramEffectOp> Operations { get; } = Array.AsReadOnly(operations);
}

// Shared metadata contains no game instance. Registration lives beside the actual
// capability implementation, so adding an interaction does not edit a dispatcher.
internal sealed class ProgramInteractionCatalog
{
    internal IReadOnlyDictionary<string, MethodInfo> Answers { get; }
    internal IReadOnlyDictionary<string, MethodInfo> Prefixes { get; }
    internal IReadOnlyDictionary<SkillProgramEffectOp, MethodInfo> AiChoices { get; }
    internal IReadOnlyList<MethodInfo> BeforeAnswers { get; }

    private ProgramInteractionCatalog(Dictionary<string, MethodInfo> answers,
        Dictionary<string, MethodInfo> prefixes, Dictionary<SkillProgramEffectOp, MethodInfo> ai,
        SortedDictionary<int, MethodInfo> before)
    {
        Answers = new ReadOnlyDictionary<string, MethodInfo>(answers);
        Prefixes = new ReadOnlyDictionary<string, MethodInfo>(prefixes);
        AiChoices = new ReadOnlyDictionary<SkillProgramEffectOp, MethodInfo>(ai);
        BeforeAnswers = Array.AsReadOnly(before.Values.ToArray());
    }

    internal static ProgramInteractionCatalog Discover(IEnumerable<MethodInfo> methods)
    {
        var answers = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
        var prefixes = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
        var ai = new Dictionary<SkillProgramEffectOp, MethodInfo>();
        var before = new SortedDictionary<int, MethodInfo>();
        foreach (var method in methods.OrderBy(method => method.DeclaringType?.FullName, StringComparer.Ordinal)
                     .ThenBy(method => method.Name, StringComparer.Ordinal))
        {
            var answer = method.GetCustomAttribute<ProgramAnswerAttribute>();
            var prefix = method.GetCustomAttribute<ProgramAnswerPrefixAttribute>();
            var first = method.GetCustomAttribute<ProgramAnswerBeforeAttribute>();
            var choice = method.GetCustomAttribute<ProgramAiChoiceAttribute>();
            if (answer is null && prefix is null && first is null && choice is null) continue;
            if (method.IsGenericMethod || method.ContainsGenericParameters)
                throw InvalidSignature(method);
            var parameters = method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
            if (answer is not null || prefix is not null)
            {
                if (method.ReturnType != typeof(void) ||
                    !parameters.SequenceEqual(new[] { typeof(PromptChoice) }) &&
                    !parameters.SequenceEqual(new[] { typeof(PromptChoice), typeof(string) }))
                    throw InvalidSignature(method);
                if (answer is not null) AddNames(answers, answer.Actions, method);
                if (prefix is not null) AddNames(prefixes, prefix.Prefixes, method);
            }
            if (first is not null)
            {
                if (first.Order < 0 || method.ReturnType != typeof(bool) ||
                    !parameters.SequenceEqual(new[] { typeof(PromptChoice) })) throw InvalidSignature(method);
                if (!before.TryAdd(first.Order, method))
                    throw new InvalidOperationException($"Duplicate program answer precedence '{first.Order}'.");
            }
            if (choice is not null)
            {
                if (method.ReturnType != typeof(PromptChoice) ||
                    !parameters.SequenceEqual(new[] { typeof(PendingDecision) }) &&
                    !parameters.SequenceEqual(new[] { typeof(ProgramSkillFrame) }) &&
                    !parameters.SequenceEqual(new[] { typeof(PendingDecision), typeof(ProgramSkillFrame) }) &&
                    !parameters.SequenceEqual(new[] { typeof(PendingDecision), typeof(ProgramSkillFrame), typeof(SkillProgramEffect) }))
                    throw InvalidSignature(method);
                if (choice.Operations.Count == 0) throw InvalidSignature(method);
                foreach (var op in choice.Operations)
                    if (!Enum.IsDefined(op) || !ai.TryAdd(op, method))
                        throw new InvalidOperationException($"Invalid or duplicate program AI operation '{op}'.");
            }
        }
        foreach (var prefix in prefixes.Keys)
            if (answers.Keys.Any(action => action.StartsWith(prefix, StringComparison.Ordinal)) ||
                prefixes.Keys.Any(other => other != prefix && other.StartsWith(prefix, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Overlapping program answer prefix '{prefix}'.");
        return new(answers, prefixes, ai, before);
    }

    private static void AddNames(Dictionary<string, MethodInfo> target, IReadOnlyList<string> names, MethodInfo method)
    {
        if (names.Count == 0) throw InvalidSignature(method);
        foreach (var name in names)
            if (string.IsNullOrWhiteSpace(name) || !target.TryAdd(name, method))
                throw new InvalidOperationException($"Invalid or duplicate program answer '{name}'.");
    }

    private static InvalidOperationException InvalidSignature(MethodInfo method) =>
        new($"Program interaction '{method.DeclaringType?.Name}.{method.Name}' has an invalid handler signature.");
}

// A router can invoke registered capabilities, but has no engine, frame store,
// command session or player-state API. Bound delegates belong to this one session
// and are created only when used; the shared catalog never retains a live game.
internal sealed class ProgramInteractionRouter
{
    private readonly ProgramInteractionCatalog _catalog;
    private readonly Func<MethodInfo, Type, Delegate> _bind;
    private readonly Action<PromptChoice> _activation;
    private readonly Dictionary<MethodInfo, Delegate> _answers = new();
    private readonly Dictionary<MethodInfo, Func<PromptChoice, bool>> _before = new();
    private readonly Dictionary<MethodInfo, Func<PendingDecision, ProgramSkillFrame, SkillProgramEffect, PromptChoice>> _ai = new();
    private readonly ProgramAiChoiceSelector _selector;

    internal ProgramInteractionRouter(ProgramInteractionCatalog catalog,
        Func<MethodInfo, Type, Delegate> bind, Action<PromptChoice> activation)
    {
        _catalog = catalog;
        _bind = bind;
        _activation = activation;
        _selector = new(ResolveAi);
    }

    internal void Resolve(PromptChoice selected)
    {
        foreach (var method in _catalog.BeforeAnswers)
        {
            if (!_before.TryGetValue(method, out var handler))
                _before.Add(method, handler = (Func<PromptChoice, bool>)_bind(method, typeof(Func<PromptChoice, bool>)));
            if (handler(selected)) return;
        }
        var action = selected.Parameters.GetValueOrDefault("program-action");
        if (action is not null)
        {
            if (_catalog.Answers.TryGetValue(action, out var method))
            {
                InvokeAnswer(method, selected, action);
                return;
            }
            foreach (var (prefix, handler) in _catalog.Prefixes)
                if (action.StartsWith(prefix, StringComparison.Ordinal))
                {
                    InvokeAnswer(handler, selected, action);
                    return;
                }
        }
        // This is the existing trigger activation protocol, not an old dispatcher.
        // It validates candidate/group identity and rejects unknown actions.
        _activation(selected);
    }

    internal PromptChoice SelectAi(PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        _selector.Select(decision, frame, effect);

    private void InvokeAnswer(MethodInfo method, PromptChoice selected, string action)
    {
        if (!_answers.TryGetValue(method, out var handler))
        {
            var type = method.GetParameters().Length == 1 ? typeof(Action<PromptChoice>) : typeof(Action<PromptChoice, string>);
            _answers.Add(method, handler = _bind(method, type));
        }
        if (handler is Action<PromptChoice> simple) simple(selected);
        else ((Action<PromptChoice, string>)handler)(selected, action);
    }

    private Func<PendingDecision, ProgramSkillFrame, SkillProgramEffect, PromptChoice>? ResolveAi(SkillProgramEffectOp op)
    {
        if (!_catalog.AiChoices.TryGetValue(op, out var method)) return null;
        if (_ai.TryGetValue(method, out var existing)) return existing;
        var parameters = method.GetParameters();
        Func<PendingDecision, ProgramSkillFrame, SkillProgramEffect, PromptChoice> handler;
        if (parameters.Length == 3)
            handler = (Func<PendingDecision, ProgramSkillFrame, SkillProgramEffect, PromptChoice>)_bind(method,
                typeof(Func<PendingDecision, ProgramSkillFrame, SkillProgramEffect, PromptChoice>));
        else if (parameters.Length == 2)
        {
            var invoke = (Func<PendingDecision, ProgramSkillFrame, PromptChoice>)_bind(method,
                typeof(Func<PendingDecision, ProgramSkillFrame, PromptChoice>));
            handler = (decision, frame, _) => invoke(decision, frame);
        }
        else if (parameters[0].ParameterType == typeof(PendingDecision))
        {
            var invoke = (Func<PendingDecision, PromptChoice>)_bind(method, typeof(Func<PendingDecision, PromptChoice>));
            handler = (decision, _, _) => invoke(decision);
        }
        else
        {
            var invoke = (Func<ProgramSkillFrame, PromptChoice>)_bind(method, typeof(Func<ProgramSkillFrame, PromptChoice>));
            handler = (_, frame, _) => invoke(frame);
        }
        _ai.Add(method, handler);
        return handler;
    }
}
