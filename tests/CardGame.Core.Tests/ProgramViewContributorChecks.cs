using System.Reflection;
using CardGame.Core;

internal static class ProgramViewContributorChecks
{
    public static void OrderedVisibilityContributionsAndProcessingPrivacy()
    {
        var catalog = ProgramViewContributionCatalog.Discover(Methods(typeof(Fixture)).Reverse());
        var a = new Fixture(catalog);
        var b = new Fixture(catalog);
        Need(a.Bound.Count == 0 && b.Bound.Count == 0, "Unused view channels must not bind match callbacks.");
        var cards = a.Views.Cards(ProgramViewContribution.PublicCardsBeforeResolution);
        Need(a.Trace.SequenceEqual(["public-first", "public-second"]) && b.Trace.Count == 0,
            "Source invocation order is explicit and match-bound, independently of discovery order.");
        Need(cards.Select(c => c.Id).SequenceEqual([1, 1]) &&
            a.Trace.SequenceEqual(["public-first", "public-second", "public-enumerate"]),
            "Sources retain lazy enumeration and duplicates until the existing projection de-duplicates them.");
        Need(!a.Bound.Contains("Private") && !a.Bound.Contains("Covered"), "Public sources must not bind unrelated channels.");
        a.Trace.Clear();
        Need(a.Views.Cards(ProgramViewContribution.PrivateCards, 7).Single().Id == 107 && a.Trace.SequenceEqual(["private:7"]) &&
            b.Views.Cards(ProgramViewContribution.PrivateCards, 3).Single().Id == 103,
            "Private sources receive the actual viewer and are bound independently for each match.");
        Need(!a.Views.Cards(ProgramViewContribution.ProgramPublicCards).Any() &&
            a.Views.Cards(ProgramViewContribution.PublicCardsAfterResolution).Single().Id == 9,
            "Program and before/after resolution sources remain separate; static sources also bind correctly.");

        a.Trace.Clear();
        Need(a.Views.FilterProcessing(ProgramViewContribution.InvariantProcessing, a.Initial).Select(c => c.Id).SequenceEqual([3, 4, 5]) &&
            a.Trace.SequenceEqual(["covered", "invariant-held"]),
            "Invariant filters retain the original input, intermediate lists and transform order.");
        Need(a.Views.ProcessingCount(a.Initial, false) == 1 && a.Views.ProcessingCount(a.Initial, true) == 3,
            "Covered/held cards never count as public Processing; revealAll bypasses only concealed contest cards.");
        Need(a.Bound.Count(name => name == "Covered") == 1,
            "One callback shared by invariant and projection channels binds only once per engine.");
        a.Trace.Clear();
        Need(!a.Views.IsPublicCardVisible(3, false) && a.Trace.SequenceEqual(["concealed-first:3"]),
            "A concealed-card match preserves short circuit order.");
        a.Trace.Clear();
        Need(a.Views.IsPublicCardVisible(3, true) && a.Trace.Count == 0,
            "Diagnostic visibility skips concealed-card predicates at their original boundary.");

        a.Trace.Clear();
        var piles = a.Views.NamedAmbushPiles(null, 2, 7, false)!;
        Need(piles.Select(p => p.SkillId).SequenceEqual(["first", "second"]) && piles.All(p => p.Entries is null) &&
            a.Trace.SequenceEqual(["named-first", "named-second"]),
            "Named pile transforms preserve nullable input, holder/viewer privacy and append order.");
        a.ExpectedReveal = true;
        Need(a.Views.NamedAmbushPiles(null, 2, 7, true)!.All(p => p.Entries?.Single().Card.Id == 2) &&
            a.Views.PrivateWhirlPiles(null, 2, 7, true)!.Single().Cards!.Single().Id == 2,
            "Pile contributors receive revealAll without a central change to their visibility policy.");
        a.ExpectedReveal = false;
        Need(a.Views.PrivateWhirlPiles(null, 2, 7, false)!.Single().Cards is null,
            "Private pile counts remain visible while their faces remain private.");

        a.Trace.Clear(); a.Failure = new InvalidOperationException("original projection failure");
        try { _ = a.Views.Cards(ProgramViewContribution.PublicCardsBeforeResolution); throw new Exception("Expected original failure."); }
        catch (InvalidOperationException error) when (ReferenceEquals(error, a.Failure))
        { Need(a.Trace.SequenceEqual(["public-first"]), "Failure propagation stops later sources without reflection wrapping."); }
        Need(catalog.Phases is IDictionary<ProgramViewContribution, IReadOnlyList<ProgramViewRegistration>> dictionary && dictionary.IsReadOnly &&
            catalog.Phases.Values.All(rows => rows is IList<ProgramViewRegistration> list && list.IsReadOnly),
            "Shared contribution metadata and each ordered channel remain immutable.");
        Reject(typeof(DuplicateOrder), "Duplicate view contributor order");
        Reject(typeof(DuplicateRegistration), "Duplicate view contributor '");
        Reject(typeof(WrongSignature), "invalid handler signature");
        Reject(typeof(OptionalArgument), "invalid handler signature");
        Reject(typeof(WrongContribution), "valid contribution");
        Reject(typeof(ZeroOrder), "positive order");

        static void Reject(Type type, string expected)
        {
            try { _ = ProgramViewContributionCatalog.Discover(Methods(typeof(Fixture)).Concat(Methods(type))); }
            catch (InvalidOperationException error) when (error.Message.Contains(expected, StringComparison.Ordinal)) { return; }
            throw new InvalidOperationException($"Did not reject {type.Name}: {expected}");
        }
    }

