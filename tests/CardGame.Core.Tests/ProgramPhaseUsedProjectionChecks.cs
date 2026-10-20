using System.Reflection;
using CardGame.Core;

internal static class ProgramPhaseUsedProjectionChecks
{
    public static void OrderedFoldAndMatchIsolation()
    {
        var supplied = Methods(typeof(Fixture)).Reverse().ToList();
        var catalog = ProgramPhaseUsedProjectionCatalog.Discover(supplied);
        supplied.Clear();
        Need(catalog.Registrations.Select(row => row.Order).SequenceEqual([10, 20, 30]) &&
            catalog.Registrations is IList<ProgramPhaseUsedProjectionRegistration> { IsReadOnly: true },
            "Projection metadata retains its explicit order in a frozen copy.");
        try { ((IList<ProgramPhaseUsedProjectionRegistration>)catalog.Registrations).Clear(); throw new Exception("Expected frozen metadata."); }
        catch (NotSupportedException) { }

        var a = new Fixture(catalog) { OverrideWithZero = true, Offset = 4 };
        var b = new Fixture(catalog) { Offset = 9 };
        Need(a.Projections.Apply(2, "fixture:phase", 5) == 4 &&
            a.Trace.SequenceEqual(["first:5", "zero:7", "last:0"]) && b.Trace.Count == 0,
            "Every projection receives the previous result; a zero override still reaches the remaining projections.");
        Need(b.Projections.Apply(2, "fixture:phase", 5) == 16 &&
            b.Trace.SequenceEqual(["first:5", "zero:7", "last:7"]),
            "Shared metadata binds projections to each match independently.");
        a.Trace.Clear();
        Need(a.Projections.Apply(3, "fixture:unrelated", 8) == 8 &&
            a.Trace.SequenceEqual(["first:8", "zero:8", "last:8"]),
            "An unrelated skill retains its ordinary usage while every projection receives the original seat and skill.");
        Need(a.Bound.Count == 3 && b.Bound.Count == 3,
            "Repeated projections reuse each match's bound delegates.");

        a.Trace.Clear(); a.Failure = new InvalidOperationException("original projection failure");
        try { a.Projections.Apply(2, "fixture:phase", 5); throw new Exception("Expected projection failure."); }
        catch (InvalidOperationException error) when (ReferenceEquals(error, a.Failure))
        { Need(a.Trace.SequenceEqual(["first:5", "zero:7"]), "The original exception stops later projections without reflection wrapping."); }

        var empty = new ProgramPhaseUsedProjections(ProgramPhaseUsedProjectionCatalog.Discover([]),
            _ => throw new InvalidOperationException("An empty projection catalog must not bind a handler."));
        Need(empty.Apply(2, "fixture:phase", 11) == 11, "An empty catalog preserves the supplied usage.");
        var staticCatalog = ProgramPhaseUsedProjectionCatalog.Discover(Methods(typeof(StaticProjection)));
        var staticProjections = new ProgramPhaseUsedProjections(staticCatalog,
            method => method.CreateDelegate<Func<int, string, int, int>>());
        Need(staticProjections.Apply(2, "fixture:phase", 5) == 7, "Stateless projections retain the same typed fold contract.");

        Reject(typeof(DuplicateOrder), "Duplicate phase-used projection order");
        Reject(typeof(ZeroOrder), "positive order");
        Reject(typeof(NegativeOrder), "positive order");
        Reject(typeof(WrongResult), "invalid handler signature");
        Reject(typeof(WrongParameter), "invalid handler signature");
        Reject(typeof(OptionalArgument), "invalid handler signature");
        Reject(typeof(GenericProjection), "invalid handler signature");
        Reject(typeof(RefArgument), "invalid handler signature");

        static void Reject(Type type, string expected)
        {
            try { _ = ProgramPhaseUsedProjectionCatalog.Discover(Methods(typeof(Fixture)).Concat(Methods(type))); }
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
        internal bool OverrideWithZero { get; init; }
        internal int Offset { get; init; }
        internal Exception? Failure { get; set; }
        internal ProgramPhaseUsedProjections Projections { get; }
        internal Fixture(ProgramPhaseUsedProjectionCatalog catalog) => Projections = new(catalog, method =>
        {
            Bound.Add(method.Name);
            return method.CreateDelegate<Func<int, string, int, int>>(this);
        });

        [ProgramPhaseUsedProjection(10)]
        private int First(int owner, string skill, int used)
        { Trace.Add($"first:{used}"); return IsFixture(owner, skill) ? used + 2 : used; }
        [ProgramPhaseUsedProjection(20)]
        private int Zero(int owner, string skill, int used)
        {
            Trace.Add($"zero:{used}");
            if (Failure is { } error) throw error;
            return IsFixture(owner, skill) && OverrideWithZero ? 0 : used;
        }
        [ProgramPhaseUsedProjection(30)]
        private int Last(int owner, string skill, int used)
        { Trace.Add($"last:{used}"); return IsFixture(owner, skill) ? used + Offset : used; }
        private static bool IsFixture(int owner, string skill)
        {
            Need(owner == 2 && skill == "fixture:phase" || owner == 3 && skill == "fixture:unrelated", "Projection arguments changed.");
            return skill == "fixture:phase";
        }
    }

    private sealed class StaticProjection
    { [ProgramPhaseUsedProjection(10)] private static int Project(int owner, string skill, int used) => skill == "fixture:phase" ? used + owner : used; }
    private sealed class DuplicateOrder
    { [ProgramPhaseUsedProjection(10)] private int Bad(int owner, string skill, int used) => used; }
    private sealed class ZeroOrder
    { [ProgramPhaseUsedProjection(0)] private int Bad(int owner, string skill, int used) => used; }
    private sealed class NegativeOrder
    { [ProgramPhaseUsedProjection(-1)] private int Bad(int owner, string skill, int used) => used; }
    private sealed class WrongResult
    { [ProgramPhaseUsedProjection(999)] private bool Bad(int owner, string skill, int used) => false; }
    private sealed class WrongParameter
    { [ProgramPhaseUsedProjection(999)] private int Bad(int owner, long skill, int used) => used; }
    private sealed class OptionalArgument
    { [ProgramPhaseUsedProjection(999)] private int Bad(int owner, string skill, int used = 0) => used; }
    private sealed class GenericProjection
    { [ProgramPhaseUsedProjection(999)] private int Bad<T>(int owner, string skill, int used) => used; }
    private sealed class RefArgument
    { [ProgramPhaseUsedProjection(999)] private int Bad(int owner, string skill, ref int used) => used; }
}
