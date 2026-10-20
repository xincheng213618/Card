using System.Reflection;
using CardGame.Content.Standard;
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
        CheckMechanismBindingsAndFramePrimitives();
    }

    private static void CheckMechanismBindingsAndFramePrimitives()
    {
        var content = StandardContentRegistry.Create();
        var first = GameEngine.CreateStandard(new GameOptions { Seed = 7 }, content);
        var second = GameEngine.CreateStandard(new GameOptions { Seed = 7 }, content);
        var metadata = ProgramRuntimeModuleCatalog.Default;
        foreach (var registration in metadata.Registrations.Where(item => !item.Type.IsAbstract))
        {
            var instance = first.GetRuntimeModuleInstance(registration.Type);
            Require(ReferenceEquals(instance, first.GetRuntimeModuleInstance(registration.Type)) &&
                    !ReferenceEquals(instance, second.GetRuntimeModuleInstance(registration.Type)),
                "Stateful mechanism runtimes must bind once per match, including modules used only by compatibility bridges.");
            Require(registration.Type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .All(field => field.FieldType.IsInterface &&
                        (typeof(IProgramSelectionQuery).IsAssignableFrom(field.FieldType) ||
                         typeof(IProgramRuntimeOperations).IsAssignableFrom(field.FieldType))),
                "Production runtimes retain only narrow ports; paid cursors and mutable stores remain outside their instances.");
        }
        var capabilities = new ProgramCapabilityCatalog(typeof(GameEngine).GetNestedTypes(BindingFlags.NonPublic)
            .Concat(metadata.Registrations.Select(item => item.Type)));
        var adapters = new ProgramCapabilityRegistry(first, capabilities);
        Require(ReferenceEquals(adapters.Get<IProgramWindowCursors>(), first.GetRuntimeModule<ProgramWindowCursorRuntime>()),
            "Native returns and ordinary window dispatch must share the same cursor runtime instance.");
        var returnMethod = typeof(ProgramWindowReturnRuntime).GetMethod("ReturnDrawPhaseEndedProgramWindow",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Require(ReferenceEquals(first.BindRuntimeHandler(returnMethod, typeof(Action<ProgramSkillFrame, bool>)).Target,
                first.GetRuntimeModule<ProgramWindowReturnRuntime>()),
            "Registered window returns bind to the same runtime used by compatibility entry points.");

        var frames = adapters.Get<IProgramRuntimeFrames>();
        var original = frames.Top;
        var frame = new ProgramSkillFrame(41, 0, "fixture:runtime", "run", "hash", 0, [], [])
        {
            NativeDraw = new NativeDrawProducerReceipt(1, 1, null, SkillProgramCardSetVisibility.Private,
                new CardMoveReason("fixture.runtime")) { Recipients = [0] }
        };
        frames.Push(frame);
        Reject(() => frames.ReplaceTop(frame with { Id = 42 }));
        Reject(() => frames.Pop(frame.Id, frame.Kind));
        Require(ReferenceEquals(frames.Top, frame), "A rejected transition cannot retire or change an unconsumed paid producer.");
        frames.ReplaceTop(frame with { NativeDraw = null });
        frames.Pop(frame.Id, frame.Kind);
        Require(ReferenceEquals(frames.Top, original), "Consuming the original receipt restores the original parent exactly once.");
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