    private static MethodInfo[] Methods(Type type) => type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
    private static CardSnapshot Face(int id) => new(id, CardKind.Slash, Suit.Spade, 1, "fixture", "A");
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture
    {
        internal List<string> Trace { get; } = [];
        internal List<string> Bound { get; } = [];
        internal bool ExpectedReveal { get; set; }
        internal Exception? Failure { get; set; }
        internal IReadOnlyList<Card> Initial { get; } = Enumerable.Range(1, 5).Select(id => new Card(id, CardKind.Slash, Suit.Spade, 1)).ToArray();
        internal ProgramViewContributors Views { get; }
        internal Fixture(ProgramViewContributionCatalog catalog) => Views = new(catalog, row =>
        {
            Bound.Add(row.Method.Name);
            return row.Method.IsStatic ? row.Method.CreateDelegate(row.DelegateType) : row.Method.CreateDelegate(row.DelegateType, this);
        });
        [ProgramViewContributor(ProgramViewContribution.PublicCardsBeforeResolution, 10)]
        private IReadOnlyList<CardSnapshot> First()
        { Trace.Add("public-first"); if (Failure is { } error) throw error; return [Face(1)]; }
        [ProgramViewContributor(ProgramViewContribution.PublicCardsBeforeResolution, 20)]
        private IEnumerable<CardSnapshot> Second()
        {
            Trace.Add("public-second"); return Enumerate();
            IEnumerable<CardSnapshot> Enumerate() { Trace.Add("public-enumerate"); yield return Face(1); }
        }
        [ProgramViewContributor(ProgramViewContribution.PublicCardsAfterResolution, 10)]
        private static CardSnapshot[] StaticAfter() => [Face(9)];
        [ProgramViewContributor(ProgramViewContribution.PrivateCards, 10)]
        private CardSnapshot[] Private(int viewer) { Trace.Add("private:" + viewer); return [Face(100 + viewer)]; }
        [ProgramViewContributor(ProgramViewContribution.InvariantProcessing, 10)]
        [ProgramViewContributor(ProgramViewContribution.PublicProcessing, 10)]
        private IReadOnlyList<Card> Covered(IReadOnlyList<Card> cards)
        { Need(ReferenceEquals(cards, Initial), "Original Processing input changed."); Trace.Add("covered"); return cards.Where(c => c.Id != 1).ToArray(); }
        [ProgramViewContributor(ProgramViewContribution.InvariantProcessing, 20)]
        private IReadOnlyList<Card> InvariantHeld(IReadOnlyList<Card> cards)
        { Need(cards.Select(c => c.Id).SequenceEqual([2, 3, 4, 5]), "Intermediate Processing input changed."); Trace.Add("invariant-held"); return cards.Where(c => c.Id != 2).ToArray(); }
        [ProgramViewContributor(ProgramViewContribution.PublicProcessingExclusion, 10)]
        private bool Held(int id) => id == 2;
        [ProgramViewContributor(ProgramViewContribution.ConcealedCardExclusion, 10)]
        private bool ConcealedFirst(int id) { Trace.Add("concealed-first:" + id); return id == 3; }
        [ProgramViewContributor(ProgramViewContribution.ConcealedCardExclusion, 20)]
        private bool ConcealedSecond(int id) { Trace.Add("concealed-second:" + id); return id == 4; }
        [ProgramViewContributor(ProgramViewContribution.NamedAmbushPiles, 10)]
        private IReadOnlyList<NamedAmbushPileSnapshot>? NamedFirst(IReadOnlyList<NamedAmbushPileSnapshot>? existing, int holder, int viewer, bool revealAll)
        {
            Need(existing is null && holder == 2 && viewer == 7 && revealAll == ExpectedReveal, "Named pile arguments changed.");
            Trace.Add("named-first"); return [new("first", "fixture", 1, revealAll ? [new(Face(holder), CardKind.Slash)] : null)];
        }
        [ProgramViewContributor(ProgramViewContribution.NamedAmbushPiles, 20)]
        private IReadOnlyList<NamedAmbushPileSnapshot>? NamedSecond(IReadOnlyList<NamedAmbushPileSnapshot>? existing, int holder, int viewer, bool revealAll)
        {
            Need(existing is [{ SkillId: "first" }] && holder == 2 && viewer == 7 && revealAll == ExpectedReveal, "Named pile intermediate state changed.");
            Trace.Add("named-second"); return existing!.Concat([new NamedAmbushPileSnapshot("second", "fixture", 1, revealAll ? [new(Face(holder), CardKind.Slash)] : null)]).ToArray();
        }
        [ProgramViewContributor(ProgramViewContribution.PrivateWhirlPiles, 10)]
        private IReadOnlyList<PrivateWhirlPileSnapshot>? Whirl(IReadOnlyList<PrivateWhirlPileSnapshot>? existing, int owner, int viewer, bool revealAll)
        {
            Need(existing is null && owner == 2 && viewer == 7 && revealAll == ExpectedReveal, "Private pile arguments changed.");
            return [new() { SkillId = "whirl", Count = 1, Cards = revealAll ? [Face(owner)] : null }];
        }
    }
    private sealed class DuplicateOrder { [ProgramViewContributor(ProgramViewContribution.PrivateCards, 10)] private CardSnapshot[] Bad(int viewer) => []; }
    private sealed class DuplicateRegistration
    {
        [ProgramViewContributor(ProgramViewContribution.ProgramPublicCards, 10)]
        [ProgramViewContributor(ProgramViewContribution.ProgramPublicCards, 20)]
        private CardSnapshot[] Bad() => [];
    }
    private sealed class WrongSignature { [ProgramViewContributor(ProgramViewContribution.PrivateCards, 999)] private int Bad(int viewer) => 0; }
    private sealed class OptionalArgument { [ProgramViewContributor(ProgramViewContribution.PrivateCards, 999)] private CardSnapshot[] Bad(int viewer = 0) => []; }
    private sealed class WrongContribution { [ProgramViewContributor((ProgramViewContribution)999, 10)] private CardSnapshot[] Bad() => []; }
    private sealed class ZeroOrder { [ProgramViewContributor(ProgramViewContribution.ProgramPublicCards, 0)] private CardSnapshot[] Bad() => []; }
}
