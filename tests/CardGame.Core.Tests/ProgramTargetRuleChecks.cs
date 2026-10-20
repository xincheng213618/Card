using System.Collections;
using System.Reflection;
using CardGame.Core;

internal static class ProgramTargetRuleChecks
{
    public static void OrderedBoundariesAndLiveQueries()
    {
        var supplied = Methods(typeof(Fixture)).Reverse().ToList();
        var catalog = ProgramTargetRuleCatalog.Discover(supplied);
        supplied.Clear();
        Need(catalog.Registrations is IDictionary<ProgramTargetRulePoint, IReadOnlyList<ProgramTargetRuleRegistration>> { IsReadOnly: true } &&
            catalog.Registrations.All(pair => pair.Value is IList<ProgramTargetRuleRegistration> { IsReadOnly: true } &&
                pair.Value.Select(row => row.Order).SequenceEqual(pair.Key == ProgramTargetRulePoint.DeclaredGlobalProhibition ? [10] : [10, 20])),
            "Target query points retain frozen independent admission sets and explicit order.");
        var a = new Fixture(catalog);
        var b = new Fixture(catalog);
        foreach (var first in new[] { false, true })
        foreach (var last in new[] { false, true })
        {
            a.BlockFirst = first; a.BlockLast = last; a.BlockAfterFirst = first; a.BlockAfterLast = last;
            Check(() => a.Rules.IsDirectedProhibited(a.Directed), first || last, first ? ["first"] : ["first", "last"]);
            Check(() => a.Rules.IsDeclaredGlobalProhibited(a.Directed), first, ["first"]);
            Check(() => a.Rules.HasQuotaAllowance(a.Quota), first || last, first ? ["first"] : ["first", "last"]);
            Check(() => a.Rules.AllowsSlash(a.Slash), !(first || last), first ? ["first"] : ["first", "last"]);
            Check(() => a.Rules.AllowsNativeUseMaterial(a.Native), !(first || last), first ? ["first"] : ["first", "last"]);
            Check(() => a.Rules.HasSlashQuotaBeforeLimit(a.SlashQuota), first || last,
                first ? ["before-first"] : ["before-first", "before-last"]);
            Check(() => a.Rules.HasSlashQuotaAfterLimit(a.SlashQuota), first || last,
                first ? ["after-first"] : ["after-first", "after-last"]);
            a.Trace.Clear();
            var original = new RuleQueryContribution[] { new FiniteRuleQueryContribution("native", SkillRuleOperation.Add, 3) };
            var result = a.Rules.AppendTargetCount(a.Count, original);
            Need(a.Trace.SequenceEqual(["count-first", "count-last"]) && original.Length == 1 &&
                ReferenceEquals(result[0], original[0]) && result.Select(item => item.SourceId)
                    .SequenceEqual(new[] { "native" }.Concat(first ? ["first"] : []).Concat(last ? ["last"] : [])) &&
                result.Skip(1).All(item => item is FiniteRuleQueryContribution { Operation: SkillRuleOperation.Add, Value: 1 }),
                "Count contributions all execute in order after native terms, retain source identities, and append fixed values.");
            if (!first && !last) Need(ReferenceEquals(result, original), "An empty contribution result retains the original array.");
            Need(b.Trace.Count == 0, "Shared target metadata must not share live match state.");
        }
        for (var flags = 0; flags < 128; flags++)
        {
            a.NativeGuard = (flags & 1) != 0; a.SkipCount = (flags & 2) != 0;
            a.BlockFirst = (flags & 4) != 0; a.BlockLast = (flags & 8) != 0;
            a.LimitAvailable = (flags & 16) != 0; a.BlockAfterFirst = (flags & 32) != 0;
            a.BlockAfterLast = (flags & 64) != 0;
            a.Trace.Clear(); a.MaterialDerivations = 0; a.RankDerivations = 0;
            var legacy = a.LegacyQuota(); var trace = a.Trace.ToArray();
            var materials = a.MaterialDerivations; var ranks = a.RankDerivations;
            a.Trace.Clear(); a.MaterialDerivations = 0; a.RankDerivations = 0;
            Need(a.RoutedQuota() == legacy && a.Trace.SequenceEqual(trace) &&
                a.MaterialDerivations == materials && a.RankDerivations == ranks,
                "Splitting target quota around the shared limit changed guard, callback, or lazy derivation order.");
        }
        Need(a.Bindings.Count == a.Bindings.Distinct().Count(), "Multi-point handlers bind once per match.");
        a.BlockFirst = true; b.BlockFirst = false; b.BlockLast = false; a.Trace.Clear();
        Need(a.Rules.IsDirectedProhibited(a.Directed) && !b.Rules.IsDirectedProhibited(b.Directed) &&
            b.Trace.SequenceEqual(["first", "last"]), "Live target queries remain independent across matches.");
        a.Failure = new InvalidOperationException("original target rule failure"); a.Trace.Clear();
        try { _ = a.Rules.IsDirectedProhibited(a.Directed); throw new Exception("Expected original target failure."); }
        catch (InvalidOperationException error) when (ReferenceEquals(error, a.Failure))
        { Need(a.Trace.SequenceEqual(["first"]), "Target errors preserve identity and stop at the original position."); }

        var empty = new ProgramTargetRules(ProgramTargetRuleCatalog.Discover([]), (_, _) => throw new Exception("Unexpected binding."));
        var contributions = new RuleQueryContribution[] { new FiniteRuleQueryContribution("native", SkillRuleOperation.Add, 0) };
        Need(!empty.IsDirectedProhibited(a.Directed) && !empty.IsDeclaredGlobalProhibited(a.Directed) &&
            !empty.HasQuotaAllowance(a.Quota) && empty.AllowsSlash(a.Slash) && empty.AllowsNativeUseMaterial(a.Native) &&
            !empty.HasSlashQuotaBeforeLimit(a.SlashQuota) && !empty.HasSlashQuotaAfterLimit(a.SlashQuota) &&
            ReferenceEquals(empty.AppendTargetCount(a.Count, contributions), contributions),
            "No module policy leaves every native target boundary and contribution array unchanged.");
        var stateless = new ProgramTargetRules(ProgramTargetRuleCatalog.Discover(Methods(typeof(StaticQuery))),
            (method, type) => method.CreateDelegate(type));
        Need(stateless.IsDirectedProhibited(a.Directed), "Stateless target rules retain the same typed contract.");
        Reject(typeof(Duplicate), "Duplicate target rule order");
        Reject(typeof(InvalidPoint), "valid point");
        Reject(typeof(InvalidOrder), "positive order");
        Reject(typeof(WrongQuery), "invalid handler signature");
        Reject(typeof(OptionalQuery), "invalid handler signature");
        Reject(typeof(GenericQuery), "invalid handler signature");
        Reject(typeof(RefQuery), "invalid handler signature");
        var production = ProgramTargetRuleCatalog.Discover(ProgramRuntimeModuleCatalog.Default.Methods);
        Need(production.Registrations.Values.All(rows => rows.Count > 0), "All production target points retain owning module declarations.");

        void Check(Func<bool> query, bool expected, string[] trace)
        {
            a.Trace.Clear();
            Need(query() == expected && a.Trace.SequenceEqual(trace), "A target query changed its original predicate meaning, short circuit, or admission point.");
        }
        static void Reject(Type type, string expected)
        {
            try { _ = ProgramTargetRuleCatalog.Discover(Methods(typeof(Fixture)).Concat(Methods(type))); }
            catch (InvalidOperationException error) when (error.Message.Contains(expected, StringComparison.Ordinal)) { return; }
            throw new InvalidOperationException($"Did not reject target rule {type.Name}: {expected}");
        }
    }

