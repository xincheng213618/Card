using System.Reflection;
using CardGame.Core;

internal static class ProgramDamageHookChecks
{
    public static void ActualDamagePhasesKeepOrderAndShortCircuit()
    {
        var supplied = Methods(typeof(Fixture)).Reverse().ToList();
        var catalog = ProgramDamageHookCatalog.Discover(supplied, DelegateType);
        supplied.Clear();
        Need(catalog.Phases[ProgramDamagePhase.AdditiveAmount].Select(row => row.Order).SequenceEqual([10, 20]) &&
            catalog.Phases[ProgramDamagePhase.AdditiveAmount] is IList<ProgramDamageRegistration> { IsReadOnly: true },
            "Damage metadata freezes explicitly ordered registrations.");
        var first = new Fixture(catalog);
        var second = new Fixture(catalog);
        var attack = new Attack();

        Need(first.Hooks.AddAmounts(attack, int.MaxValue) == int.MaxValue &&
            first.Trace.SequenceEqual(["amount:first", "amount:last"]) && attack.Issued == 1 && second.Trace.Count == 0,
            "Each actual-damage provider executes once in order; unchecked additions retain the old left fold and frame issue side effect.");
        first.Trace.Clear();
        try { _ = first.Hooks.AddAmounts(attack, new[] { int.MaxValue, 1 }.Sum()); throw new Exception("Expected checked fact sum."); }
        catch (OverflowException) { Need(first.Trace.Count == 0 && attack.Issued == 1, "Checked fact Sum fails before native providers can issue receipts."); }
        Need(second.Hooks.AddAmounts(new Attack(), 4) == 4 && second.Trace.SequenceEqual(["amount:first", "amount:last"]),
            "Shared metadata binds each engine independently.");
        Need(first.Bound.Count(name => name == "FirstAmount") == 1, "Repeated damage attempts reuse match-bound delegates.");

        first.Trace.Clear();
        var modifiers = first.Hooks.AppendModifiers(attack, new[] { 3 });
        Need(first.Trace.SequenceEqual(["source:first", "source:last"]), "Modifier sources are called eagerly in original order.");
        Need(modifiers.SequenceEqual([3, 1, 2]) && first.Trace.SequenceEqual(["source:first", "source:last", "enumerate:first", "enumerate:last"]),
            "Source sequences retain lazy enumeration after every provider has been called.");

        first.Trace.Clear(); first.Interception = ProgramDamageInterception.Prevented;
        Need(first.Hooks.Intercept(ProgramDamagePhase.BeforeProgramWindow, attack, 2, DamageNature.Fire) == ProgramDamageInterception.Prevented &&
            first.Trace.SequenceEqual(["intercept:first:2:Fire", "intercept:stop"]) && attack.Issued == 2,
            "A prevention preserves earlier side effects and stops all later handlers.");
        first.Trace.Clear(); first.Interception = ProgramDamageInterception.Paused;
        Need(first.Hooks.Intercept(ProgramDamagePhase.BeforeProgramWindow, attack, 3, DamageNature.Thunder) == ProgramDamageInterception.Paused &&
            first.Trace.SequenceEqual(["intercept:first:3:Thunder", "intercept:stop"]),
            "A suspended continuation is distinct from prevented damage and still short-circuits.");

        first.Trace.Clear();
        Need(first.Hooks.TryComplete(ProgramDamagePhase.IssuedAttackCompletion, attack, out var paused) && !paused &&
            first.Trace.SequenceEqual(["finish:decline", "finish:handled"]),
            "A handled synchronous completion remains distinct from a pause; an ignored out value cannot stop the chain.");
        first.Trace.Clear();
        Need(first.Hooks.TryComplete(ProgramDamagePhase.AttackCompletionPrelude, attack, out paused) && paused &&
            first.Trace.SequenceEqual(["finish:pause"]), "Prelude completion retains its separate position before shared frame-kind checks.");
        first.Trace.Clear();
        Need(first.Hooks.TryBeginApplied(attack, 42) && first.Trace.SequenceEqual(["applied:first:42", "applied:stop"]),
            "Applied-damage continuations stop before the shared dying window when a module takes ownership.");
        first.Trace.Clear();
        first.Hooks.AdvanceChain(ProgramDamagePhase.BeforeChainAdvance, attack);
        first.Hooks.AdvanceChain(ProgramDamagePhase.AfterChainAdvance, attack);
        Need(first.Trace.SequenceEqual(["chain:prepare:first", "chain:prepare:last", "chain:recipient"]),
            "Preparation and recipient application are distinct phases preserving each ordered side effect.");

        first.Trace.Clear(); first.Failure = new InvalidOperationException("original damage failure");
        try { _ = first.Hooks.AddAmounts(attack, 1); throw new Exception("Expected original failure."); }
        catch (InvalidOperationException error) when (ReferenceEquals(error, first.Failure))
        { Need(first.Trace.SequenceEqual(["amount:first"]), "Direct delegates retain exception identity and stop later providers."); }

        var empty = new ProgramDamageHooks(ProgramDamageHookCatalog.Discover([], DelegateType), _ => throw new Exception("Unexpected empty binding."));
        Need(empty.AddAmounts(attack, 7) == 7 && !empty.TryComplete(ProgramDamagePhase.IssuedAttackCompletion, attack, out paused) && !paused &&
            empty.Intercept(ProgramDamagePhase.BeforeProgramWindow, attack, 1, DamageNature.Normal) == ProgramDamageInterception.Continue,
            "Empty stages preserve the ordinary engine path.");
        Reject(typeof(DuplicateOrder), "Duplicate damage hook order");
        Reject(typeof(WrongSignature), "invalid handler signature");
        Reject(typeof(OptionalArgument), "invalid handler signature");
        Reject(typeof(ZeroOrder), "positive order");

        // Exercise actual engine metadata too: fixtures cannot conceal an invalid
        // module signature or a duplicate order elsewhere in the content tree.
        var field = typeof(GameEngine).GetField("DamageHookMetadata", BindingFlags.NonPublic | BindingFlags.Static)!;
        var actual = ((Lazy<ProgramDamageHookCatalog>)field.GetValue(null)!).Value;
        Need(actual.Phases[ProgramDamagePhase.AdditiveAmount].Count > 0 && actual.Phases[ProgramDamagePhase.BeforeChainAdvance].Count > 0,
            "The real engine discovers its module-owned actual-damage contracts.");

        void Reject(Type type, string expected)
        {
            try { _ = ProgramDamageHookCatalog.Discover(Methods(typeof(Fixture)).Concat(Methods(type)), DelegateType); }
            catch (InvalidOperationException error) when (error.Message.Contains(expected, StringComparison.Ordinal)) { return; }
            throw new InvalidOperationException($"Did not reject {type.Name}: {expected}");
        }
    }

