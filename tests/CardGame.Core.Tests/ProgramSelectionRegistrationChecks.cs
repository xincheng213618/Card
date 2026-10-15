using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramSelectionRegistrationChecks
{
    public static void CatalogRegistrationPreservesOrderFilteringAndFailures()
    {
        Type[] types = [typeof(Tail), typeof(OtherPhase), typeof(Stop), typeof(DrawOnly), typeof(Early), typeof(RecoverOnly)];
        var catalog = new ProgramSelectionCatalog(types);
        var reversed = new ProgramSelectionCatalog(types.Reverse());
        Require(catalog.Registrations.Select(item => (item.Stage, item.Order, item.Type))
                .SequenceEqual(reversed.Registrations.Select(item => (item.Stage, item.Order, item.Type))),
            "Discovery enumeration must not change phase or explicit registration order.");
        RequireReadOnly(catalog.Registrations, catalog.Registrations[0], "Registration metadata must be immutable.");
        foreach (var registration in catalog.Registrations)
            RequireReadOnly(registration.Operations, SkillProgramEffectOp.Draw, "Operation filters must be immutable.");
        var draw = Context(SkillProgramEffectOp.Draw);
        var modules = catalog.For(ProgramSelectionStage.AfterCards, draw.Plan);
        RequireReadOnly(modules, new object(), "A cached plan must not expose its shared module array.");

        var first = new RegistrationQueries();
        var second = new RegistrationQueries();
        var firstRegistry = new ProgramSelectionRegistry(catalog, first);
        var secondRegistry = new ProgramSelectionRegistry(catalog, second);
        Require(firstRegistry.Apply(ProgramSelectionStage.AfterCards, draw, new()) &&
                first.Calls.SequenceEqual(["early", "draw", "stop", "tail"]) && second.Calls.Count == 0,
            "Applicable modules must run in order through the query instance bound to this registry.");
        first.Calls.Clear();
        first.Continue = false;
        Require(!firstRegistry.Apply(ProgramSelectionStage.AfterCards, draw, new()) &&
                first.Calls.SequenceEqual(["early", "draw", "stop"]),
            "A live veto must stop later modules even when the execution plan was cached by an earlier query.");
        Require(secondRegistry.Apply(ProgramSelectionStage.AfterCards, draw, new()) &&
                second.Calls.SequenceEqual(["early", "draw", "stop", "tail"]),
            "Sharing a catalog must not share another registry's query state or veto results.");
        first.Calls.Clear();
        Require(firstRegistry.Apply(ProgramSelectionStage.AfterTargets, draw, new()) && first.Calls.SequenceEqual(["other-phase"]),
            "The same numeric order in another phase must remain isolated from card filters.");
        second.Calls.Clear();
        Require(secondRegistry.Apply(ProgramSelectionStage.AfterCards, Context(SkillProgramEffectOp.Recover), new()) &&
                second.Calls.SequenceEqual(["early", "recover", "stop", "tail"]),
            "Operation filters must be matched to each immutable plan rather than the first plan queried.");
        second.Calls.Clear();
        Require(secondRegistry.Apply(ProgramSelectionStage.AfterCards, Context(SkillProgramEffectOp.Damage), new()) &&
                second.Calls.SequenceEqual(["early", "draw", "stop", "tail"]),
            "A module with multiple operations must apply when any registered operation is present.");

        Reject<InvalidOperationException>(() => new ProgramSelectionCatalog([typeof(Early), typeof(DuplicateOrder)]));
        Reject<InvalidOperationException>(() => new ProgramSelectionCatalog([typeof(MutableModule)]));
        Reject<InvalidOperationException>(() => new ProgramSelectionCatalog([typeof(WrongContract)]));
        Reject<InvalidOperationException>(() => new ProgramSelectionCatalog([typeof(UnknownStage)]));
        Reject<InvalidOperationException>(() => new ProgramSelectionCatalog([typeof(UnknownOperation)]));
        Reject<InvalidOperationException>(() => new ProgramSelectionCatalog([typeof(Unregistered)]));
        Reject<InvalidOperationException>(() => new ProgramSelectionCatalog([]));
        try { _ = new ProgramSelectionCatalog([typeof(ThrowingConstructor)]); }
        catch (InvalidOperationException error) when (ReferenceEquals(error, ThrowingConstructor.Failure)) { return; }
        throw new InvalidOperationException("A selection-module constructor failure must propagate as its original exception.");
    }

    public static void ClaimedSourcesKeepCardAndTargetStagesSeparate()
    {
        var catalog = new ProgramSelectionCatalog([typeof(FallbackSource), typeof(EmptyClaimSource)]);
        var queries = new RegistrationQueries { TargetBase = 7 };
        var registry = new ProgramSelectionRegistry(catalog, queries);
        var context = Context(SkillProgramEffectOp.Draw);
        var candidates = new ProgramSelectionCandidates { Cards = [99], Targets = [77] };
        Require(registry.TryBuildSource(context, candidates) && candidates.Cards.Length == 0 &&
                candidates.Source is EmptyClaimSource && candidates.Targets.SequenceEqual([77]) &&
                queries.Calls.SequenceEqual(["claim-cards"]),
            "A source that claims an empty card domain must suppress fallback and defer target construction.");
        candidates.Cards = [4, 5];
        queries.TargetBase = 20;
        Require(registry.TryBuildSourceTargets(context, candidates) && candidates.Targets.SequenceEqual([22]) &&
                queries.Calls.SequenceEqual(["claim-cards", "claim-targets"]),
            "The claimed source must build targets later from current query facts and the transformed card domain.");

        var otherQueries = new RegistrationQueries { TargetBase = 40 };
        var otherRegistry = new ProgramSelectionRegistry(catalog, otherQueries);
        var other = new ProgramSelectionCandidates();
        Require(otherRegistry.TryBuildSource(context, other) && otherRegistry.TryBuildSourceTargets(context, other) &&
                other.Targets.SequenceEqual([40]) && candidates.Targets.SequenceEqual([22]),
            "A shared stateless source must use the current registry's query adapter for both stages.");
        var unclaimed = new ProgramSelectionCandidates();
        var selective = new ProgramSelectionRegistry(new ProgramSelectionCatalog([typeof(EmptyClaimSource)]), queries);
        Require(!selective.TryBuildSource(Context(SkillProgramEffectOp.Recover), unclaimed) && unclaimed.Source is null &&
                !selective.TryBuildSourceTargets(context, unclaimed),
            "An unmatched source must leave the ordinary card and target paths available without invoking a target callback.");
    }

    public static void ConvertedAndAllHandSourcesRequeryPrivateFacts()
    {
        var catalog = new ProgramSelectionCatalog([typeof(ConvertedCardSelectionSource), typeof(AllHandTrickSelectionSource),
            typeof(ConvertedSelectionAvailability), typeof(AllHandTrickAvailability), typeof(FallbackSource)]);
        var queries = new SourceQueries();
        var registry = new ProgramSelectionRegistry(catalog, queries);
        var converted = Context(SkillProgramEffectOp.UseSelectedCardsAs);
        var physical = Array.AsReadOnly(new[] { Card(4), Card(2) });
        queries.ConversionFacts = Array.AsReadOnly(new[]
        {
            Conversion(physical), Conversion(Array.AsReadOnly(new[] { Card(8) }), skill: "other:skill"),
            Conversion(Array.AsReadOnly(new[] { Card(9) }), binding: "other-binding")
        });
        queries.SlashTargets.UnionWith([1, 2]);
        var candidates = new ProgramSelectionCandidates { Targets = [77] };
        Require(registry.TryBuildSource(converted, candidates) && candidates.Cards.SequenceEqual([2, 4]) &&
                candidates.Targets.SequenceEqual([77]) && candidates.MultiCardUses.Count == 1 &&
                !queries.Calls.Any(call => call.StartsWith("slash:", StringComparison.Ordinal)),
            "Converted card sources must scope materials to the exact skill/binding and defer all target queries.");
        RequireReadOnly(candidates.MultiCardUses, Conversion(physical), "Converted-selection metadata must be immutable.");
        RequireReadOnly(candidates.MultiCardUses[0].Cards, Card(99), "Private conversion card facts must remain immutable.");
        Require(registry.TryBuildSourceTargets(converted, candidates) && candidates.Targets.SequenceEqual([1, 2]) &&
                registry.Apply(ProgramSelectionStage.Availability, converted, candidates),
            "Converted targets must be built from the captured native conversion groups at the target stage.");
        queries.SlashTargets.Clear();
        queries.SlashTargets.Add(2);
        candidates = new();
        Require(registry.TryBuildSource(converted, candidates) && registry.TryBuildSourceTargets(converted, candidates) &&
                candidates.Targets.SequenceEqual([2]), "Repeated source queries must not cache target eligibility.");

        queries.ConversionFacts = Array.AsReadOnly(Array.Empty<ProgramMultiCardViewAsSelection>());
        queries.Calls.Clear();
        candidates = new();
        Require(registry.TryBuildSource(converted, candidates) && candidates.Cards.Length == 0 &&
                candidates.Source is ConvertedCardSelectionSource && !queries.Calls.Contains("fallback") &&
                !registry.Apply(ProgramSelectionStage.Availability, converted, candidates),
            "A production converted source with no legal material must stay claimed and fail availability without ordinary fallback.");

        var allHand = Context(SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick);
        queries.HandFacts = Array.AsReadOnly(new[] { Card(7), Card(3) });
        candidates = new();
        Require(registry.TryBuildSource(allHand, candidates) && candidates.AllHandInput && candidates.Cards.SequenceEqual([3, 7]) &&
                registry.TryBuildSourceTargets(allHand, candidates) && candidates.Targets.Length == 0 &&
                registry.Apply(ProgramSelectionStage.Availability, allHand, candidates),
            "All-hand trick sources must select the full sorted hand and keep the ordinary implicit target domain.");
        queries.RestrictedCards.Add(7);
        candidates = new();
        Require(registry.TryBuildSource(allHand, candidates) && candidates.Cards.Length == 0 &&
                !registry.Apply(ProgramSelectionStage.Availability, allHand, candidates),
            "One newly restricted hand card must invalidate the entire all-hand source on the next query.");
        Require(queries.HandFacts.Select(card => card.Id).SequenceEqual([7, 3]),
            "Source selection must never reorder or remove cards from private query facts.");

        var content = StandardContentRegistry.Create();
        var first = GameEngine.CreateStandard(new GameOptions { Seed = 7 }, content);
        var second = GameEngine.CreateStandard(new GameOptions { Seed = 8 }, content);
        var firstQueries = SourceAdapter(first);
        var secondQueries = SourceAdapter(second);
        Require(!ReferenceEquals(firstQueries, secondQueries) && ReferenceEquals(firstQueries, SourceAdapter(first)),
            "Production query capabilities must be cached per engine rather than retained by shared selection metadata.");
        RequireReadOnly(firstQueries.Participants(), default, "Participant query facts must be immutable.");
        RequireReadOnly(firstQueries.Hand(0), Card(999), "Hand query facts must be immutable snapshots.");
        RequireReadOnly(firstQueries.Equipment(0), Card(999), "Equipment query facts must be immutable snapshots.");
        Require(firstQueries is not ISkillProgramEffectHost,
            "A selection query adapter must not expose the command/effect host contract.");
    }

    private static IProgramSelectionSourceQuery SourceAdapter(GameEngine game) =>
        ((ProgramCapabilityRegistry)typeof(GameEngine).GetField("_programCapabilities", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(game)!).Get<IProgramSelectionSourceQuery>();

    private static ProgramSelectionContext Context(params SkillProgramEffectOp[] operations)
    {
        var condition = new SkillProgramCondition(SkillProgramConditionKind.Always, 0, []);
        var effects = Array.AsReadOnly(operations.Select(op => new SkillProgramEffect(op, SkillProgramEffectTarget.Owner, 0, condition,
            sourceBind: "convert", outputKind: op switch
            {
                SkillProgramEffectOp.UseSelectedCardsAs => CardKind.Slash,
                SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick => CardKind.Duel,
                _ => null
            })).ToArray());
        var activation = new SkillProgramActivation("activate", 0, 4, 1, 1, SkillProgramTargetKind.AnyLiving,
            null, condition, effects);
        var program = new SkillProgram("fixture:selection-registration", 1, "fixture-hash", "fixture-runtime",
            GameCheckpoint.CurrentRulesVersion, [], [], Array.AsReadOnly(new[] { activation }), [], [], []);
        var plan = new ProgramExecutionPlan(program.Id, program.GameplayHash, ProgramInstructionSourceKind.Activation,
            activation.Id, effects, activation: activation);
        return new(0, program, plan);
    }

    private static Card Card(int id) => new(id, CardKind.Dodge, Suit.Heart, 7);
    private static ProgramMultiCardViewAsSelection Conversion(IReadOnlyList<Card> cards,
        string skill = "fixture:selection-registration", string binding = "convert") =>
        new(cards, new(skill, binding, 0, "instance"), CardKind.Slash);

    private static void RequireReadOnly<T>(IReadOnlyList<T> source, T value, string message)
    {
        Require(source is IList<T> { IsReadOnly: true }, message);
        if (source.Count != 0)
        {
            var rejected = false;
            try { ((IList<T>)source)[0] = source[0]; }
            catch (NotSupportedException) { rejected = true; }
            Require(rejected, message);
        }
        try { ((IList<T>)source).Add(value); }
        catch (NotSupportedException) { return; }
        ((IList<T>)source).RemoveAt(source.Count - 1);
        throw new InvalidOperationException(message);
    }

    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected '{typeof(T).Name}' for invalid selection metadata.");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private interface IRegistrationQuery : IProgramSelectionQuery
    {
        bool Probe(string name);
        int TargetBase { get; }
    }

    private sealed class RegistrationQueries : IProgramSelectionQueries, IRegistrationQuery
    {
        internal List<string> Calls { get; } = [];
        internal bool Continue { get; set; } = true;
        public int TargetBase { get; set; }
        public bool Probe(string name) { Calls.Add(name); return name != "stop" || Continue; }
        public T Get<T>() where T : class, IProgramSelectionQuery => this is T query ? query : throw new NotSupportedException();
    }

    [ProgramSelectionModule(ProgramSelectionStage.AfterCards, 10)]
    private sealed class Early : IProgramSelectionCandidateFilter
    { public bool Apply(ProgramSelectionContext c, ProgramSelectionCandidates d, IProgramSelectionQueries q) => q.Get<IRegistrationQuery>().Probe("early"); }
    [ProgramSelectionModule(ProgramSelectionStage.AfterCards, 20, SkillProgramEffectOp.Draw, SkillProgramEffectOp.Damage)]
    private sealed class DrawOnly : IProgramSelectionCandidateFilter
    { public bool Apply(ProgramSelectionContext c, ProgramSelectionCandidates d, IProgramSelectionQueries q) => q.Get<IRegistrationQuery>().Probe("draw"); }
    [ProgramSelectionModule(ProgramSelectionStage.AfterCards, 30, SkillProgramEffectOp.Recover)]
    private sealed class RecoverOnly : IProgramSelectionCandidateFilter
    { public bool Apply(ProgramSelectionContext c, ProgramSelectionCandidates d, IProgramSelectionQueries q) => q.Get<IRegistrationQuery>().Probe("recover"); }
    [ProgramSelectionModule(ProgramSelectionStage.AfterCards, 40)]
    private sealed class Stop : IProgramSelectionCandidateFilter
    { public bool Apply(ProgramSelectionContext c, ProgramSelectionCandidates d, IProgramSelectionQueries q) => q.Get<IRegistrationQuery>().Probe("stop"); }
    [ProgramSelectionModule(ProgramSelectionStage.AfterCards, 50)]
    private sealed class Tail : IProgramSelectionCandidateFilter
    { public bool Apply(ProgramSelectionContext c, ProgramSelectionCandidates d, IProgramSelectionQueries q) => q.Get<IRegistrationQuery>().Probe("tail"); }
    [ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 10)]
    private sealed class OtherPhase : IProgramSelectionCandidateFilter
    { public bool Apply(ProgramSelectionContext c, ProgramSelectionCandidates d, IProgramSelectionQueries q) => q.Get<IRegistrationQuery>().Probe("other-phase"); }

    [ProgramSelectionModule(ProgramSelectionStage.AfterCards, 10)]
    private sealed class DuplicateOrder : IProgramSelectionCandidateFilter
    { public bool Apply(ProgramSelectionContext c, ProgramSelectionCandidates d, IProgramSelectionQueries q) => true; }
    [ProgramSelectionModule(ProgramSelectionStage.AfterCards, 10)]
    private sealed class MutableModule : IProgramSelectionCandidateFilter
    {
        private int _calls;
        public bool Apply(ProgramSelectionContext c, ProgramSelectionCandidates d, IProgramSelectionQueries q) => ++_calls > 0;
    }
    [ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 10)]
    private sealed class WrongContract : IProgramSelectionCardFilter
    { public bool Allows(ProgramSelectionContext c, Card card, IProgramSelectionQueries q) => true; }
    [ProgramSelectionModule((ProgramSelectionStage)12345, 10)]
    private sealed class UnknownStage : IProgramSelectionCandidateFilter
    { public bool Apply(ProgramSelectionContext c, ProgramSelectionCandidates d, IProgramSelectionQueries q) => true; }
    [ProgramSelectionModule(ProgramSelectionStage.AfterCards, 10, (SkillProgramEffectOp)123456789)]
    private sealed class UnknownOperation : IProgramSelectionCandidateFilter
    { public bool Apply(ProgramSelectionContext c, ProgramSelectionCandidates d, IProgramSelectionQueries q) => true; }
    private sealed class Unregistered { }
    [ProgramSelectionModule(ProgramSelectionStage.AfterCards, 10)]
    private sealed class ThrowingConstructor : IProgramSelectionCandidateFilter
    {
        internal static readonly InvalidOperationException Failure = new("Selection constructor fixture failure.");
        internal ThrowingConstructor() => throw Failure;
        public bool Apply(ProgramSelectionContext c, ProgramSelectionCandidates d, IProgramSelectionQueries q) => true;
    }

    [ProgramSelectionModule(ProgramSelectionStage.Sources, 10, SkillProgramEffectOp.Draw)]
    private sealed class EmptyClaimSource : IProgramSelectionSourceProvider
    {
        public bool TryBuild(ProgramSelectionContext c, ProgramSelectionCandidates d, IProgramSelectionQueries q)
        { q.Get<IRegistrationQuery>().Probe("claim-cards"); d.Cards = []; return true; }
        public void BuildTargets(ProgramSelectionContext c, ProgramSelectionCandidates d, IProgramSelectionQueries q)
        { var query = q.Get<IRegistrationQuery>(); query.Probe("claim-targets"); d.Targets = [query.TargetBase + d.Cards.Length]; }
    }
    [ProgramSelectionModule(ProgramSelectionStage.Sources, 99)]
    private sealed class FallbackSource : IProgramSelectionSourceProvider
    {
        public bool TryBuild(ProgramSelectionContext c, ProgramSelectionCandidates d, IProgramSelectionQueries q)
        { q.Get<IRegistrationQuery>().Probe("fallback"); d.Cards = [99]; return true; }
        public void BuildTargets(ProgramSelectionContext c, ProgramSelectionCandidates d, IProgramSelectionQueries q)
        { q.Get<IRegistrationQuery>().Probe("fallback-targets"); d.Targets = [99]; }
    }

    private sealed class SourceQueries : IProgramSelectionQueries, IProgramSelectionSourceQuery, IRegistrationQuery
    {
        internal List<string> Calls { get; } = [];
        internal IReadOnlyList<Card> HandFacts { get; set; } = Array.AsReadOnly(Array.Empty<Card>());
        internal IReadOnlyList<ProgramMultiCardViewAsSelection> ConversionFacts { get; set; } =
            Array.AsReadOnly(Array.Empty<ProgramMultiCardViewAsSelection>());
        internal HashSet<int> RestrictedCards { get; } = [];
        internal HashSet<int> SlashTargets { get; } = [];
        public int TargetBase => 0;
        public bool Probe(string name) { Calls.Add(name); return true; }
        public T Get<T>() where T : class, IProgramSelectionQuery => this is T query ? query : throw new NotSupportedException();
        public IReadOnlyList<ProgramSelectionParticipant> Participants() => Array.AsReadOnly(new[]
        {
            new ProgramSelectionParticipant(2, true, 4, 4, true, 1, 0),
            new ProgramSelectionParticipant(0, true, 4, 4, true, HandFacts.Count, 0),
            new ProgramSelectionParticipant(1, true, 3, 4, true, 1, 0)
        });
        public IReadOnlyList<Card> Hand(int seat) { Calls.Add("hand"); return HandFacts; }
        public IReadOnlyList<Card> Equipment(int seat) => Array.AsReadOnly(Array.Empty<Card>());
        public CardLocation Location(int cardId) => CardLocation.Hand(0);
        public bool IsHandRestricted(int seat, Card card) => RestrictedCards.Contains(card.Id);
        public bool HasCardIdentity(int seat, Card card) => false;
        public bool IsUseForbidden(int seat, CardKind kind) => false;
        public bool IsPhysicalUseForbidden(int seat, int cardId) => false;
        public Suit EffectiveSuit(int seat, Card card) => card.Suit;
        public IReadOnlyList<ProgramMultiCardViewAsSelection> Conversions(int seat, CardKind kind)
        { Calls.Add("conversions"); return ConversionFacts; }
        public bool HasHpLossTargets(int seat) => false;
        public bool CanUseSlash(int owner, int target, IReadOnlyList<Card>? cards, bool ignoreDistance)
        { Calls.Add($"slash:{target}"); return SlashTargets.Contains(target); }
        public bool CanUseGlobal(int owner, CardKind kind) => true;
        public bool CanUseOrdinaryTrick(int owner, CardKind? kind, IReadOnlyList<int> physicalCards) => true;
        public bool CanRequestFactionSlash(int owner, string faction) => true;
        public bool HasNextUseAdjustment(int owner) => false;
        public bool IsTargetProhibited(int target, CardKind kind, int owner, IReadOnlyList<Card> cards) => false;
        public bool HasSuitShield(int owner, int target, IReadOnlyList<Card> cards) => false;
        public int HpLossMaximum(int owner, CardKind kind) => 1;
    }
}
