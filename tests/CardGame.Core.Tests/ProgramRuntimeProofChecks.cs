using System.Reflection;
using CardGame.Core;

internal static class ProgramRuntimeProofChecks
{
    public static void OrderedTypedAdmissionAndDyingGroups()
    {
        var catalog = ProgramRuntimeProofCatalog.Discover(Methods(typeof(Fixture)).Reverse());
        var frame = new ProgramSkillFrame(1, 0, "fixture:proof", "proof", "hash", 0, [], []);
        var effect = new SkillProgramEffect(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1,
            new(SkillProgramConditionKind.Always, 0, []));
        var movement = new ProgramMovementContinuation(0, 0, null);
        var source = new ProgramParticipantReference(ProgramParticipantRef.Actor);
        var a = new Fixture(catalog, frame, effect, movement, source) { Match = true };
        var b = new Fixture(catalog, frame, effect, movement, source);
        Need(a.Bound.Count == 0 && b.Bound.Count == 0, "Unused proof phases must not bind handlers.");
        Need(a.Proofs.MatchesObserver(ProgramObserverProofPhase.PaidDamage, 17) &&
            a.Trace.SequenceEqual(["observer-first", "observer-second"]) && b.Trace.Count == 0,
            "Observer proofs retain exact window identity, order, short circuit and per-match binding.");
        Need(!a.Bound.Any(name => name.Contains("Nested", StringComparison.Ordinal) || name.Contains("Movement", StringComparison.Ordinal)),
            "An observer query must not initialize unrelated proof phases.");
        Need(!b.Proofs.MatchesObserver(ProgramObserverProofPhase.PaidDamage, 17) &&
            b.Trace.SequenceEqual(["observer-first", "observer-second", "observer-last"]), "Unmatched observers try the original whole list.");
        a.Trace.Clear();
        Need(!a.Proofs.MatchesObserver(ProgramObserverProofPhase.NestedDamage, 17) && a.Trace.SequenceEqual(["observer-other"]),
            "Paid-damage admission cannot authorize a different observer position.");
        Need(a.Proofs.MatchesObserver(ProgramObserverProofPhase.BeforeDamage, 18), "Static proofs retain their exact argument.");
        a.Trace.Clear();
        Need(a.Proofs.AllowsNestedDamage(ProgramNestedDamageProofPhase.BeforeFactionDebts, frame, 4, 3, source, DamageNature.Fire, true) &&
            a.Trace.SequenceEqual(["nested"]), "Nested damage retains its original frame, target, amount, source, nature and source-less flag.");
        a.Trace.Clear();
        Need(!a.Proofs.AllowsNestedDamage(ProgramNestedDamageProofPhase.AfterFactionDebts, frame, 4, 3, source, DamageNature.Fire, true) &&
            a.Trace.SequenceEqual(["nested-other"]), "Admission on one side of faction debts cannot bypass the other side.");
        a.Trace.Clear();
        Need(a.Proofs.MatchesMovement(ProgramMovementProofPhase.EffectPayments, frame, effect, movement) && a.Trace.SequenceEqual(["movement"]),
            "Movement admission receives the exact effect and paid continuation.");
        a.Trace.Clear(); a.ExpectedEffect = null;
        Need(a.Proofs.MatchesMovement(ProgramMovementProofPhase.EffectPayments, frame, null, movement), "A nullable paused effect remains nullable.");
        a.Trace.Clear();
        Need(!a.Proofs.MatchesMovement(ProgramMovementProofPhase.BoundEquipment, frame, effect, movement) && a.Trace.Count == 0,
            "A missing movement phase does not borrow admission from another phase.");
        Need(a.Proofs.IsDying(ProgramDyingProofPhase.Invariant) && a.Trace.SequenceEqual(["dying-first", "dying-later-group"]),
            "A matched OR skips later members of that group, while later |= groups must still run.");
        a.Trace.Clear();
        Need(a.Proofs.IsDying(ProgramDyingProofPhase.Runtime) && a.Trace.SequenceEqual(["dying-first"]),
            "The runtime dying list keeps its distinct short-circuit scope.");
        a.Trace.Clear(); a.Failure = new InvalidOperationException("original proof failure");
        try { a.Proofs.MatchesObserver(ProgramObserverProofPhase.PaidDamage, 17); throw new Exception("Expected proof failure."); }
        catch (InvalidOperationException error) when (ReferenceEquals(error, a.Failure))
        { Need(a.Trace.SequenceEqual(["observer-first"]), "The original failure stops dispatch without reflection wrapping."); }
        Need(catalog.Observers.Values.Concat(catalog.NestedDamage.Values).Concat(catalog.Movements.Values)
                .Concat(catalog.Dying.Values.SelectMany(groups => groups)).All(rows => rows is IList<ProgramRuntimeRegistration> list && list.IsReadOnly) &&
            catalog.Dying.Values.All(groups => groups is IList<IReadOnlyList<ProgramRuntimeRegistration>> list && list.IsReadOnly),
            "Shared proof lists and their eager dying groups remain immutable.");
        Reject(typeof(DuplicateOrder), "Duplicate runtime proof order");
        Reject(typeof(WrongSignature), "invalid handler signature");
        Reject(typeof(OptionalArgument), "invalid handler signature");
        Reject(typeof(WrongPhase), "valid phase");
        Reject(typeof(WrongGroup), "positive group");
        static void Reject(Type type, string expected)
        {
            try { _ = ProgramRuntimeProofCatalog.Discover(Methods(typeof(Fixture)).Concat(Methods(type))); }
            catch (InvalidOperationException error) when (error.Message.Contains(expected, StringComparison.Ordinal)) { return; }
            throw new InvalidOperationException($"Did not reject {type.Name}: {expected}");
        }
    }

