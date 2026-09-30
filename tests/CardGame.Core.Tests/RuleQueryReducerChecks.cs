using CardGame.Core;

internal static class RuleQueryReducerChecks
{

    public static void DirectionalDistanceRunsBothStagesBeforeClamping()
    {
        var extreme = RuleQueryService.EvaluateDirectionalDistance(
            [new RuleQueryBaseTerm("mode:alive-seat-distance", 2)],
            [new FiniteRuleQueryContribution("outgoing:max", SkillRuleOperation.Add, int.MaxValue)],
            [new FiniteRuleQueryContribution("incoming:cancel", SkillRuleOperation.Add, -int.MaxValue)]);
        Require(Finite(extreme.Value).Value == 2 &&
                extreme.Stages is
                [
                    { Name: "outgoing", InputValue: 2, IsUnlimited: false, OutputValue: (long)int.MaxValue + 2 },
                    { Name: "incoming", InputValue: (long)int.MaxValue + 2, IsUnlimited: false, OutputValue: 2 }
                ],
            "Directional distance must retain wide intermediate values until the incoming stage completes.");

        var staged = RuleQueryService.EvaluateDirectionalDistance(
            [
                new RuleQueryBaseTerm("mode:alive-seat-distance", 2),
                new RuleQueryBaseTerm("equipment:offensive-horse", -1)
            ],
            [new FiniteRuleQueryContribution("skill:mashu", SkillRuleOperation.Add, -1)],
            [new FiniteRuleQueryContribution("skill:yicong", SkillRuleOperation.Set, 5, priority: 10)]);
        Require(Finite(staged.Value).Value == 5 && staged.Stages.Count == 2 &&
                staged.Stages[0].Contributions.Single().SourceId == "skill:mashu" &&
                staged.Stages[1].Contributions.Single().SourceId == "skill:yicong",
            "Distance explanations must preserve outgoing and incoming stage identity instead of flattening Set/Add semantics.");
    }


    public static void StaticSetConflictsAreRejectedBeforePlay()
    {
        var sameValue = LoadPrograms("""
            {"id":"fixture:set-a","revision":1,"minimumRulesVersion": 171,
             "modifiers":[{"id":"set","query":"handLimit","operation":"set","value":5,"priority":10}],
             "viewAs":[],"activations":[],"triggers":[],"contributions":[],"cardIdentities":[]},
            {"id":"fixture:set-b","revision":1,"minimumRulesVersion": 171,
             "modifiers":[{"id":"set","query":"handLimit","operation":"set","value":5,"priority":10}],
             "viewAs":[],"activations":[],"triggers":[],"contributions":[],"cardIdentities":[]},
            {"id":"fixture:set-c","revision":1,"minimumRulesVersion": 171,
             "modifiers":[{"id":"set","query":"handLimit","operation":"set","value":7,"priority":20}],
             "viewAs":[],"activations":[],"triggers":[],"contributions":[],"cardIdentities":[]}
            """);
        SkillProgramRules.ValidateSetModifierConflicts(sameValue);

        var conflict = LoadPrograms("""
            {"id":"fixture:set-left","revision":1,"minimumRulesVersion": 171,
             "modifiers":[{"id":"future-hp","query":"handLimit","operation":"set","value":5,"priority":10,
               "condition":{"kind":"hpAtLeast","value":2}}],
             "viewAs":[],"activations":[],"triggers":[],"contributions":[],"cardIdentities":[]},
            {"id":"fixture:set-right","revision":1,"minimumRulesVersion": 171,
             "modifiers":[{"id":"future-hp","query":"handLimit","operation":"set","value":6,"priority":10,
               "condition":{"kind":"hpAtLeast","value":3}}],
             "viewAs":[],"activations":[],"triggers":[],"contributions":[],"cardIdentities":[]}
            """);
        var exception = Capture<InvalidOperationException>(() =>
            ContentRegistry.Build(new QueryFixturePackage(conflict)));
        Require(exception.Message.Contains("fixture:set-left/future-hp", StringComparison.Ordinal) &&
                exception.Message.Contains("fixture:set-right/future-hp", StringComparison.Ordinal),
            "Registry freeze must reject future conditional Set conflicts with both stable sources.");
    }


