using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramRuntimeHookChecks
{
    public static void OrderedPhasesAndMatchIsolation()
    {
        var catalog = ProgramRuntimeHookCatalog.Discover(Methods(typeof(Fixture)).Reverse());
        var a = new Fixture(catalog) { Stop = true };
        var b = new Fixture(catalog);
        Require(a.Hooks.TryAdvance(ProgramRuntimePhase.BeforeResponseHealth) && a.Trace.SequenceEqual(["first", "second"]) && b.Trace.Count == 0,
            "Subtrees preserve explicit order, short circuit and per-match binding.");
        a.Trace.Clear(); a.Reenter = true;
        a.Hooks.TryAdvance(ProgramRuntimePhase.BeforeResponseHealth);
        Require(a.Trace.SequenceEqual(["first", "first", "second", "second"]), "Nested dispatch finishes before the parent's next candidate.");
        Require(!b.Hooks.TryAdvance(ProgramRuntimePhase.AfterResponseHealth) && b.Trace.SequenceEqual(["first", "second", "last"]),
            "An unhandled phase tries all candidates without borrowing another match's state.");
        var program = StandardContentRegistry.CreateWithClassicGenerals().GetSkill("ol:liehou").Program!;
        var plan = ProgramInstructionResolver.Default.Resolve(program, ProgramInstructionSourceKind.Activation, program.Activations[0].Id);
        var frame = new ProgramSkillFrame(1, 0, program.Id, "fixture", program.GameplayHash, 0, [], []);
        a.Trace.Clear();
        Require(a.Hooks.CanContinue(ProgramRuntimePhase.AfterOwnerDeath, frame) && !a.Hooks.CanContinue(ProgramRuntimePhase.AfterSkillLoss, frame) &&
            a.Trace.SequenceEqual(["death", "loss"]), "Owner-death permission cannot authorize a skill-loss continuation.");
        a.Trace.Clear(); a.ExpectedFrame = frame; a.ExpectedPlan = plan;
        a.Hooks.AssertFrame(ProgramFrameInvariantPhase.BeforeSelectionState, frame, plan);
        Require(a.Trace.SequenceEqual(["before", "plan"]), "Invariants receive the original frame and resolved plan in order.");
        a.Hooks.AssertFrame(ProgramFrameInvariantPhase.AfterSelectionState, frame, plan);
        Require(a.Trace.SequenceEqual(["before", "plan", "after"]), "Selection checks stay between separate invariant phases; static invariants remain supported.");
        a.Trace.Clear(); a.Failure = new InvalidOperationException("original invariant failure");
        try { a.Hooks.AssertFrame(ProgramFrameInvariantPhase.BeforeSelectionState, frame, plan); throw new Exception("Expected failure"); }
        catch (InvalidOperationException error) when (ReferenceEquals(error, a.Failure))
        { Require(a.Trace.SequenceEqual(["before"]), "The original exception stops later checks without reflection wrapping."); }
        Require(catalog.Hooks.Values.Concat(catalog.Invariants.Values).All(rows => rows is IList<ProgramRuntimeRegistration> list && list.IsReadOnly),
            "Shared metadata lists must be frozen.");
        _ = ProgramRuntimeHookCatalog.Discover(typeof(GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic));
    }

    public static void InvalidMetadataRejected()
    {
        Reject(typeof(DuplicateHook), "Duplicate runtime hook order");
        Reject(typeof(DuplicateInvariant), "Duplicate frame invariant order");
        Reject(typeof(InvalidPhase), "valid phase");
        Reject(typeof(ZeroOrder), "positive order");
        Reject(typeof(StaticHook), "invalid handler signature");
        Reject(typeof(OptionalPermission), "invalid handler signature");
        Reject(typeof(GenericHook), "invalid handler signature");
        Reject(typeof(WrongHookParameter), "invalid handler signature");
        Reject(typeof(WrongInvariantResult), "invalid handler signature");
        Reject(typeof(OptionalInvariant), "invalid handler signature");
        try { _ = ProgramRuntimeHookCatalog.Discover([]); }
        catch (InvalidOperationException e) when (e.Message.Contains("has no registrations", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Missing runtime phases must fail before dispatch.");
        static void Reject(Type type, string expected)
        {
            try { _ = ProgramRuntimeHookCatalog.Discover(Methods(typeof(Fixture)).Concat(Methods(type))); }
            catch (InvalidOperationException e) when (e.Message.Contains(expected, StringComparison.Ordinal)) { return; }
            throw new InvalidOperationException($"Did not reject {type.Name}: {expected}");
        }
    }

    private static MethodInfo[] Methods(Type type) => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class Fixture
    {
        internal List<string> Trace { get; } = [];
        internal bool Stop { get; set; }
        internal bool Reenter { get; set; }
        internal Exception? Failure { get; set; }
        internal ProgramSkillFrame? ExpectedFrame { get; set; }
        internal ProgramExecutionPlan? ExpectedPlan { get; set; }
        internal ProgramRuntimeHooks Hooks { get; }
        internal Fixture(ProgramRuntimeHookCatalog catalog) => Hooks = new(catalog, (m, t) => m.IsStatic ? m.CreateDelegate(t) : m.CreateDelegate(t, this));
        [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 10)]
        [ProgramRuntimeHook(ProgramRuntimePhase.BeforeResponseHealth, 10)]
        [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 10)]
        private bool First() { Trace.Add("first"); if (Reenter) { Reenter = false; Hooks.TryAdvance(ProgramRuntimePhase.BeforeResponseHealth); } return false; }
        [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 20)]
        [ProgramRuntimeHook(ProgramRuntimePhase.BeforeResponseHealth, 20)]
        [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 20)]
        private bool Second() { Trace.Add("second"); return Stop; }
        [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 30)]
        [ProgramRuntimeHook(ProgramRuntimePhase.BeforeResponseHealth, 30)]
        [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 30)]
        private bool Last() { Trace.Add("last"); return false; }
        [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 10)]
        private bool Death(ProgramSkillFrame frame) { Trace.Add("death"); return frame.OwnerSeat == 0; }
        [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 10)]
        private bool Loss(ProgramSkillFrame frame) { Trace.Add("loss"); return false; }
        [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 10)]
        private void Before(ProgramSkillFrame frame) { Require(ReferenceEquals(frame, ExpectedFrame), "Frame changed."); Trace.Add("before"); if (Failure is { } e) throw e; }
        [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 20)]
        private void Plan(ProgramSkillFrame frame, ProgramExecutionPlan plan) { Require(ReferenceEquals(plan, ExpectedPlan), "Resolved plan changed."); Trace.Add("plan"); }
        [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 10)]
        private static void Static(ProgramSkillFrame frame) => Require(frame.Id > 0, "Static argument lost.");
        [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 20)]
        private void After(ProgramSkillFrame frame) => Trace.Add("after");
    }
    private sealed class DuplicateHook { [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 10)] private bool Bad() => false; }
    private sealed class DuplicateInvariant { [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 10)] private void Bad(ProgramSkillFrame f) { } }
    private sealed class InvalidPhase { [ProgramRuntimeHook((ProgramRuntimePhase)999, 10)] private bool Bad() => false; }
    private sealed class ZeroOrder { [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 0)] private bool Bad() => false; }
    private sealed class StaticHook { [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 999)] private static bool Bad() => false; }
    private sealed class OptionalPermission { [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 999)] private bool Bad(ProgramSkillFrame? f = null) => false; }
    private sealed class GenericHook { [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 999)] private bool Bad<T>() => false; }
    private sealed class WrongHookParameter { [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 999)] private bool Bad(ProgramSkillFrame f) => false; }
    private sealed class WrongInvariantResult { [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 999)] private bool Bad(ProgramSkillFrame f) => false; }
    private sealed class OptionalInvariant { [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 999)] private void Bad(ProgramSkillFrame f, ProgramExecutionPlan? p = null) { } }
}