    private static MethodInfo[] Methods(Type type) => type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture
    {
        internal List<string> Trace { get; } = [];
        internal List<string> Bound { get; } = [];
        internal bool Match { get; init; }
        internal Exception? Failure { get; set; }
        private readonly ProgramSkillFrame _frame;
        internal SkillProgramEffect? ExpectedEffect { get; set; }
        private readonly ProgramMovementContinuation _movement;
        private readonly ProgramParticipantReference _source;
        internal ProgramRuntimeProofs Proofs { get; }
        internal Fixture(ProgramRuntimeProofCatalog catalog, ProgramSkillFrame frame, SkillProgramEffect effect,
            ProgramMovementContinuation movement, ProgramParticipantReference source)
        {
            (_frame, ExpectedEffect, _movement, _source) = (frame, effect, movement, source);
            Proofs = new(catalog, row =>
            {
                Bound.Add(row.Method.Name);
                return row.Method.IsStatic ? row.Method.CreateDelegate(row.DelegateType) : row.Method.CreateDelegate(row.DelegateType, this);
            });
        }
        [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 10)]
        private bool ObserverFirst(long id) { Need(id == 17, "Window changed."); Trace.Add("observer-first"); if (Failure is { } error) throw error; return false; }
        [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 20)]
        private bool ObserverSecond(long id) { Trace.Add("observer-second"); return Match; }
        [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 30)]
        private bool ObserverLast(long id) { Trace.Add("observer-last"); return false; }
        [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 10)]
        private bool ObserverOther(long id) { Trace.Add("observer-other"); return false; }
        [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 10)]
        private static bool StaticObserver(long id) => id == 18;
        [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 10)]
        private bool Nested(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
        {
            Need(ReferenceEquals(f, _frame) && target == 4 && amount == 3 && ReferenceEquals(source, _source) && nature == DamageNature.Fire && sourceLess,
                "Nested damage arguments changed."); Trace.Add("nested"); return Match;
        }
        [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 10)]
        private bool NestedOther(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
        { Trace.Add("nested-other"); return false; }
        [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 10)]
        private bool Movement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation movement)
        {
            Need(ReferenceEquals(f, _frame) && ReferenceEquals(effect, ExpectedEffect) && ReferenceEquals(movement, _movement), "Movement arguments changed.");
            Trace.Add("movement"); return Match;
        }
        [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 10, group: 10)]
        [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 10)]
        private bool DyingFirst() { Trace.Add("dying-first"); return Match; }
        [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 20, group: 10)]
        [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 20)]
        private bool DyingSecond() { Trace.Add("dying-second"); return false; }
        [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 10, group: 20)]
        private bool DyingLaterGroup() { Trace.Add("dying-later-group"); return false; }
    }
    private sealed class DuplicateOrder { [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 10)] private bool Bad(long id) => false; }
    private sealed class WrongSignature { [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 999)] private int Bad(long id) => 0; }
    private sealed class OptionalArgument { [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 999)] private bool Bad(long id = 0) => false; }
    private sealed class WrongPhase { [ProgramMovementProof((ProgramMovementProofPhase)999, 10)] private bool Bad(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation m) => false; }
    private sealed class WrongGroup { [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 999, group: 0)] private bool Bad() => false; }
}