    private static MethodInfo[] Methods(Type type) => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    // A query router must forward the supplied list identity without reading
    // materials before a capability's original native condition reaches them.
    private sealed class UnreadableMaterials : IReadOnlyList<int>
    {
        public int Count => throw new InvalidOperationException("Materials were read by dispatch.");
        public int this[int index] => throw new InvalidOperationException("Materials were indexed by dispatch.");
        public IEnumerator<int> GetEnumerator() => throw new InvalidOperationException("Materials were enumerated by dispatch.");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class Fixture
    {
        internal List<string> Trace { get; } = [];
        internal List<MethodInfo> Bindings { get; } = [];
        internal bool BlockFirst { get; set; }
        internal bool BlockLast { get; set; }
        internal bool BlockAfterFirst { get; set; }
        internal bool BlockAfterLast { get; set; }
        internal bool NativeGuard { get; set; }
        internal bool SkipCount { get; set; }
        internal bool LimitAvailable { get; set; }
        internal int MaterialDerivations { get; set; }
        internal int RankDerivations { get; set; }
        internal Exception? Failure { get; set; }
        internal ProgramTargetRules Rules { get; }
        internal ProgramDirectedTargetQuery Directed { get; } = new(2, 1, CardKind.FireSlash, 123);
        internal ProgramTargetQuotaQuery Quota { get; } = new(2, 1);
        internal ProgramSlashTargetQuery Slash { get; }
        internal ProgramNativeUseMaterialQuery Native { get; }
        internal ProgramTargetCountQuery Count { get; }
        internal ProgramSlashQuotaQuery SlashQuota { get; }

        internal Fixture(ProgramTargetRuleCatalog catalog)
        {
            var actor = Player(2);
            var target = Player(1);
            var source = new CardConversionSource("fixture:conversion", "fixture", 2, "fixture:instance");
            var materials = new UnreadableMaterials();
            Slash = new(actor, target, CardKind.FireSlash, source, materials);
            Native = new(2, CardKind.FireSlash, materials, source, 123);
            Count = new(actor, CardKind.FireSlash);
            SlashQuota = new(actor, target, CardKind.FireSlash, new Card(7, CardKind.Slash, Suit.Club, 8),
                null, 17, materials, EquipmentSlot.Weapon, true);
            Rules = new(catalog, (method, type) => { Bindings.Add(method); return method.CreateDelegate(type, this); });
        }
        private static CharacterState Player(int seat) => new()
        {
            Seat = seat, Name = "fixture", IsHuman = true, Role = Role.Rebel,
            RoleRevealed = false, General = new("fixture", "Fixture", "fixture", []),
            GeneralSelected = true, GeneralRevealed = true, MaxHp = 4, Hp = 3
        };
        private bool Visit(bool first)
        {
            Trace.Add(first ? "first" : "last");
            if (Failure is { } error) throw error;
            return first ? BlockFirst : BlockLast;
        }
        internal bool LegacyQuota() => Guard() && (SkipCount || QuotaBeforeFirst(SlashQuota) ||
            QuotaBeforeLast(SlashQuota) || SharedLimit() || QuotaAfterFirst(SlashQuota) || QuotaAfterLast(SlashQuota));
        internal bool RoutedQuota() => Guard() && (SkipCount || Rules.HasSlashQuotaBeforeLimit(SlashQuota) ||
            SharedLimit() || Rules.HasSlashQuotaAfterLimit(SlashQuota));
        private bool Guard() { Trace.Add("guard"); return NativeGuard; }
        private bool SharedLimit() { Trace.Add("limit"); return LimitAvailable; }
        private IReadOnlyList<int> DeriveMaterials(ProgramSlashQuotaQuery query)
        { MaterialDerivations++; return query.PhysicalCardIds ?? (query.PhysicalCard is { Id: > 0 } ? [query.PhysicalCard.Id] : []); }
        private int? DeriveRank(ProgramSlashQuotaQuery query)
        { RankDerivations++; return query.PhysicalCard?.Rank; }
        [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 10)]
        private bool QuotaBeforeFirst(ProgramSlashQuotaQuery query)
        { Need(query == SlashQuota, "Pre-limit quota raw arguments changed."); Trace.Add("before-first"); return BlockFirst; }
        [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 20)]
        private bool QuotaBeforeLast(ProgramSlashQuotaQuery query)
        {
            Need(query == SlashQuota && ReferenceEquals(DeriveMaterials(query), SlashQuota.PhysicalCardIds), "Quota materials were derived early or changed identity.");
            Trace.Add("before-last"); return BlockLast;
        }
        [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaAfterLimit, 10)]
        private bool QuotaAfterFirst(ProgramSlashQuotaQuery query)
        {
            Need(query == SlashQuota && (query.EffectiveRank ?? DeriveRank(query)) == 8, "Quota effective rank was derived early or changed.");
            Trace.Add("after-first"); return BlockAfterFirst;
        }
        [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaAfterLimit, 20)]
        private bool QuotaAfterLast(ProgramSlashQuotaQuery query)
        { Need(query == SlashQuota, "Post-limit quota raw arguments changed."); Trace.Add("after-last"); return BlockAfterLast; }
        [ProgramTargetRule(ProgramTargetRulePoint.DirectedProhibition, 10)]
        [ProgramTargetRule(ProgramTargetRulePoint.DeclaredGlobalProhibition, 10)]
        private bool DirectedFirst(ProgramDirectedTargetQuery query)
        { Need(query == Directed, "Directed target identity, kind, or issued use changed."); return Visit(true); }
        [ProgramTargetRule(ProgramTargetRulePoint.DirectedProhibition, 20)]
        private bool DirectedLast(ProgramDirectedTargetQuery query)
        { Need(query == Directed, "Directed target arguments changed."); return Visit(false); }
        [ProgramTargetRule(ProgramTargetRulePoint.QuotaAllowance, 10)]
        private bool QuotaFirst(ProgramTargetQuotaQuery query)
        { Need(query == Quota, "Quota participants changed."); return Visit(true); }
        [ProgramTargetRule(ProgramTargetRulePoint.QuotaAllowance, 20)]
        private bool QuotaLast(ProgramTargetQuotaQuery query)
        { Need(query == Quota, "Quota participants changed."); return Visit(false); }
        [ProgramTargetRule(ProgramTargetRulePoint.SlashQualification, 10)]
        private bool SlashFirst(ProgramSlashTargetQuery query)
        { Need(query == Slash, "Slash participant, source, or physical identity changed."); return !Visit(true); }
        [ProgramTargetRule(ProgramTargetRulePoint.SlashQualification, 20)]
        private bool SlashLast(ProgramSlashTargetQuery query)
        { Need(query == Slash, "Slash participant, source, or physical identity changed."); return !Visit(false); }
        [ProgramTargetRule(ProgramTargetRulePoint.NativeUseMaterialQualification, 10)]
        private bool NativeFirst(ProgramNativeUseMaterialQuery query)
        { Need(query == Native, "Native true Use source, materials, or issued frame changed."); return !Visit(true); }
        [ProgramTargetRule(ProgramTargetRulePoint.NativeUseMaterialQualification, 20)]
        private bool NativeLast(ProgramNativeUseMaterialQuery query)
        { Need(query == Native, "Native true Use source, materials, or issued frame changed."); return !Visit(false); }
        [ProgramTargetRule(ProgramTargetRulePoint.TargetCountContributions, 10)]
        private RuleQueryContribution? CountFirst(ProgramTargetCountQuery query)
        { Need(query == Count, "Count actor or effective kind changed."); Trace.Add("count-first"); return BlockFirst ? new FiniteRuleQueryContribution("first", SkillRuleOperation.Add, 1) : null; }
        [ProgramTargetRule(ProgramTargetRulePoint.TargetCountContributions, 20)]
        private RuleQueryContribution? CountLast(ProgramTargetCountQuery query)
        { Need(query == Count, "Count actor or effective kind changed."); Trace.Add("count-last"); return BlockLast ? new FiniteRuleQueryContribution("last", SkillRuleOperation.Add, 1) : null; }
    }
    private static class StaticQuery
    {
        [ProgramTargetRule(ProgramTargetRulePoint.DirectedProhibition, 10)]
        private static bool Query(ProgramDirectedTargetQuery query) => query.ActorSeat == 2;
    }
    private static class Duplicate
    {
        [ProgramTargetRule(ProgramTargetRulePoint.DirectedProhibition, 10)]
        private static bool Query(ProgramDirectedTargetQuery query) => false;
    }
    private static class InvalidPoint
    {
        [ProgramTargetRule((ProgramTargetRulePoint)999, 30)]
        private static bool Query(ProgramDirectedTargetQuery query) => false;
    }
    private static class InvalidOrder
    {
        [ProgramTargetRule(ProgramTargetRulePoint.DirectedProhibition, 0)]
        private static bool Query(ProgramDirectedTargetQuery query) => false;
    }
    private static class WrongQuery
    {
        [ProgramTargetRule(ProgramTargetRulePoint.SlashQualification, 30)]
        private static bool Query(ProgramDirectedTargetQuery query) => false;
    }
    private static class OptionalQuery
    {
        [ProgramTargetRule(ProgramTargetRulePoint.DirectedProhibition, 30)]
        private static bool Query(ProgramDirectedTargetQuery query = default) => false;
    }
    private static class GenericQuery
    {
        [ProgramTargetRule(ProgramTargetRulePoint.DirectedProhibition, 30)]
        private static bool Query<T>(ProgramDirectedTargetQuery query) => false;
    }
    private static class RefQuery
    {
        [ProgramTargetRule(ProgramTargetRulePoint.DirectedProhibition, 30)]
        private static bool Query(ref ProgramDirectedTargetQuery query) => false;
    }
}
