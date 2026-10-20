using System.Collections.ObjectModel;
using System.Reflection;

namespace CardGame.Core;

// Positions in the existing scheduler. A route opts in locally; explicit child
// returns do not acquire an automatic step just because they have a route.
internal enum NativeFrameAutomaticPhase
{
    BeforeOpening,
    AfterOpening,
    AfterDeclarationChallenge,
    BeforeJudgmentDecision
}

internal enum NativeFrameAutomaticPending
{
    RequireNoneBeforeFrame,
    RequireNoneAfterFrame,
    Ignore
}

internal enum NativeFrameAutomaticPublish { AfterAdvance, None }

[AttributeUsage(AttributeTargets.Method)]
internal sealed class NativeFrameAutomaticStepAttribute(NativeFrameAutomaticPhase phase, int order,
    NativeFrameAutomaticPending pending = NativeFrameAutomaticPending.RequireNoneBeforeFrame,
    NativeFrameAutomaticPublish publish = NativeFrameAutomaticPublish.AfterAdvance) : Attribute
{
    internal NativeFrameAutomaticPhase Phase { get; } = phase;
    internal int Order { get; } = order;
    internal NativeFrameAutomaticPending Pending { get; } = pending;
    internal NativeFrameAutomaticPublish Publish { get; } = publish;
}

internal sealed record NativeFrameAutomaticStepMetadata(NativeFrameAutomaticPhase Phase, int Order,
    NativeFrameAutomaticPending Pending, NativeFrameAutomaticPublish Publish);

internal sealed class NativeFrameAutomaticStepCatalog
{
    internal static NativeFrameAutomaticStepCatalog Default { get; } = new(
        NativeFrameRouteCatalog.Default, ProgramRuntimeModuleCatalog.Default.Methods);
    internal sealed record Registration(NativeFrameAutomaticStepMetadata Step,
        NativeFrameRouteCatalog.Registration Route, Func<GameEngine, ResolutionFrame, bool> Matches);
    internal IReadOnlyDictionary<NativeFrameAutomaticPhase, IReadOnlyList<Registration>> Registrations { get; }

    internal NativeFrameAutomaticStepCatalog(NativeFrameRouteCatalog routes, IEnumerable<MethodInfo> methods)
    {
        var registrations = new List<Registration>();
        foreach (var method in methods)
        {
            if (method.GetCustomAttribute<NativeFrameAutomaticStepAttribute>() is not { } step) continue;
            var route = routes.Registrations.SingleOrDefault(candidate => candidate.Method == method);
            if (route is null || step.Order < 0 || !Enum.IsDefined(step.Phase) ||
                !Enum.IsDefined(step.Pending) || !Enum.IsDefined(step.Publish))
                throw new InvalidOperationException($"Native automatic step '{method.Name}' has invalid route metadata.");
            var binder = typeof(NativeFrameAutomaticStepCatalog).GetMethod(nameof(BindMatch),
                BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(route.FrameType);
            var matches = (Func<GameEngine, ResolutionFrame, bool>)binder.Invoke(null, [route.Condition])!;
            registrations.Add(new(new(step.Phase, step.Order, step.Pending, step.Publish), route, matches));
        }
        if (registrations.GroupBy(item => (item.Step.Phase, item.Step.Order)).Any(group => group.Count() != 1))
            throw new InvalidOperationException("Native automatic steps require unique orders within each scheduler phase.");
        Registrations = new ReadOnlyDictionary<NativeFrameAutomaticPhase, IReadOnlyList<Registration>>(
            Enum.GetValues<NativeFrameAutomaticPhase>().ToDictionary(phase => phase,
                phase => (IReadOnlyList<Registration>)Array.AsReadOnly(registrations
                    .Where(item => item.Step.Phase == phase).OrderBy(item => item.Step.Order).ToArray())));
    }

    private static Func<GameEngine, ResolutionFrame, bool> BindMatch<TFrame>(MethodInfo? condition)
        where TFrame : ResolutionFrame => (engine, selected) =>
        selected is TFrame frame && (condition is null ||
            ((Func<TFrame, bool>)engine.BindRuntimeHandler(condition, typeof(Func<TFrame, bool>)))(frame));
}