    public static void IsIndependentOfInputOrderAndRejectsOnlyWinningSetConflicts()
    {
        RuleQueryContribution[] inputs =
        [
            new FiniteRuleQueryContribution("skill:add", SkillRuleOperation.Add, 4),
            new FiniteRuleQueryContribution("skill:set-a", SkillRuleOperation.Set, 7, priority: 20),
            new FiniteRuleQueryContribution("skill:set-b", SkillRuleOperation.Set, 7, priority: 20),
            new FiniteRuleQueryContribution("legacy:low-a", SkillRuleOperation.Set, 1, priority: 10),
            new FiniteRuleQueryContribution("legacy:low-b", SkillRuleOperation.Set, 2, priority: 10)
        ];

        var forward = Finite(RuleQueryReducer.Reduce(3, new(0, 20), inputs));
        var reverse = Finite(RuleQueryReducer.Reduce(3, new(0, 20), inputs.Reverse().ToArray()));
        Require(forward.Value == 11 && reverse.Value == 11,
            "The winning Set and all Add contributions must be independent of input order.");
        Require(forward.Contributions.Select(item => item.SourceId).SequenceEqual(
                reverse.Contributions.Select(item => item.SourceId)),
            "Published contribution order must be stable.");

        var conflict = new RuleQueryContribution[]
        {
            new FiniteRuleQueryContribution("set:z", SkillRuleOperation.Set, 8, 20),
            new FiniteRuleQueryContribution("set:a", SkillRuleOperation.Set, 7, 20)
        };
        var first = Capture<InvalidOperationException>(() =>
            RuleQueryReducer.Reduce(0, new(0, 20), conflict));
        var second = Capture<InvalidOperationException>(() =>
            RuleQueryReducer.Reduce(0, new(0, 20), conflict.Reverse().ToArray()));
        Require(first.Message == second.Message && first.Message.Contains("set:a=7, set:z=8", StringComparison.Ordinal),
            "A winning Set conflict must be deterministic and identify sorted sources.");
    }



    public static void RejectsInvalidInputsAndFreezesOutput()
    {
        Throws<ArgumentNullException>(() => RuleQueryReducer.Reduce(0, new(0, 1), null!));
        Throws<ArgumentException>(() => RuleQueryReducer.Reduce(0, new(2, 1), []));
        Throws<ArgumentException>(() => RuleQueryReducer.Reduce(0, new(0, 1),
            [new FiniteRuleQueryContribution(" ", SkillRuleOperation.Add, 1)]));
        Throws<ArgumentException>(() => RuleQueryReducer.Reduce(0, new(0, 1),
            [new FiniteRuleQueryContribution(" untrimmed", SkillRuleOperation.Add, 1)]));
        Throws<ArgumentException>(() => RuleQueryReducer.Reduce(0, new(0, 1),
            [null!]));
        Throws<ArgumentException>(() => RuleQueryReducer.Reduce(0, new(0, 1),
        [
            new FiniteRuleQueryContribution("same", SkillRuleOperation.Add, 1),
            new UnlimitedRuleQueryContribution("same")
        ]));
        Throws<ArgumentOutOfRangeException>(() => RuleQueryReducer.Reduce(0, new(0, 1),
            [new FiniteRuleQueryContribution("unknown", (SkillRuleOperation)999, 1)]));
        Throws<ArgumentOutOfRangeException>(() => RuleQueryReducer.Reduce(0, new(0, 1),
            [new FiniteRuleQueryContribution("wrong-type", SkillRuleOperation.Unlimited, 1)]));

        var mutable = new List<RuleQueryContribution>
        {
            new FiniteRuleQueryContribution("add", SkillRuleOperation.Add, 1)
        };
        var result = Finite(RuleQueryReducer.Reduce(0, new(0, 2), mutable));
        mutable.Clear();
        Require(result.Contributions.Count == 1 && result.Value == 1,
            "The result must own an immutable snapshot of caller input.");
        Throws<NotSupportedException>(() =>
            ((ICollection<RuleQueryContribution>)result.Contributions).Clear());
    }


    private static FiniteRuleQueryValue Finite(RuleQueryValue value) => value as FiniteRuleQueryValue ??
        throw new InvalidOperationException("Expected a finite query value.");

    private static IReadOnlyList<SkillProgram> LoadPrograms(string skills)
    {
        var ids = System.Text.RegularExpressions.Regex.Matches(skills, "\\\"id\\\":\\\"(fixture:[^\\\"]+)\\\"")
            .Select(match => match.Groups[1].Value)
            .Where(id => !id.Contains("/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var presentation = "{\"schemaVersion\":3,\"skills\":{" + string.Join(",", ids.Select(id =>
            $"\"{id}\":{{\"name\":\"Fixture\",\"description\":\"Fixture\"}}")) + "}}";
        var catalog = SkillProgramCatalog.Load(
            $"{{\"schemaVersion\":{SkillProgramCatalog.RulesSchemaVersion},\"skills\":[{skills}]}}",
            presentation);
        return catalog.Programs.Values.OrderBy(program => program.Id, StringComparer.Ordinal).ToArray();
    }

    private static TException Capture<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Throws<TException>(Action action) where TException : Exception =>
        _ = Capture<TException>(action);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class QueryFixturePackage(IReadOnlyList<SkillProgram> programs) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("query-fixture", new Version(1, 0, 0));

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var program in programs)
                builder.AddSkill(new ContentSkillDefinition(program.Id, "Fixture", "Fixture")
                {
                    Program = program
                });
        }
    }
}
