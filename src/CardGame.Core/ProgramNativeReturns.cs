using System.Reflection;
using AttackReceipt = CardGame.Core.GameEngine.AttackCompletionReceipt;

namespace CardGame.Core;

internal enum NativeAttackReturnPhase { BeforeCleanup, AfterCleanup, AfterBorrowedSword, BeforeParentResume }

[AttributeUsage(AttributeTargets.Method)]
internal sealed class NativeUseValidationAttribute(int order) : Attribute
{ internal int Order { get; } = order; }

// A preparation always runs before pop. A paired continuation participates in
// the ordered, exclusive after-pop routing only when that preparation succeeded.
[AttributeUsage(AttributeTargets.Method)]
internal sealed class NativeUsePreparationAttribute(int order, int returnOrder = 0, string? continuation = null) : Attribute
{
    internal int Order { get; } = order;
    internal int ReturnOrder { get; } = returnOrder;
    internal string? Continuation { get; } = continuation;
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class NativeUseReturnAttribute(int order) : Attribute
{ internal int Order { get; } = order; }

[AttributeUsage(AttributeTargets.Method)]
internal sealed class NativeAttackReturnAttribute(NativeAttackReturnPhase phase, int order) : Attribute
{
    internal NativeAttackReturnPhase Phase { get; } = phase;
    internal int Order { get; } = order;
}

internal delegate void NativeUseValidation(ref CardUseFrame use);

// This scope exists only on the PopFinishedCardUse call stack. It is neither
// pending engine state nor a second owner of a native use/attack receipt.
internal sealed class NativeUseReturnContext(CardUseFrame use, bool hpLossMaterialReturn, int preparationCount)
{
    private bool _returned;
    internal CardUseFrame Use { get; } = use;
    internal bool HpLossMaterialReturn { get; } = hpLossMaterialReturn;
    internal ProgramVirtualBasicReturn? BasicReturn { get; } = use.VirtualBasicReturn;
    internal DyingResponseEvent? DyingResponse { get; } = use.DyingResponse;
    internal bool[] Prepared { get; } = new bool[preparationCount];
    internal void BeginReturn()
    {
        if (_returned) throw new InvalidOperationException("A native use completion cannot return twice.");
        _returned = true;
    }
}

internal sealed class ProgramNativeReturnCatalog
{
    internal sealed record Validation(int Order, MethodInfo Method);
    internal sealed record Preparation(int Order, MethodInfo Method);
    internal sealed record UseReturn(int Order, MethodInfo Method, int PreparationIndex = -1);
    internal sealed record AttackReturn(NativeAttackReturnPhase Phase, int Order, MethodInfo Method);

    internal IReadOnlyList<Validation> Validations { get; }
    internal IReadOnlyList<Preparation> Preparations { get; }
    internal IReadOnlyList<UseReturn> UseReturns { get; }
    internal IReadOnlyList<AttackReturn> AttackReturns { get; }

