using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramSelectionTransformChecks
{
    public static void CandidateTransformsRequeryFactsAndPreserveReadOnlySources()
    {
        var queries = new Queries
        {
            RecastMaterials = Array.AsReadOnly(new[]
            {
                Material(1, CardKind.Slash), Material(2, CardKind.FireSlash), Material(3, CardKind.Dodge),
                Material(4, CardKind.BaguaFormation), Material(5, CardKind.QinggangSword)
            })
        };
        var originalCards = new[] { 5, 2, 3, 1, 2, 4, 99 };
        var candidates = new ProgramSelectionCandidates { Cards = originalCards };
        var matching = Context(SkillProgramEffectOp.RecastMatchingCardsThenPeerChoice);
        new MatchingRecastSelectionFilter().Apply(matching, candidates, queries);
        Require(candidates.Cards.SequenceEqual([5, 2, 1, 4]) && originalCards.SequenceEqual([5, 2, 3, 1, 2, 4, 99]),
            "Matching recasts must preserve initial order, normalize Slash names and leave the source array unchanged.");
        Require(queries.RecastMaterials.Select(item => item.CardId).SequenceEqual([1, 2, 3, 4, 5]),
            "Filtering candidates must not edit query material facts.");
        queries.RecastMaterials = Array.AsReadOnly(new[]
            { Material(1, CardKind.Slash), Material(3, CardKind.Dodge), Material(4, CardKind.BaguaFormation) });
        candidates = new() { Cards = originalCards };
        new MatchingRecastSelectionFilter().Apply(matching, candidates, queries);
        Require(candidates.Cards.Length == 0, "A second action query must recompute eligibility after its material facts change.");

        var pair = Context(SkillProgramEffectOp.ObtainOneFromEachSelectedTarget);
        queries.ObtainTargets.UnionWith([0, 2]);
        var originalTargets = new[] { 3 };
        candidates = new() { Targets = originalTargets };
        Require(new PairObtainSelectionFilter().Apply(pair, candidates, queries) && candidates.Targets.SequenceEqual([0, 2]),
            "Pair obtain must replace the earlier target domain, including a payable owner.");
        Require(originalTargets.SequenceEqual([3]) && queries.InstanceReads == 1 &&
                queries.ObtainInstances.All(value => value == "first"),
            "Pair reconstruction must leave source targets intact and capture the current skill instance once.");
        queries.ObtainTargets.Clear();
        queries.ObtainTargets.UnionWith([1, 3]);
        queries.Instance = "second";
        queries.ObtainInstances.Clear();
        candidates = new() { Targets = originalTargets };
        Require(new PairObtainSelectionFilter().Apply(pair, candidates, queries) && candidates.Targets.SequenceEqual([1, 3]) &&
                queries.InstanceReads == 2 && queries.ObtainInstances.All(value => value == "second"),
            "Cached modules must never cache prior target eligibility or an expired skill instance.");

        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7 }, StandardContentRegistry.Create());
        var registry = (ProgramCapabilityRegistry)typeof(GameEngine)
            .GetField("_programCapabilities", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        var players = registry.Get<ISelectionParticipantQueries>();
        var seats = players.Seats;
        RequireReadOnly(seats, 99, "Production seat facts must be immutable detached lists.");
        Require(players.Seats.SequenceEqual(seats), "A rejected query-fact edit must not change engine participants.");
        var materials = registry.Get<IMatchingRecastSelectionQueries>().Materials(0);
        RequireReadOnly(materials, Material(999, CardKind.Slash), "Production matching-material facts must be immutable lists.");
    }

    public static void CorrelatedSelectionsPreserveOrderedPairsAndCardTargets()
    {
        var queries = new Queries();
        queries.Hands[0] = 1;
        queries.Hands[1] = 1;
        queries.Hands[2] = 2;
        queries.Hands[3] = 9;
        var pair = Context(SkillProgramEffectOp.ExchangeHandsAndArmPhaseDebt);
        var candidates = new ProgramSelectionCandidates { Targets = [3, 2, 1, 0] };
        Require(new DeferredHandPairSelectionFilter().Apply(pair, candidates, queries) && candidates.Targets.SequenceEqual([2, 1]),
            "Deferred exchange must retain only participants with a currently payable partner.");
        new DeferredHandPairSelectionRelationships().Apply(pair, candidates, queries);
        Require(candidates.TargetSequences is { } sequences && sequences.Count == 2 &&
                sequences[0].SequenceEqual([2, 1]) && sequences[1].SequenceEqual([1, 2]),
            "Ordered target pairs must follow the current target order in both directions.");
        RequireReadOnly(candidates.TargetSequences!, Array.AsReadOnly(new[] { 7, 8 }),
            "The outer ordered-pair list must be immutable.");
        foreach (var sequence in candidates.TargetSequences!)
            RequireReadOnly(sequence, 99, "Each nested ordered pair must be immutable.");
        queries.Hands[2] = 8;
        candidates = new() { Targets = [3, 2, 1] };
        Require(new DeferredHandPairSelectionFilter().Apply(pair, candidates, queries) && candidates.Targets.SequenceEqual([3, 2]),
            "A later query must reevaluate the same pair rule from current hand counts.");

        var placement = Context(SkillProgramEffectOp.PlaceSelectedEquipment);
        queries.Placements.UnionWith([(10, 1), (11, 2)]);
        var cards = new[] { 12, 11, 10 };
        var targets = new[] { 3, 2, 1 };
        candidates = new() { Cards = cards, Targets = targets };
        new EquipmentPlacementSelectionFilter().Apply(placement, candidates, queries);
        Require(candidates.Cards.SequenceEqual([11, 10]) && candidates.Targets.SequenceEqual([2, 1]) &&
                cards.SequenceEqual([12, 11, 10]) && targets.SequenceEqual([3, 2, 1]),
            "Placement must first filter targets by original cards, then cards by retained targets, without mutating inputs.");
        Require(queries.PlacementCalls.SequenceEqual(new[]
        {
            "12:3", "11:3", "10:3", "12:2", "11:2", "12:1", "11:1", "10:1",
            "12:2", "12:1", "11:2", "10:2", "10:1"
        }), "Correlated placement checks must preserve the existing two-pass short-circuit order.");
        new EquipmentPlacementSelectionRelationships().Apply(placement, candidates, queries);
        Require(candidates.CardTargets is { } cardTargets &&
                cardTargets.SequenceEqual(new[] { new ActiveSkillCardTarget(11, 2), new ActiveSkillCardTarget(10, 1) }),
            "Selectable card-target relationships must retain real placements instead of the candidate Cartesian product.");
        RequireReadOnly(candidates.CardTargets!, new ActiveSkillCardTarget(99, 99),
            "Published card-target relationship facts must be immutable.");

        var category = Context(SkillProgramEffectOp.Draw, categoryLedger: "category");
        queries.CategoryTargets.UnionWith([(12, 2), (10, 1)]);
        candidates = new() { Cards = cards, Targets = targets };
        new CategoryTargetLedgerSelectionFilter().Apply(category, candidates, queries);
        Require(candidates.Cards.SequenceEqual([12, 10]) && candidates.Targets.SequenceEqual([2, 1]),
            "Category ledgers must keep cards and targets only while a compatible partner survives.");
    }

    public static void PindianOrderingUsesCurrentFactsWithoutMutatingInputs()
    {
        var queries = new Queries { NextPindian = true };
        queries.Ranks[10] = 8;
        queries.Ranks[11] = 13;
        queries.Ranks[12] = 13;
        queries.KeepValues[10] = 40;
        queries.KeepValues[11] = 70;
        queries.KeepValues[12] = 35;
        var input = new[] { 10, 11, 12 };
        var context = Context(SkillProgramEffectOp.Pindian);
        var candidates = new ProgramSelectionCandidates { Cards = input };
        new NextActualUsePindianSelectionOrder().Apply(context, candidates, queries);
        Require(candidates.Cards.SequenceEqual([12, 11, 10]) && input.SequenceEqual([10, 11, 12]),
            "Next-use Pindian ordering must prefer effective rank, then keep value, while preserving input facts.");
        var simultaneous = Context(SkillProgramEffectOp.StartSimultaneousPindian);
        candidates = new() { Cards = input };
        new SimultaneousPindianSelectionOrder().Apply(simultaneous, candidates, queries);
        Require(candidates.Cards.SequenceEqual([11, 12, 10]),
            "Simultaneous Pindian must use card identity rather than keep value to break equal-rank ties.");
        queries.Ranks[10] = 14;
        candidates = new() { Cards = input };
        new NextActualUsePindianSelectionOrder().Apply(context, candidates, queries);
        Require(candidates.Cards.SequenceEqual([10, 12, 11]), "Pindian sorting must requery ranks after live modifiers change.");
    }

    private static MatchingRecastMaterial Material(int id, CardKind kind) => new(id, kind, CardLocation.Hand(0));

    private static ProgramSelectionContext Context(SkillProgramEffectOp operation, string? categoryLedger = null)
    {
        var condition = new SkillProgramCondition(SkillProgramConditionKind.Always, 0, []);
        var effects = Array.AsReadOnly(new[] { new SkillProgramEffect(operation, SkillProgramEffectTarget.Owner, 0, condition) });
        var activation = new SkillProgramActivation("activate", 0, 3, 0, 3,
            SkillProgramTargetKind.AnyLiving, null, condition, effects) { CategoryTargetLedgerId = categoryLedger };
        var program = new SkillProgram("fixture:selection-transform", 1, "fixture-hash", "fixture-runtime",
            GameCheckpoint.CurrentRulesVersion, [], [], Array.AsReadOnly(new[] { activation }), [], [], []);
        var plan = new ProgramExecutionPlan(program.Id, program.GameplayHash, ProgramInstructionSourceKind.Activation,
            activation.Id, effects, activation: activation);
        return new(0, program, plan);
    }

    private static void RequireReadOnly<T>(IReadOnlyList<T> source, T value, string message)
    {
        Require(source is IList<T> { IsReadOnly: true }, message);
        try { ((IList<T>)source).Add(value); }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Queries : IProgramSelectionQueries, ISelectionParticipantQueries,
        IMatchingRecastSelectionQueries, IPairObtainSelectionQueries, IEquipmentPlacementSelectionQueries,
        ISelectionUsageQueries, IPindianSelectionQueries
    {
        public IReadOnlyList<int> Seats { get; } = Array.AsReadOnly(new[] { 0, 1, 2, 3 });
        internal int[] Hands { get; } = new int[4];
        internal IReadOnlyList<MatchingRecastMaterial> RecastMaterials { get; set; } = Array.AsReadOnly(Array.Empty<MatchingRecastMaterial>());
        internal HashSet<int> ObtainTargets { get; } = [];
        internal string Instance { get; set; } = "first";
        internal int InstanceReads { get; private set; }
        internal List<string> ObtainInstances { get; } = [];
        internal HashSet<(int Card, int Target)> Placements { get; } = [];
        internal List<string> PlacementCalls { get; } = [];
        internal HashSet<(int Card, int Target)> CategoryTargets { get; } = [];
        internal bool NextPindian { get; set; }
        internal Dictionary<int, int> Ranks { get; } = [];
        internal Dictionary<int, int> KeepValues { get; } = [];
        public T Get<T>() where T : class, IProgramSelectionQuery => this is T query ? query : throw new NotSupportedException();
        public bool IsValidSeat(int seat) => seat >= 0 && seat < Hands.Length;
        public bool IsAlive(int seat) => true;
        public int HandCount(int seat) => Hands[seat];
        public int EquipmentCount(int seat) => 0;
        public int Hp(int seat) => 4;
        public IReadOnlyList<MatchingRecastMaterial> Materials(int ownerSeat) => RecastMaterials;
        public string CurrentSkillInstance(int ownerSeat, string skillId) { InstanceReads++; return Instance; }
        public bool CanObtainFrom(int ownerSeat, string skillId, string instanceId, int targetSeat)
        { ObtainInstances.Add(instanceId); return ObtainTargets.Contains(targetSeat); }
        public bool CanPlace(int ownerSeat, int targetSeat, int cardId)
        { PlacementCalls.Add($"{cardId}:{targetSeat}"); return Placements.Contains((cardId, targetSeat)); }
        public bool CanPlaceCaptured(int ownerSeat, int targetSeat, int cardId, string skillId) => Placements.Contains((cardId, targetSeat));
        public bool CanUseDistinctTurnTarget(int ownerSeat, string skillId, string usageId, int targetSeat) => true;
        public bool CanUsePhaseTarget(int ownerSeat, string skillId, string usageId, int targetSeat) => true;
        public bool CanUseCategoryTarget(int ownerSeat, string skillId, string usageId, int cardId, int targetSeat) => CategoryTargets.Contains((cardId, targetSeat));
        public bool HasNextActualUseStructure(SkillProgramActivation activation) => NextPindian;
        public int EffectiveRank(int ownerSeat, int cardId) => Ranks[cardId];
        public int HandKeepValue(int ownerSeat, int cardId) => KeepValues[cardId];
    }
}
