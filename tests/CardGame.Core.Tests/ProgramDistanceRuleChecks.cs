using System.Reflection;
using CardGame.Core;

internal static class ProgramDistanceRuleChecks
{
    public static void OrderedArithmeticAndNativeDistanceQueries()
    {
        var supplied = Methods(typeof(Fixture)).Reverse().ToList();
        var catalog = ProgramDistanceCatalog.Discover(supplied);
        supplied.Clear();
        Need(catalog.Registrations is IDictionary<ProgramDistanceStage, IReadOnlyList<ProgramDistanceRegistration>> { IsReadOnly: true } &&
            catalog.Registrations.All(pair => pair.Value is IList<ProgramDistanceRegistration> { IsReadOnly: true } &&
                pair.Value.Select(row => row.Order).SequenceEqual([10, 20])), "Distance stage metadata freezes its explicit native order.");
        try { ((IList<ProgramDistanceRegistration>)catalog.Registrations[ProgramDistanceStage.PhysicalSlash]).Clear(); throw new Exception("Expected frozen metadata."); }
        catch (NotSupportedException) { }

        var a = new Fixture(catalog);
        var b = new Fixture(catalog);
        var distance = new ProgramDistanceQuery(a.Actor, a.Target);
        var terms = new List<RuleQueryBaseTerm> { new("native:seats", 3) };
        a.Rules.AddDistanceBaseTerms(distance, terms);
        Need(a.Trace.SequenceEqual(["base-first", "base-last"]) && terms.Select(term => term.SourceId).SequenceEqual(["native:seats", "fixture:base-first", "fixture:base-last"]),
            "Physical base additions retain source order and stay separate from use-distance exemptions.");
        a.Trace.Clear();
        var incoming = new List<RuleQueryContribution>();
        a.Rules.AddIncomingContributions(a.Target.Seat, incoming);
        Need(a.Trace.SequenceEqual(["incoming-first", "incoming-last"]) && incoming.Select(row => row.SourceId).SequenceEqual(["fixture:incoming-first", "fixture:incoming-last"]),
            "Each incoming contribution sequence is consumed before the next contributor runs.");
        var native = RuleQueryService.EvaluateDirectionalDistance(terms,
            [new FiniteRuleQueryContribution("native:outgoing-set", SkillRuleOperation.Set, 1)], incoming);
        Need(Value(native) == 4 && native.Stages.Select(stage => stage.Name).SequenceEqual(["outgoing", "incoming"]),
            "Outgoing Set still precedes incoming Add with the shared final clamp.");
        a.Trace.Clear();
        var projected = a.Rules.ProjectDistance(a.Actor, a.Target, native);
        Need(Value(projected) == 8 && a.Trace.SequenceEqual(["project-set:4", "project-add:2"]) &&
            projected.BaseTerms.Single().SourceId == "fixture:before-add", "Distance projections receive the previous evaluation, including its diagnostics, in Set-then-Add order.");
        a.Trace.Clear();
        var rangeTerms = new List<RuleQueryBaseTerm> { new("native:weapon", 1) };
        a.Rules.AddAttackRangeBaseTerms(new(a.Actor, 71, EquipmentSlot.Weapon), rangeTerms);
        var rangeContributions = new List<RuleQueryContribution>();
        a.Rules.AddAttackRangeContributions(a.Actor, rangeContributions);
        Need(a.Trace.SequenceEqual(["range-first", "range-last", "range-contribution-first", "range-contribution-last"]) &&
            rangeTerms[0].Value == 3 && rangeContributions.Count == 2,
            "Equipment-id and slot exclusions reach the virtual-weapon base stage before attack-range contributions.");

        Func<Fixture, bool>[] calls =
        [
            fixture => fixture.Rules.HasCardUseExemption(fixture.CardQuery),
            fixture => fixture.Rules.HasConvertedSlashExemption(fixture.ConversionQuery),
            fixture => fixture.Rules.HasCardUseLimitExemption(fixture.Actor),
            .. new[] { ProgramDistanceStage.PhysicalSlash, ProgramDistanceStage.VirtualSlash, ProgramDistanceStage.ProvidedSlash,
                ProgramDistanceStage.BorrowedSlash, ProgramDistanceStage.BorrowedPhysicalSlash,
                ProgramDistanceStage.BorrowedPaidWeaponSlash, ProgramDistanceStage.SpecificSlashExemption }
                .Select(stage => (Func<Fixture, bool>)(fixture => fixture.Rules.AllowsSlashDistance(stage, fixture.SlashQuery)))
        ];
        foreach (var call in calls)
        foreach (var first in new[] { false, true })
        foreach (var last in new[] { false, true })
        {
            a.Trace.Clear(); a.First = first; a.Last = last;
            Need(call(a) == (first || last) && a.Trace.SequenceEqual(first ? ["first"] : ["first", "last"]),
                "Every native query rechecks live state and short-circuits at its original first exemption.");
            Need(b.Trace.Count == 0, "Shared distance metadata must not share engine state.");
        }
        a.Trace.Clear(); a.First = true; b.First = false; b.Last = false;
        Need(calls[0](a) && !calls[0](b) && b.Trace.SequenceEqual(["first", "last"]), "Independent engine delegates preserve live match isolation.");
        Need(a.Bound.Count == a.Bound.Distinct(StringComparer.Ordinal).Count(), "Methods reused at native stages bind once per engine, not per query.");
        a.Trace.Clear(); a.Failure = new InvalidOperationException("original distance query failure");
        try { calls[0](a); throw new Exception("Expected original distance failure."); }
        catch (InvalidOperationException error) when (ReferenceEquals(error, a.Failure))
        { Need(a.Trace.SequenceEqual(["first"]), "Typed distance delegates retain exception identity and stop later modules."); }

        var empty = new ProgramDistanceRules(ProgramDistanceCatalog.Discover([]), (_, _) => throw new Exception("Unexpected empty bind."));
        Need(!empty.HasCardUseExemption(a.CardQuery) && !empty.HasConvertedSlashExemption(a.ConversionQuery) &&
            !empty.HasCardUseLimitExemption(a.Actor) && !empty.AllowsSlashDistance(ProgramDistanceStage.PhysicalSlash, a.SlashQuery) &&
            ReferenceEquals(empty.ProjectDistance(a.Actor, a.Target, native), native), "Empty catalogs preserve native distance and deny exemptions.");
        Reject(typeof(Duplicate), "Duplicate distance rule order");
        Reject(typeof(InvalidStage), "valid stage");
        Reject(typeof(InvalidOrder), "positive order");
        Reject(typeof(InvalidResult), "invalid handler signature");
        Reject(typeof(OptionalArgument), "invalid handler signature");
        Reject(typeof(GenericQuery), "invalid handler signature");
        Reject(typeof(RefArgument), "invalid handler signature");
        var production = ProgramDistanceCatalog.Discover(Methods(typeof(GameEngine)));
        Need(production.Registrations.All(pair => pair.Value.Count > 0), "Every migrated native distance position retains its declared contributors.");
        var productionMethods = typeof(GameEngine).Assembly.GetTypes().SelectMany(Methods)
            .Where(method => method.GetCustomAttributes<ProgramDistanceRuleAttribute>().Any()).ToArray();
        Need(productionMethods.All(method => method.DeclaringType == typeof(GameEngine)) &&
            production.Registrations.Values.SelectMany(rows => rows).Select(row => row.Method).Distinct().Count() == productionMethods.Length,
            "Every production distance declaration must belong to GameEngine and enter the actual discovery catalog; another partial type must not hide a contributor.");

        static void Reject(Type type, string expected)
        {
            try { _ = ProgramDistanceCatalog.Discover(Methods(typeof(Fixture)).Concat(Methods(type))); }
            catch (InvalidOperationException error) when (error.Message.Contains(expected, StringComparison.Ordinal)) { return; }
            throw new InvalidOperationException($"Did not reject {type.Name}: {expected}");
        }
    }