    private static Type DelegateType(ProgramDamagePhase phase) => phase switch
    {
        ProgramDamagePhase.FactModifiers => typeof(Func<Attack, IEnumerable<int>>),
        ProgramDamagePhase.AdditiveAmount => typeof(Func<Attack, int>),
        ProgramDamagePhase.BeforeProgramWindow or ProgramDamagePhase.AfterEquipmentChoices => typeof(Func<Attack, int, DamageNature, ProgramDamageInterception>),
        ProgramDamagePhase.AppliedBeforeDying => typeof(Func<Attack, long, bool>),
        ProgramDamagePhase.AttackCompletionPrelude or ProgramDamagePhase.IssuedAttackCompletion => typeof(ProgramDamageCompletion<Attack>),
        ProgramDamagePhase.BeforeChainAdvance or ProgramDamagePhase.AfterChainAdvance => typeof(Action<Attack>),
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };
    private static MethodInfo[] Methods(Type type) => type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Attack { internal int Issued; }
    private sealed class Fixture
    {
        internal List<string> Trace { get; } = [];
        internal List<string> Bound { get; } = [];
        internal ProgramDamageInterception Interception { get; set; }
        internal Exception? Failure { get; set; }
        internal ProgramDamageHooks Hooks { get; }
        internal Fixture(ProgramDamageHookCatalog catalog) => Hooks = new(catalog, row =>
        { Bound.Add(row.Method.Name); return row.Method.CreateDelegate(row.DelegateType, this); });

