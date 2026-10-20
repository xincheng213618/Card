using System.Reflection;
using CardGame.Core;

internal static class ProgramTargetWindowProtocolChecks
{
    public static void OrderedWindowSubscriptionsAndExactTargetIdentity()
    {
        var supplied = Methods(typeof(Fixture)).Reverse().ToList();
        var catalog = ProgramTargetWindowCatalog.Discover(supplied);
        supplied.Clear();
        Need(catalog.Stages is IDictionary<ProgramTargetWindowStage, IReadOnlyList<ProgramTargetWindowRegistration>> { IsReadOnly: true } &&
            catalog.Stages.Values.All(rows => rows is IList<ProgramTargetWindowRegistration> { IsReadOnly: true } &&
                rows.Select(row => row.Order).SequenceEqual([10, 20])),
            "Window metadata freezes discovery order into separate native stages.");
        try { ((IList<ProgramTargetWindowRegistration>)catalog.Stages[ProgramTargetWindowStage.FinalizedBeforeActivity]).Clear(); throw new Exception("Expected frozen metadata."); }
        catch (NotSupportedException) { }

        var a = new Fixture(catalog); var b = new Fixture(catalog);
        var cardStages = new[] { ProgramTargetWindowStage.FinalizedBeforeActivity, ProgramTargetWindowStage.FinalizedAfterCategory,
            ProgramTargetWindowStage.FinalizedInstantTrickRequirement, ProgramTargetWindowStage.ImplicitSelfDrawTwo,
            ProgramTargetWindowStage.NormalizeImplicitDrawTwo };
        foreach (var stage in cardStages)
        foreach (var first in new[] { false, true })
        foreach (var last in new[] { false, true })
        {
            a.Trace.Clear(); a.First = first; a.Last = last;
            var query = new ProgramTargetCardWindowQuery(CardKind.DrawTwo,
                stage is ProgramTargetWindowStage.FinalizedBeforeActivity or ProgramTargetWindowStage.ImplicitSelfDrawTwo or
                    ProgramTargetWindowStage.NormalizeImplicitDrawTwo ? null : SkillProgramCardCategory.Trick);
            a.ExpectedQuery = query;
            Need(a.Protocols.HasCardWindow(stage, query) == (first || last) &&
                a.Trace.SequenceEqual(first ? ["card:first"] : ["card:first", "card:last"]),
                "Each live card query retains short circuit order and does not invoke a different native stage.");
        }
        Need(b.Trace.Count == 0 && a.Bindings == 6 && b.Bindings == 6,
            "Callbacks registered at several stages bind once to each match and never retain another match's state.");
        a.First = false; a.Last = true; a.Trace.Clear();
        Need(a.Protocols.AllowsRedirectSource(a.Source) && a.Trace.SequenceEqual(["source:first", "source:last"]),
            "A redirect source forwards the exact owning skill instance in its original position.");
        a.First = true; a.Last = false; a.Trace.Clear();
        Need(a.Protocols.AcceptsReplacementFact(a.Use, a.Fact, a.Before) && a.Trace.SequenceEqual(["fact:first"]),
            "An accepted producer proof stops before later proofs while preserving actual-use, previous-target and source identities.");
        a.First = false; a.Last = false; a.Trace.Clear();
        Need(!a.Protocols.AcceptsReplacementFact(a.Use, a.Fact, a.Before) && a.Trace.SequenceEqual(["fact:first", "fact:last"]),
            "A declined producer proof leaves the native replacement validator eligible without changing any target set.");

        a.Trace.Clear(); a.Failure = new InvalidOperationException("original window failure");
        try { a.Protocols.HasCardWindow(ProgramTargetWindowStage.FinalizedBeforeActivity, a.ExpectedQuery); throw new Exception("Expected original failure."); }
        catch (InvalidOperationException error) when (ReferenceEquals(error, a.Failure))
        { Need(a.Trace.SequenceEqual(["card:first"]), "Failures retain their original identity and stop later subscriptions."); }
        Need(!b.Protocols.HasCardWindow(ProgramTargetWindowStage.FinalizedBeforeActivity, b.ExpectedQuery),
            "Another match remains independent after a query failure.");

        var empty = new ProgramTargetWindowProtocols(ProgramTargetWindowCatalog.Discover([]), (_, _) => throw new Exception("Unexpected empty binding."));
        Need(cardStages.All(stage => !empty.HasCardWindow(stage, a.ExpectedQuery)) && !empty.AllowsRedirectSource(a.Source) &&
            !empty.AcceptsReplacementFact(a.Use, a.Fact, a.Before), "Empty content adds no optional target window or special producer proof.");
        var stateless = new ProgramTargetWindowProtocols(ProgramTargetWindowCatalog.Discover(Methods(typeof(StaticQuery))),
            (method, type) => method.CreateDelegate(type));
        Need(stateless.HasCardWindow(ProgramTargetWindowStage.ImplicitSelfDrawTwo, new(CardKind.DrawTwo)),
            "Stateless subscription callbacks use the same typed contract.");

        Reject(typeof(Duplicate), "Duplicate target-window protocol order");
        Reject(typeof(InvalidStage), "valid stage");
        Reject(typeof(InvalidOrder), "positive order");
        Reject(typeof(InvalidSignature), "invalid handler signature");
        Reject(typeof(OptionalParameter), "invalid handler signature");
        var production = ProgramTargetWindowCatalog.Discover(typeof(GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic));
        Need(production.Stages.Values.All(rows => rows.Count > 0), "Every production target stage has its module-owned subscription declarations.");

        static void Reject(Type type, string expected)
        {
            try { _ = ProgramTargetWindowCatalog.Discover(Methods(typeof(Fixture)).Concat(Methods(type))); }
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
        internal bool First { get; set; }
        internal bool Last { get; set; }
        internal int Bindings { get; private set; }
        internal Exception? Failure { get; set; }
        internal ProgramTargetCardWindowQuery ExpectedQuery { get; set; } = new(CardKind.DrawTwo);
        internal CardConversionSource Source { get; } = new("fixture:window", "redirect", 2, "fixture:instance");
        internal IReadOnlyList<int> Before { get; } = Array.AsReadOnly(new[] { 3, 1 });
        internal CardUseFrame Use { get; }
        internal SoleTargetReplacedEvent Fact { get; }
        internal ProgramTargetWindowProtocols Protocols { get; }
        internal Fixture(ProgramTargetWindowCatalog catalog)
        {
            Use = new(31, 0, 7, CardKind.BorrowedSword, Array.AsReadOnly(new[] { 2, 1 }));
            Fact = new(31, 19, Source, 3, 2, Before, Use.TargetSeats);
            Protocols = new(catalog, (method, type) => { Bindings++; return method.CreateDelegate(type, this); });
        }

        [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedBeforeActivity, 10)]
        [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedAfterCategory, 10)]
        [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedInstantTrickRequirement, 10)]
        [ProgramTargetWindow(ProgramTargetWindowStage.ImplicitSelfDrawTwo, 10)]
        [ProgramTargetWindow(ProgramTargetWindowStage.NormalizeImplicitDrawTwo, 10)]
        private bool FirstCard(ProgramTargetCardWindowQuery query)
        { Need(query == ExpectedQuery, "Card kind/category boundary changed."); Trace.Add("card:first"); if (Failure is { } error) throw error; return First; }
        [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedBeforeActivity, 20)]
        [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedAfterCategory, 20)]
        [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedInstantTrickRequirement, 20)]
        [ProgramTargetWindow(ProgramTargetWindowStage.ImplicitSelfDrawTwo, 20)]
        [ProgramTargetWindow(ProgramTargetWindowStage.NormalizeImplicitDrawTwo, 20)]
        private bool LastCard(ProgramTargetCardWindowQuery query)
        { Need(query == ExpectedQuery, "Card classification changed between subscribers."); Trace.Add("card:last"); return Last; }
        [ProgramTargetWindow(ProgramTargetWindowStage.SoleTargetRedirectSource, 10)]
        private bool FirstSource(CardConversionSource source)
        { Need(ReferenceEquals(source, Source), "Source instance identity changed."); Trace.Add("source:first"); return First; }
        [ProgramTargetWindow(ProgramTargetWindowStage.SoleTargetRedirectSource, 20)]
        private bool LastSource(CardConversionSource source)
        { Need(ReferenceEquals(source, Source), "Source instance identity changed."); Trace.Add("source:last"); return Last; }
        [ProgramTargetWindow(ProgramTargetWindowStage.SoleTargetReplacementFact, 10)]
        private bool FirstFact(CardUseFrame use, SoleTargetReplacedEvent fact, IReadOnlyList<int> before)
        { ExactFact(use, fact, before); Trace.Add("fact:first"); return First; }
        [ProgramTargetWindow(ProgramTargetWindowStage.SoleTargetReplacementFact, 20)]
        private bool LastFact(CardUseFrame use, SoleTargetReplacedEvent fact, IReadOnlyList<int> before)
        { ExactFact(use, fact, before); Trace.Add("fact:last"); return Last; }
        private void ExactFact(CardUseFrame use, SoleTargetReplacedEvent fact, IReadOnlyList<int> before) =>
            Need(ReferenceEquals(use, Use) && ReferenceEquals(fact, Fact) && ReferenceEquals(before, Before) &&
                use.TargetSeats.SequenceEqual([2, 1]) && before.SequenceEqual([3, 1]),
                "Actual targets and the ordered original main/secondary target pair must remain distinct and untouched.");
    }

    private sealed class StaticQuery
    { [ProgramTargetWindow(ProgramTargetWindowStage.ImplicitSelfDrawTwo, 10)] private static bool HasWindow(ProgramTargetCardWindowQuery query) => query.Kind == CardKind.DrawTwo; }
    private sealed class Duplicate
    { [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedBeforeActivity, 10)] private bool Invalid(ProgramTargetCardWindowQuery query) => false; }
    private sealed class InvalidStage
    { [ProgramTargetWindow((ProgramTargetWindowStage)999, 1)] private bool Invalid(ProgramTargetCardWindowQuery query) => false; }
    private sealed class InvalidOrder
    { [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedBeforeActivity, 0)] private bool Invalid(ProgramTargetCardWindowQuery query) => false; }
    private sealed class InvalidSignature
    { [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedBeforeActivity, 30)] private int Invalid(ProgramTargetCardWindowQuery query) => 0; }
    private sealed class OptionalParameter
    { [ProgramTargetWindow(ProgramTargetWindowStage.SoleTargetRedirectSource, 30)] private bool Invalid(CardConversionSource? source = null) => false; }
}
