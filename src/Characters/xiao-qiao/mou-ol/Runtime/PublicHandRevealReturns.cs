using System.Reflection;

namespace CardGame.Core;

// A reveal producer registers its exact existing native continuation beside its
// implementation. Only immutable method metadata is shared between matches.
[AttributeUsage(AttributeTargets.Method)]
internal sealed class PublicHandRevealReturnAttribute(string key) : Attribute
{ internal string Key { get; } = key; }
internal sealed class PublicHandRevealReturns
{
    internal static PublicHandRevealReturns Default { get; } = new(ProgramRuntimeModuleCatalog.Default.Methods);
    private readonly IReadOnlyDictionary<string, Action<GameEngine, ResolutionFrame>> _returns;
    private PublicHandRevealReturns(IEnumerable<MethodInfo> methods)
    {
        var result = new Dictionary<string, Action<GameEngine, ResolutionFrame>>(StringComparer.Ordinal);
        foreach (var method in methods)
        {
            if (method.GetCustomAttribute<PublicHandRevealReturnAttribute>() is not { } a) continue;
            var p = method.GetParameters();
            if (string.IsNullOrWhiteSpace(a.Key) || method.ReturnType != typeof(void) || method.IsGenericMethod || method.IsAbstract ||
                p.Length != 1 || p[0].IsOut || p[0].IsOptional || !typeof(ResolutionFrame).IsAssignableFrom(p[0].ParameterType))
                throw new InvalidOperationException("A public Hand reveal requires a keyed, strongly typed native return.");
            var bind = typeof(PublicHandRevealReturns).GetMethod(nameof(Bind), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(p[0].ParameterType);
            if (!result.TryAdd(a.Key, (Action<GameEngine, ResolutionFrame>)bind.Invoke(null, [method])!))
                throw new InvalidOperationException("Duplicate public Hand reveal return key: " + a.Key);
        }
        _returns = result;
    }
    private static Action<GameEngine, ResolutionFrame> Bind<T>(MethodInfo method) where T : ResolutionFrame => (engine, parent) =>
    {
        if (parent is not T exact) throw new InvalidOperationException("Public Hand reveal changed its exact native producer type.");
        ((Action<T>)engine.BindRuntimeHandler(method, typeof(Action<T>)))(exact);
    };
    internal void Return(GameEngine engine, string key, ResolutionFrame parent)
    {
        if (!_returns.TryGetValue(key, out var callback)) throw new InvalidOperationException("Public Hand reveal lost its registered native return.");
        callback(engine, parent);
    }
}