        [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 10)]
        private int FirstAmount(Attack attack)
        { Trace.Add("amount:first"); if (Failure is { } error) throw error; attack.Issued++; return 1; }
        [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 20)]
        private int LastAmount(Attack attack) { Trace.Add("amount:last"); return -1; }
        [ProgramDamageHook(ProgramDamagePhase.FactModifiers, 10)]
        private IEnumerable<int> FirstSource(Attack attack) { Trace.Add("source:first"); return Enumerate("first", 1); }
        [ProgramDamageHook(ProgramDamagePhase.FactModifiers, 20)]
        private IEnumerable<int> LastSource(Attack attack) { Trace.Add("source:last"); return Enumerate("last", 2); }
        private IEnumerable<int> Enumerate(string name, int amount) { Trace.Add($"enumerate:{name}"); yield return amount; }
        [ProgramDamageHook(ProgramDamagePhase.BeforeProgramWindow, 10)]
        private ProgramDamageInterception FirstInterception(Attack attack, int amount, DamageNature nature)
        { Trace.Add($"intercept:first:{amount}:{nature}"); attack.Issued++; return ProgramDamageInterception.Continue; }
        [ProgramDamageHook(ProgramDamagePhase.BeforeProgramWindow, 20)]
        private ProgramDamageInterception StopInterception(Attack attack, int amount, DamageNature nature)
        { Trace.Add("intercept:stop"); return Interception; }
        [ProgramDamageHook(ProgramDamagePhase.BeforeProgramWindow, 30)]
        private ProgramDamageInterception UnreachedInterception(Attack attack, int amount, DamageNature nature)
        { throw new InvalidOperationException("Later prevention must not run."); }
        [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 10)]
        private bool DeclineCompletion(Attack attack, out bool paused) { Trace.Add("finish:decline"); paused = true; return false; }
        [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 20)]
        private bool HandleCompletion(Attack attack, out bool paused) { Trace.Add("finish:handled"); paused = false; return true; }
        [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 30)]
        private bool UnreachedCompletion(Attack attack, out bool paused) { throw new InvalidOperationException("Later completion must not run."); }
        [ProgramDamageHook(ProgramDamagePhase.AttackCompletionPrelude, 10)]
        private bool PauseCompletion(Attack attack, out bool paused) { Trace.Add("finish:pause"); paused = true; return true; }
        [ProgramDamageHook(ProgramDamagePhase.AppliedBeforeDying, 10)]
        private bool FirstApplied(Attack attack, long frame) { Trace.Add($"applied:first:{frame}"); return false; }
        [ProgramDamageHook(ProgramDamagePhase.AppliedBeforeDying, 20)]
        private bool StopApplied(Attack attack, long frame) { Trace.Add("applied:stop"); return true; }
        [ProgramDamageHook(ProgramDamagePhase.AppliedBeforeDying, 30)]
        private bool UnreachedApplied(Attack attack, long frame) { throw new InvalidOperationException("Later applied continuation must not run."); }
        [ProgramDamageHook(ProgramDamagePhase.BeforeChainAdvance, 10)]
        private void FirstPrepare(Attack attack) { Trace.Add("chain:prepare:first"); }
        [ProgramDamageHook(ProgramDamagePhase.BeforeChainAdvance, 20)]
        private void LastPrepare(Attack attack) { Trace.Add("chain:prepare:last"); }
        [ProgramDamageHook(ProgramDamagePhase.AfterChainAdvance, 10)]
        private void ApplyRecipient(Attack attack) { Trace.Add("chain:recipient"); }
    }
    private sealed class DuplicateOrder
    { [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 10)] private int Bad(Attack attack) => 0; }
    private sealed class WrongSignature
    { [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 999)] private bool Bad(Attack attack) => false; }
    private sealed class OptionalArgument
    { [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 999)] private int Bad(Attack attack, int extra = 0) => 0; }
    private sealed class ZeroOrder
    { [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 0)] private int Bad(Attack attack) => 0; }
}
