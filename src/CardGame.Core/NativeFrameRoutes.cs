using System.Reflection;
using System.Collections.ObjectModel;

namespace CardGame.Core;

// Local registration replaces the central frame/character switch. Conditions
// inspect the selected frame only; the first matching route owns this step.
[AttributeUsage(AttributeTargets.Method)]
internal sealed class NativeFrameRouteAttribute(Type frameType, int order,
    Type? conditionType = null, string? condition = null) : Attribute
{
    internal Type FrameType { get; } = frameType;
    internal int Order { get; } = order;
    internal Type? ConditionType { get; } = conditionType;
    internal string? Condition { get; } = condition;
}

internal sealed class NativeFrameRouteCatalog
{
    internal static NativeFrameRouteCatalog Default { get; } = new(ProgramRuntimeModuleCatalog.Default.Methods);
    internal sealed record Registration(Type FrameType, int Order, MethodInfo Method, MethodInfo? Condition,
        Func<GameEngine, ResolutionFrame, bool> TryAdvance);
    internal IReadOnlyList<Registration> Registrations { get; }
    private readonly IReadOnlyDictionary<Type, Registration[]> _byFrameType;

    internal NativeFrameRouteCatalog(IEnumerable<MethodInfo> methods)
    {
        var registrations = new List<Registration>();
        foreach (var method in methods)
        {
            if (method.GetCustomAttribute<NativeFrameRouteAttribute>() is not { } route) continue;
            var parameters = method.GetParameters();
            if (!ValidHost(method) || method.ReturnType != typeof(void) || route.Order < 0 ||
                route.FrameType.IsAbstract || route.FrameType.ContainsGenericParameters ||
                !typeof(ResolutionFrame).IsAssignableFrom(route.FrameType) || parameters.Length > 1 ||
                parameters.Any(parameter => parameter.IsOptional || parameter.IsOut || parameter.ParameterType.IsByRef ||
                    parameter.ParameterType != typeof(long) && parameter.ParameterType != route.FrameType))
                throw new InvalidOperationException($"Native frame route '{method.Name}' has an invalid typed signature.");
            MethodInfo? condition = null;
            if (route.ConditionType is not null || route.Condition is not null)
            {
                condition = route.ConditionType?.GetMethod(route.Condition ?? "", BindingFlags.Static | BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.NonPublic, [route.FrameType]);
                if (condition is null || !ValidHost(condition) || condition.ReturnType != typeof(bool) ||
                    condition.GetParameters().Any(parameter => parameter.IsOptional || parameter.IsOut))
                    throw new InvalidOperationException($"Native frame route '{method.Name}' has an invalid condition.");
            }
            var binder = typeof(NativeFrameRouteCatalog).GetMethod(nameof(Bind), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(route.FrameType);
            var invoke = (Func<GameEngine, ResolutionFrame, bool>)binder.Invoke(null, [method, condition])!;
            registrations.Add(new(route.FrameType, route.Order, method, condition, invoke));
        }
        var ordered = registrations.OrderBy(route => route.Order).ThenBy(route => route.Method.DeclaringType!.FullName,
            StringComparer.Ordinal).ThenBy(route => route.Method.Name, StringComparer.Ordinal).ToArray();
        if (ordered.Length == 0 || ordered.GroupBy(route => (route.FrameType, route.Order)).Any(group => group.Count() > 1) ||
            ordered.Any(route => route.Condition is null && ordered.Any(later => later != route &&
                route.Order <= later.Order && route.FrameType.IsAssignableFrom(later.FrameType))))
            throw new InvalidOperationException("Native frame routes require unambiguous ordered ownership.");
        Registrations = Array.AsReadOnly(ordered);
        _byFrameType = new ReadOnlyDictionary<Type, Registration[]>(ordered.Select(route => route.FrameType).Distinct()
            .ToDictionary(type => type, type => ordered.Where(route => route.FrameType.IsAssignableFrom(type)).ToArray()));
    }

    private static bool ValidHost(MethodInfo method) => !method.IsGenericMethod && !method.ContainsGenericParameters &&
        (method.DeclaringType == typeof(GameEngine) || ProgramRuntimeModuleCatalog.IsDeclared(method.DeclaringType));

    private static Func<GameEngine, ResolutionFrame, bool> Bind<TFrame>(MethodInfo method, MethodInfo? condition)
        where TFrame : ResolutionFrame
    {
        var parameters = method.GetParameters();
        return (engine, selected) =>
        {
            if (selected is not TFrame frame) return false;
            if (condition is not null && !((Func<TFrame, bool>)engine.BindRuntimeHandler(condition, typeof(Func<TFrame, bool>)))(frame))
                return false;
            if (parameters.Length == 0)
                ((Action)engine.BindRuntimeHandler(method, typeof(Action)))();
            else if (parameters[0].ParameterType == typeof(long))
                ((Action<long>)engine.BindRuntimeHandler(method, typeof(Action<long>)))(frame.Id);
            else ((Action<TFrame>)engine.BindRuntimeHandler(method, typeof(Action<TFrame>)))(frame);
            return true;
        };
    }

    internal bool TryAdvance(GameEngine engine, ResolutionFrame frame)
    {
        var candidates = _byFrameType.TryGetValue(frame.GetType(), out var known) ? known : Registrations;
        foreach (var route in candidates)
            if (route.TryAdvance(engine, frame)) return true;
        return false;
    }
}

[ProgramRuntimeModule]
internal static class NativeFrameRouteConditions
{
    internal static bool SoleIncomingRangeDraw(CardUseFrame frame) => frame.SoleIncomingRangeDraw is not null;
    internal static bool NativeCardEffectDraw(CardUseFrame frame) => frame.NativeCardEffectDraw is not null;
    internal static bool GuanduCardEffect(CardUseFrame frame) => frame.GuanduCardEffect is not null;
    internal static bool WumiaoFlood(CardUseFrame frame) => frame.WumiaoFlood is not null;
    internal static bool DragonBoatCardEffect(CardUseFrame frame) => frame.DragonBoatCardEffect is not null;
    internal static bool CounterspellPayment(NullificationWindowFrame frame) => frame.CounterspellPayment is not null;
    internal static bool ColorFireAttackPayment(CardUseFrame frame) => frame.ColorFireAttack?.PaidCardId is not null;
    internal static bool MouZhitianPayment(CardUseFrame frame) => frame.MouZhitianPayment is not null;
    internal static bool GuoSiPayment(CardUseFrame frame) => frame is { GuoSiNative: not null, GuoSiCostDrained: false };
    internal static bool DenglouPayment(CardUseFrame frame) => frame is { DenglouNative: not null, DenglouCostDrained: false };
    internal static bool HistoricalEndingPayment(CardUseFrame frame) => frame is { EndingHistoricalUseReturn: not null, EndingHistoricalCostDrained: false };
    internal static bool RoundPileAlcoholPayment(CardUseFrame frame) => frame is { RoundPileAlcoholReturn: not null, RoundPileAlcoholCostDrained: false };
}