    internal ProgramNativeReturnCatalog(IEnumerable<MethodInfo> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        var validations = new SortedDictionary<int, Validation>();
        var preparations = new SortedDictionary<int, (Preparation Entry, NativeUsePreparationAttribute Attribute)>();
        var useReturns = new SortedDictionary<int, UseReturn>();
        var attackReturns = new SortedDictionary<(NativeAttackReturnPhase, int), AttackReturn>();
        foreach (var method in methods)
        {
            if (method.GetCustomAttribute<NativeUseValidationAttribute>() is { } validation)
            {
                RequireInstance(method);
                if (method.ReturnType != typeof(void) ||
                    (!Parameters(method, typeof(CardUseFrame)) && !Parameters(method, typeof(CardUseFrame).MakeByRefType())) ||
                    method.GetParameters().Any(parameter => parameter.IsOut) || validation.Order <= 0 ||
                    !validations.TryAdd(validation.Order, new(validation.Order, method))) throw Invalid(method);
            }
            if (method.GetCustomAttribute<NativeUsePreparationAttribute>() is { } preparation)
            {
                RequireInstance(method);
                if (method.ReturnType != typeof(bool) || !Parameters(method, typeof(CardUseFrame)) || preparation.Order <= 0 ||
                    (preparation.ReturnOrder == 0 ? preparation.Continuation is not null :
                        preparation.ReturnOrder < 0 || string.IsNullOrWhiteSpace(preparation.Continuation)) ||
                    !preparations.TryAdd(preparation.Order, (new(preparation.Order, method), preparation))) throw Invalid(method);
            }
            if (method.GetCustomAttribute<NativeUseReturnAttribute>() is { } useReturn)
            {
                RequireInstance(method);
                if (!ValidUseReturn(method) || useReturn.Order <= 0 ||
                    !useReturns.TryAdd(useReturn.Order, new(useReturn.Order, method))) throw Invalid(method);
            }
            if (method.GetCustomAttribute<NativeAttackReturnAttribute>() is { } attackReturn)
            {
                RequireInstance(method);
                if (!Enum.IsDefined(attackReturn.Phase) || attackReturn.Order <= 0 ||
                    (method.ReturnType != typeof(bool) && method.ReturnType != typeof(void)) ||
                    (!Parameters(method, typeof(AttackReceipt)) && !Parameters(method)) ||
                    !attackReturns.TryAdd((attackReturn.Phase, attackReturn.Order),
                        new(attackReturn.Phase, attackReturn.Order, method))) throw Invalid(method);
            }
        }
        var orderedPreparations = preparations.Values.ToArray();
        for (var index = 0; index < orderedPreparations.Length; index++)
        {
            var (entry, declaration) = orderedPreparations[index];
            if (declaration.ReturnOrder == 0) continue;
            var candidates = entry.Method.DeclaringType!.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic).Where(method => method.Name == declaration.Continuation).ToArray();
            if (candidates is not [var continuation]) throw Invalid(entry.Method);
            RequireInstance(continuation);
            if (continuation.ReturnType != typeof(void) || !Parameters(continuation, typeof(CardUseFrame)) ||
                !useReturns.TryAdd(declaration.ReturnOrder, new(declaration.ReturnOrder, continuation, index))) throw Invalid(entry.Method);
        }
        if (validations.Count == 0 || preparations.Count == 0 || useReturns.Count == 0 ||
            Enum.GetValues<NativeAttackReturnPhase>().Any(phase => !attackReturns.Keys.Any(key => key.Item1 == phase)))
            throw new InvalidOperationException("Native returns require validation, preparation, use-return and every attack-return phase.");
        Validations = Array.AsReadOnly(validations.Values.ToArray());
        Preparations = Array.AsReadOnly(orderedPreparations.Select(item => item.Entry).ToArray());
        UseReturns = Array.AsReadOnly(useReturns.Values.ToArray());
        AttackReturns = Array.AsReadOnly(attackReturns.Values.ToArray());
    }

    private static bool ValidUseReturn(MethodInfo method) => method.ReturnType == typeof(void)
        ? Parameters(method, typeof(CardUseFrame)) || Parameters(method, typeof(NativeUseReturnContext)) ||
          Parameters(method, typeof(long)) || Parameters(method)
        : method.ReturnType == typeof(bool) &&
          (Parameters(method, typeof(CardUseFrame)) || Parameters(method, typeof(NativeUseReturnContext)));
    private static bool Parameters(MethodInfo method, params Type[] types) =>
        method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(types);
    private static void RequireInstance(MethodInfo method)
    {
        if (method.IsStatic || method.IsAbstract || method.IsGenericMethod || method.ContainsGenericParameters)
            throw Invalid(method);
    }
    private static InvalidOperationException Invalid(MethodInfo method) =>
        new($"Native return '{method.DeclaringType?.Name}.{method.Name}' has an invalid or duplicate typed registration.");
}

internal sealed class ProgramNativeReturns
{
    private readonly NativeUseValidation[] _validations;
    private readonly Func<CardUseFrame, bool>[] _preparations;
    private readonly Func<NativeUseReturnContext, bool>[] _useReturns;
    private readonly IReadOnlyDictionary<NativeAttackReturnPhase, Func<AttackReceipt, bool>[]> _attackReturns;

