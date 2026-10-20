using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramCapabilityChecks
{
    public static void RegistrationAndIsolationPreserveBoundaries()
    {
        var content = StandardContentRegistry.Create();
        var firstGame = GameEngine.CreateStandard(new GameOptions { Seed = 7 }, content);
        var secondGame = GameEngine.CreateStandard(new GameOptions { Seed = 7 }, content);
        var types = typeof(GameEngine).GetNestedTypes(BindingFlags.NonPublic).Concat(
            ProgramRuntimeModuleCatalog.Default.Registrations.Select(registration => registration.Type)).ToArray();
        var production = new ProgramCapabilityCatalog(types);
        var reversed = new ProgramCapabilityCatalog(types.Reverse());
        Require(production.Registrations.Select(item => item.Implementation)
                .SequenceEqual(reversed.Registrations.Select(item => item.Implementation)),
            "Discovery order must not change the registered capability modules.");

        var hostType = typeof(GameEngine).GetNestedType("ProgramSkillHost", BindingFlags.NonPublic)!;
        Require(hostType.GetInterfaces().OrderBy(type => type.FullName, StringComparer.Ordinal)
                .SequenceEqual(new[] { typeof(ISkillProgramExecutionHost), typeof(ISkillProgramEffectHost), typeof(IResumableProgramDrawHost) }
                    .OrderBy(type => type.FullName, StringComparer.Ordinal)),
            "The shared host keeps execution/effect contracts plus the optional resumable Draw adapter; character capabilities stay separate.");
        var constructor = hostType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Single();
        var firstHost = (ISkillProgramEffectHost)constructor.Invoke([firstGame]);
        var secondHost = (ISkillProgramEffectHost)constructor.Invoke([secondGame]);
        var registryField = typeof(GameEngine).GetField("_programCapabilities", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Require(registryField.GetValue(firstGame) is ProgramCapabilityRegistry firstRegistry &&
                registryField.GetValue(secondGame) is ProgramCapabilityRegistry secondRegistry &&
                !ReferenceEquals(firstRegistry, secondRegistry),
            "Each constructor must validate registration and install its own capability registry.");
        var get = typeof(ISkillProgramEffectHost).GetMethod(nameof(ISkillProgramEffectHost.GetCapability))!;
        foreach (var registration in production.Registrations)
        {
            object? shared = null;
            foreach (var contract in registration.Contracts)
            {
                var method = get.MakeGenericMethod(contract);
                var capability = method.Invoke(firstHost, null);
                Require(capability?.GetType() == registration.Implementation &&
                        ReferenceEquals(capability, method.Invoke(firstHost, null)) &&
                        !ReferenceEquals(capability, method.Invoke(secondHost, null)),
                    $"Production capability '{contract.FullName}' must construct, resolve and cache per game.");
                if (shared is not null)
                    Require(ReferenceEquals(shared, capability), "One module must share its instance across its own contracts.");
                shared = capability;
            }
        }

        FixtureCapability.Constructions = 0;
        var catalog = new ProgramCapabilityCatalog([typeof(UnregisteredCapability), typeof(FixtureCapability)]);
        var registry = new ProgramCapabilityRegistry(firstGame, catalog);
        var another = new ProgramCapabilityRegistry(secondGame, catalog);
        Require(FixtureCapability.Constructions == 0, "Registration must not eagerly construct capability modules.");
        var first = registry.Get<IFixtureCapability>();
        Require(FixtureCapability.Constructions == 1 && ReferenceEquals(first.Owner, firstGame) &&
                ReferenceEquals(first, registry.Get<IFixtureCapability>()) &&
                ReferenceEquals(first, registry.Get<ISecondFixtureCapability>()),
            "All interfaces of a module must share one lazily constructed instance for their game.");
        var second = another.Get<IFixtureCapability>();
        Require(FixtureCapability.Constructions == 2 && !ReferenceEquals(first, second) && ReferenceEquals(second.Owner, secondGame),
            "Shared metadata must never retain another game's adapter or state.");
        Require(!registry.TryGet<IMissingCapability>(out var missing) && missing is null,
            "An unknown optional capability must decline without manufacturing an implementation.");
        Reject<NotSupportedException>(() => registry.Get<IMissingCapability>());
        Reject<ArgumentException>(() => registry.Get<object>());
        Reject<ArgumentNullException>(() => new ProgramCapabilityCatalog(null!));
        Reject<InvalidOperationException>(() => new ProgramCapabilityCatalog([null!]));
        Reject<InvalidOperationException>(() => new ProgramCapabilityCatalog([]));
        Reject<InvalidOperationException>(() => new ProgramCapabilityCatalog([typeof(UnregisteredCapability)]));
        Reject<InvalidOperationException>(() => new ProgramCapabilityCatalog([typeof(FixtureCapability), typeof(FixtureCapability)]));
        Reject<InvalidOperationException>(() => new ProgramCapabilityCatalog([typeof(FixtureCapability), typeof(DuplicateCapability)]));
        foreach (var invalid in new[]
                 {
                     typeof(UnsealedCapability), typeof(GenericCapability<>), typeof(GenericCapability<int>), typeof(MissingConstructorCapability),
                     typeof(ExtraConstructorCapability), typeof(EmptyCapability), typeof(SharedHostCapability)
                 })
            Reject<InvalidOperationException>(() => new ProgramCapabilityCatalog([invalid]));

        var throwing = new ProgramCapabilityRegistry(firstGame, new ProgramCapabilityCatalog([typeof(ThrowingCapability)]));
        try { throwing.Get<IFixtureCapability>(); throw new Exception("A constructor failure was swallowed."); }
        catch (InvalidOperationException error)
        {
            Require(ReferenceEquals(error, ThrowingCapability.Failure), "Constructor failures must retain the original exception.");
        }

        var frame = new ProgramSkillFrame(41, 0, "fixture:capability", "run", "hash", 0, [], []);
        var legacy = new LegacyHost(frame);
        ISkillProgramEffectHost legacyHost = legacy;
        Require(ReferenceEquals(legacyHost.GetCapability<IConversionPolarityProgramHost>(), legacy) &&
                legacyHost.TryGetCapability<IConversionPolarityProgramHost>(out var direct) && ReferenceEquals(direct, legacy),
            "Existing custom hosts that implement dedicated interfaces must keep working without a registry.");
        new CommitConversionPolarityHandler().Execute(
            new(SkillProgramEffectOp.CommitConversionPolarity, SkillProgramEffectTarget.Owner, 0,
                new(SkillProgramConditionKind.Always, 0, [])), frame, frame.OwnerSeat, legacyHost);
        Require(legacy.Commits == 1, "A migrated effect handler must invoke an existing custom host exactly once.");
        Require(!legacyHost.TryGetCapability<IMissingCapability>(out _), "Legacy hosts must decline unsupported optional capabilities.");
        Reject<NotSupportedException>(() => legacyHost.GetCapability<IMissingCapability>());
    }

    private interface IFixtureCapability { GameEngine Owner { get; } }
    private interface ISecondFixtureCapability { }
    private interface IMissingCapability { }

    [ProgramCapability]
    private sealed class FixtureCapability : IFixtureCapability, ISecondFixtureCapability
    {
        internal static int Constructions;
        public GameEngine Owner { get; }
        internal FixtureCapability(GameEngine engine) { Owner = engine; Constructions++; }
    }

    private sealed class UnregisteredCapability : IFixtureCapability
    {
        public GameEngine Owner => throw new NotSupportedException();
    }

    [ProgramCapability]
    private sealed class DuplicateCapability(GameEngine engine) : IFixtureCapability
    {
        public GameEngine Owner => engine;
    }

    [ProgramCapability]
    private class UnsealedCapability(GameEngine engine) : IFixtureCapability
    {
        public GameEngine Owner => engine;
    }

    [ProgramCapability]
    private sealed class GenericCapability<T>(GameEngine engine) : IFixtureCapability
    {
        public GameEngine Owner => engine;
    }

    [ProgramCapability]
    private sealed class MissingConstructorCapability : IFixtureCapability
    {
        public GameEngine Owner => throw new NotSupportedException();
    }

    [ProgramCapability]
    private sealed class ExtraConstructorCapability : IFixtureCapability
    {
        internal ExtraConstructorCapability() { }
        internal ExtraConstructorCapability(GameEngine engine) { }
        public GameEngine Owner => throw new NotSupportedException();
    }

    [ProgramCapability]
    private sealed class EmptyCapability
    {
        internal EmptyCapability(GameEngine engine) { }
    }

    [ProgramCapability]
    private sealed class SharedHostCapability : SkillProgramExecutorChecks.FakeRuntime
    {
        internal SharedHostCapability(GameEngine engine)
            : base(null!, new(41, 0, "fixture:capability", "run", "hash", 0, [], [])) { }
    }

    [ProgramCapability]
    private sealed class ThrowingCapability : IFixtureCapability
    {
        internal static readonly InvalidOperationException Failure = new("Fixture constructor failure.");
        internal ThrowingCapability(GameEngine engine) => throw Failure;
        public GameEngine Owner => throw new NotSupportedException();
    }

    private sealed class LegacyHost(ProgramSkillFrame frame)
        : SkillProgramExecutorChecks.FakeRuntime(null!, frame), IConversionPolarityProgramHost
    {
        internal int Commits { get; private set; }
        public void CommitConversionPolarity(ProgramSkillFrame current)
        {
            Require(ReferenceEquals(current, Frame), "The legacy capability lost the caller's original frame.");
            Commits++;
        }
    }

    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name} for an invalid capability operation.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