    private static int Value(RuleQueryEvaluation evaluation) => ((FiniteRuleQueryValue)evaluation.Value).Value;
    private static MethodInfo[] Methods(Type type) => type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture
    {
        internal CharacterState Actor { get; } = Player(2);
        internal CharacterState Target { get; } = Player(3);
        internal List<string> Trace { get; } = [];
        internal List<string> Bound { get; } = [];
        internal bool First { get; set; }
        internal bool Last { get; set; }
        internal Exception? Failure { get; set; }
        internal ProgramDistanceRules Rules { get; }
        internal ProgramCardDistanceQuery CardQuery => new(Actor, Target, CardKind.FireSlash, 123, 71, EquipmentSlot.Weapon);
        internal ProgramSlashConversionDistanceQuery ConversionQuery => new(Actor, new("fixture:conversion", "identity", 2, "instance"));
        internal ProgramSlashDistanceQuery SlashQuery => new(Actor, Target, CardKind.FireSlash,
            Conversion: ConversionQuery.Source, ExistingUseFrameId: 123, PhysicalCardIds: [71], PhysicalSuit: Suit.Diamond,
            EffectiveColor: true, EffectiveRank: 12, NoEffectiveRank: true, SpecificEffectiveRank: 13,
            ExcludedEquipmentId: 71, ExcludedEquipmentSlot: EquipmentSlot.Weapon, AllowAnyPhysicalSuit: true);
        internal Fixture(ProgramDistanceCatalog catalog) => Rules = new(catalog, (method, type) =>
        { Bound.Add(method.Name); return method.CreateDelegate(type, this); });

        private static CharacterState Player(int seat) => new()
        {
            Seat = seat, Name = "fixture", IsHuman = false, Role = Role.Rebel,
            RoleRevealed = true, General = new("fixture", "Fixture", "fixture", []),
            GeneralSelected = true, GeneralRevealed = true, MaxHp = 4, Hp = 4
        };

        private bool Visit(bool first)
        {
            Trace.Add(first ? "first" : "last");
            if (Failure is { } error) throw error;
            return first ? First : Last;
        }
        [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 10)]
        private bool CardFirst(ProgramCardDistanceQuery query) { Need(query == CardQuery, "Card-use arguments changed."); return Visit(true); }
        [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 20)]
        private bool CardLast(ProgramCardDistanceQuery query) { Need(query == CardQuery, "Card-use arguments changed."); return Visit(false); }
        [ProgramDistanceRule(ProgramDistanceStage.ConvertedSlashExemption, 10)]
        private bool ConversionFirst(ProgramSlashConversionDistanceQuery query) { Need(query == ConversionQuery, "Conversion ownership changed."); return Visit(true); }
        [ProgramDistanceRule(ProgramDistanceStage.ConvertedSlashExemption, 20)]
        private bool ConversionLast(ProgramSlashConversionDistanceQuery query) { Need(query == ConversionQuery, "Conversion ownership changed."); return Visit(false); }
        [ProgramDistanceRule(ProgramDistanceStage.CardUseLimitExemption, 10)]
        private bool LimitFirst(CharacterState actor) { Need(ReferenceEquals(actor, Actor), "Limit owner changed."); return Visit(true); }
        [ProgramDistanceRule(ProgramDistanceStage.CardUseLimitExemption, 20)]
        private bool LimitLast(CharacterState actor) { Need(ReferenceEquals(actor, Actor), "Limit owner changed."); return Visit(false); }
        [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 10)]
        [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 10)]
        [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash, 10)]
        [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 10)]
        [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash, 10)]
        [ProgramDistanceRule(ProgramDistanceStage.BorrowedPaidWeaponSlash, 10)]
        [ProgramDistanceRule(ProgramDistanceStage.SpecificSlashExemption, 10)]
        private bool SlashFirst(ProgramSlashDistanceQuery query) { CheckSlash(query); return Visit(true); }
        [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 20)]
        [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 20)]
        [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash, 20)]
        [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 20)]
        [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash, 20)]
        [ProgramDistanceRule(ProgramDistanceStage.BorrowedPaidWeaponSlash, 20)]
        [ProgramDistanceRule(ProgramDistanceStage.SpecificSlashExemption, 20)]
        private bool SlashLast(ProgramSlashDistanceQuery query) { CheckSlash(query); return Visit(false); }
        private void CheckSlash(ProgramSlashDistanceQuery query)
        {
            Need((query with { PhysicalCardIds = null }) == (SlashQuery with { PhysicalCardIds = null }) &&
                query.PhysicalCardIds is [71], "Native distance arguments, rank semantics or equipment exclusions changed.");
        }
        [ProgramDistanceRule(ProgramDistanceStage.DistanceBaseTerms, 10)]
        private void BaseFirst(ProgramDistanceQuery query, List<RuleQueryBaseTerm> terms)
        { Need(ReferenceEquals(query.Source, Actor) && ReferenceEquals(query.Target, Target), "Distance participants changed."); Trace.Add("base-first"); terms.Add(new("fixture:base-first", 2)); }
        [ProgramDistanceRule(ProgramDistanceStage.DistanceBaseTerms, 20)]
        private void BaseLast(ProgramDistanceQuery query, List<RuleQueryBaseTerm> terms)
        { Trace.Add("base-last"); terms.Add(new("fixture:base-last", -1)); }
        [ProgramDistanceRule(ProgramDistanceStage.IncomingContributions, 10)]
        private IEnumerable<RuleQueryContribution> IncomingFirst(int seat)
        { Need(seat == Target.Seat, "Incoming owner changed."); Trace.Add("incoming-first"); yield return new FiniteRuleQueryContribution("fixture:incoming-first", SkillRuleOperation.Add, 1); }
        [ProgramDistanceRule(ProgramDistanceStage.IncomingContributions, 20)]
        private IEnumerable<RuleQueryContribution> IncomingLast(int seat)
        { Need(Trace.SequenceEqual(["incoming-first"]), "Incoming enumeration order changed."); Trace.Add("incoming-last"); yield return new FiniteRuleQueryContribution("fixture:incoming-last", SkillRuleOperation.Add, 2); }
        [ProgramDistanceRule(ProgramDistanceStage.DistanceProjection, 10)]
        private RuleQueryEvaluation ProjectFirst(CharacterState source, CharacterState target, RuleQueryEvaluation prior)
        {
            Need(ReferenceEquals(source, Actor) && ReferenceEquals(target, Target), "Projection participants changed."); Trace.Add($"project-set:{Value(prior)}");
            return RuleQueryService.Evaluate(SkillRuleQuery.OutgoingDistance, new(1, int.MaxValue),
                [new("fixture:before-set", Value(prior))], [new FiniteRuleQueryContribution("fixture:set", SkillRuleOperation.Set, 2)]);
        }
        [ProgramDistanceRule(ProgramDistanceStage.DistanceProjection, 20)]
        private RuleQueryEvaluation ProjectLast(CharacterState source, CharacterState target, RuleQueryEvaluation prior)
        {
            Trace.Add($"project-add:{Value(prior)}");
            return RuleQueryService.Evaluate(SkillRuleQuery.OutgoingDistance, new(1, int.MaxValue),
                [new("fixture:before-add", Value(prior))], [new FiniteRuleQueryContribution("fixture:add", SkillRuleOperation.Add, 6)]);
        }
        [ProgramDistanceRule(ProgramDistanceStage.AttackRangeBaseTerms, 10)]
        private void RangeFirst(ProgramAttackRangeQuery query, List<RuleQueryBaseTerm> terms)
        { Need(ReferenceEquals(query.Player, Actor) && query.ExcludedEquipmentId == 71 && query.ExcludedEquipmentSlot == EquipmentSlot.Weapon, "Excluded equipment changed."); Trace.Add("range-first"); terms[0] = new("fixture:virtual-weapon", 3); }
        [ProgramDistanceRule(ProgramDistanceStage.AttackRangeBaseTerms, 20)]
        private void RangeLast(ProgramAttackRangeQuery query, List<RuleQueryBaseTerm> terms)
        { Need(terms[0].Value == 3, "Virtual weapon base lost its position."); Trace.Add("range-last"); }
        [ProgramDistanceRule(ProgramDistanceStage.AttackRangeContributions, 10)]
        private void RangeContributionFirst(CharacterState player, List<RuleQueryContribution> contributions)
        { Need(ReferenceEquals(player, Actor), "Range owner changed."); Trace.Add("range-contribution-first"); contributions.Add(new FiniteRuleQueryContribution("fixture:range-first", SkillRuleOperation.Add, 1)); }
        [ProgramDistanceRule(ProgramDistanceStage.AttackRangeContributions, 20)]
        private void RangeContributionLast(CharacterState player, List<RuleQueryContribution> contributions)
        { Trace.Add("range-contribution-last"); contributions.Add(new FiniteRuleQueryContribution("fixture:range-last", SkillRuleOperation.Add, 2)); }
    }

    private sealed class Duplicate
    { [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 10)] private bool Query(ProgramSlashDistanceQuery query) => false; }
    private sealed class InvalidStage
    { [ProgramDistanceRule((ProgramDistanceStage)999, 10)] private bool Query(ProgramSlashDistanceQuery query) => false; }
    private sealed class InvalidOrder
    { [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 0)] private bool Query(ProgramSlashDistanceQuery query) => false; }
    private sealed class InvalidResult
    { [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 999)] private int Query(ProgramSlashDistanceQuery query) => 0; }
    private sealed class OptionalArgument
    { [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 999)] private bool Query(ProgramSlashDistanceQuery query = default) => false; }
    private sealed class GenericQuery
    { [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 999)] private bool Query<T>(ProgramSlashDistanceQuery query) => false; }
    private sealed class RefArgument
    { [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 999)] private bool Query(ref ProgramSlashDistanceQuery query) => false; }
}