    internal ProgramNativeReturns(ProgramNativeReturnCatalog catalog, Func<MethodInfo, Type, Delegate> bind)
    {
        T Bind<T>(MethodInfo method) where T : Delegate => (T)bind(method, typeof(T));
        _validations = catalog.Validations.Select<ProgramNativeReturnCatalog.Validation, NativeUseValidation>(entry =>
        {
            if (entry.Method.GetParameters()[0].ParameterType.IsByRef) return Bind<NativeUseValidation>(entry.Method);
            var handler = Bind<Action<CardUseFrame>>(entry.Method);
            return (ref CardUseFrame use) => handler(use);
        }).ToArray();
        _preparations = catalog.Preparations.Select(entry => Bind<Func<CardUseFrame, bool>>(entry.Method)).ToArray();
        _useReturns = catalog.UseReturns.Select(entry => entry.PreparationIndex < 0
            ? BindUseReturn(entry.Method, bind)
            : PreparedReturn(entry.PreparationIndex, Bind<Action<CardUseFrame>>(entry.Method))).ToArray();
        _attackReturns = catalog.AttackReturns.GroupBy(entry => entry.Phase).ToDictionary(group => group.Key,
            group => group.Select(entry => BindAttackReturn(entry.Method, bind)).ToArray());
    }

    private static Func<NativeUseReturnContext, bool> PreparedReturn(int index, Action<CardUseFrame> continuation) => context =>
    {
        if (!context.Prepared[index]) return false;
        continuation(context.Use);
        return true;
    };

    private static Func<NativeUseReturnContext, bool> BindUseReturn(MethodInfo method, Func<MethodInfo, Type, Delegate> bind)
    {
        if (method.ReturnType == typeof(bool))
        {
            if (method.GetParameters()[0].ParameterType == typeof(NativeUseReturnContext))
                return (Func<NativeUseReturnContext, bool>)bind(method, typeof(Func<NativeUseReturnContext, bool>));
            var handler = (Func<CardUseFrame, bool>)bind(method, typeof(Func<CardUseFrame, bool>));
            return context => handler(context.Use);
        }
        var parameters = method.GetParameters();
        if (parameters.Length == 0)
        {
            var handler = (Action)bind(method, typeof(Action));
            return _ => { handler(); return false; };
        }
        if (parameters[0].ParameterType == typeof(long))
        {
            var handler = (Action<long>)bind(method, typeof(Action<long>));
            return context => { handler(context.Use.Id); return false; };
        }
        if (parameters[0].ParameterType == typeof(NativeUseReturnContext))
        {
            var handler = (Action<NativeUseReturnContext>)bind(method, typeof(Action<NativeUseReturnContext>));
            return context => { handler(context); return false; };
        }
        var useHandler = (Action<CardUseFrame>)bind(method, typeof(Action<CardUseFrame>));
        return context => { useHandler(context.Use); return false; };
    }

    private static Func<AttackReceipt, bool> BindAttackReturn(MethodInfo method, Func<MethodInfo, Type, Delegate> bind)
    {
        if (method.GetParameters().Length == 0)
        {
            if (method.ReturnType == typeof(bool))
            {
                var handler = (Func<bool>)bind(method, typeof(Func<bool>));
                return _ => handler();
            }
            var action = (Action)bind(method, typeof(Action));
            return _ => { action(); return false; };
        }
        if (method.ReturnType == typeof(bool))
            return (Func<AttackReceipt, bool>)bind(method, typeof(Func<AttackReceipt, bool>));
        var receiptAction = (Action<AttackReceipt>)bind(method, typeof(Action<AttackReceipt>));
        return receipt => { receiptAction(receipt); return false; };
    }

    internal void ValidateUse(ref CardUseFrame use)
    {
        foreach (var validate in _validations) validate(ref use);
    }
    internal NativeUseReturnContext PrepareUse(CardUseFrame use, bool hpLossMaterialReturn)
    {
        var context = new NativeUseReturnContext(use, hpLossMaterialReturn, _preparations.Length);
        for (var index = 0; index < _preparations.Length; index++) context.Prepared[index] = _preparations[index](use);
        return context;
    }
    internal bool ReturnUse(NativeUseReturnContext context)
    {
        context.BeginReturn();
        foreach (var handler in _useReturns) if (handler(context)) return true;
        return false;
    }
    internal bool ReturnAttack(NativeAttackReturnPhase phase, AttackReceipt completion)
    {
        foreach (var handler in _attackReturns[phase]) if (handler(completion)) return true;
        return false;
    }
}

public sealed partial class GameEngine
{
    private static readonly Lazy<ProgramNativeReturnCatalog> NativeReturnMetadata = new(() =>
        new(typeof(GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)), true);
    private ProgramNativeReturns? _nativeReturns;
    private ProgramNativeReturns NativeReturns => _nativeReturns ??= new(NativeReturnMetadata.Value,
        (method, type) => method.CreateDelegate(type, this));
}
