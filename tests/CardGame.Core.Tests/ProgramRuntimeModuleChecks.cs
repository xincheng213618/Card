using System.Reflection;
using CardGame.Core;

internal static class ProgramRuntimeModuleChecks
{
    public static void IndependentFactoriesShareMatchInstancesAndRejectEngineDependencies()
    {
        var types = new[] { typeof(FixtureModule), typeof(StatelessModule) };
        var catalog = new ProgramRuntimeModuleCatalog(types);
        var reversed = new ProgramRuntimeModuleCatalog(types.Reverse());
        Require(catalog.Methods.SequenceEqual(reversed.Methods), "Module discovery must be deterministic.");
        var firstFacts = new Facts(3);
        var secondFacts = new Facts(7);
        var first = new ProgramRuntimeModuleRegistry(catalog, type => type == typeof(IFacts) ? firstFacts : throw new Exception());
        var second = new ProgramRuntimeModuleRegistry(catalog, _ => secondFacts);
        var gate = typeof(FixtureModule).GetMethod(nameof(FixtureModule.Gate))!;
        var effect = typeof(FixtureModule).GetMethod(nameof(FixtureModule.Effect))!;
        var boundGate = (Func<int>)first.Bind(gate, typeof(Func<int>), new object());
        var boundEffect = (Action)first.Bind(effect, typeof(Action), new object());
        Require(ReferenceEquals(boundGate, first.Bind(gate, typeof(Func<int>), new object())),
            "Different entry points cache delegates on the same match registry.");
        Require(boundGate() == 3, "Constructor facts must come from the owning match.");
        boundEffect();
        Require(boundGate() == 4 && ReferenceEquals(boundGate.Target, boundEffect.Target) &&
                ReferenceEquals(boundGate.Target, first.Get(typeof(FixtureModule))),
            "All hooks and explicit module resolution must share exactly one module instance.");
        Require(((Func<int>)second.Bind(gate, typeof(Func<int>), new object()))() == 7 &&
                !ReferenceEquals(boundGate.Target, second.Get(typeof(FixtureModule))),
            "Match instances and module-local counters cannot leak between games.");
        var staticMethod = typeof(StatelessModule).GetMethod(nameof(StatelessModule.Double))!;
        Require(((Func<int, int>)first.Bind(staticMethod, typeof(Func<int, int>), new object()))(5) == 10,
            "A declared stateless static module binds without manufacturing an instance.");
        Require(new ProgramCapabilityCatalog([typeof(FixtureModule)]).Registrations.Single().Implementation == typeof(FixtureModule),
            "An independent module may expose dedicated capability contracts.");
        Reject(() => new ProgramRuntimeModuleCatalog([typeof(EngineDependency)]));
        Reject(() => new ProgramRuntimeModuleCatalog([typeof(UndeclaredDependency)]));
        Reject(() => new ProgramRuntimeModuleCatalog([typeof(MutableStaticModule)]));
        Reject(() => first.Get(typeof(StatelessModule)));
        Reject(() => new ProgramRuntimeModuleRegistry(catalog, _ => new object()).Get(typeof(FixtureModule)));
    }

    private interface IFacts : IProgramSelectionQuery { int Value { get; } }
    private interface IFixtureCapability { int Gate(); }
    private sealed record Facts(int Value) : IFacts;

    [ProgramCapability, ProgramRuntimeModule]
    private sealed class FixtureModule(IFacts facts) : IFixtureCapability
    {
        private int _calls;
        public int Gate() => facts.Value + _calls;
        public void Effect() => _calls++;
    }

    [ProgramRuntimeModule]
    private static class StatelessModule
    {
        private const int Factor = 2;
        public static int Double(int value) => value * Factor;
    }

    [ProgramRuntimeModule]
    private sealed class EngineDependency(GameEngine engine) { private readonly object _host = engine; }
    [ProgramRuntimeModule]
    private sealed class UndeclaredDependency(IDisposable value) { private readonly object _value = value; }
    [ProgramRuntimeModule]
    private static class MutableStaticModule { internal static int Counter = 0; }

    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("An invalid runtime module was accepted.");
    }
}
