using CardGame.Core;

internal static class RuleQueryReducerChecks
{
    public static void BaseTermsPrecedeSetsAndRemainInspectable()
    {
        RuleQueryBaseTerm[] baseTerms =
        [
            new("mode:hand-limit", 3),
            new("skill:xueyi:baseline", 4),
            new("equipment:jade-seal:draw", 1)
        ];
        RuleQueryContribution[] modifiers =
        [
            new FiniteRuleQueryContribution("skill:set", SkillRuleOperation.Set, 5, priority: 20),
            new FiniteRuleQueryContribution("state:add", SkillRuleOperation.Add, 2)
        ];
        var evaluated = RuleQueryService.Evaluate(
            SkillRuleQuery.HandLimit,
            new(0, 20),
            baseTerms,
            modifiers);
        Require(Finite(evaluated.Value).Value == 7,
            "A Set must replace the complete pre-Set baseline before post-Set Add contributions run.");
        Require(evaluated.BaseTerms.Select(item => item.SourceId).SequenceEqual(
                baseTerms.Select(item => item.SourceId).Order(StringComparer.Ordinal)),
            "Pre-Set mode, equipment and compatibility sources must remain inspectable in stable order.");
    }

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

    public static void ProgramContributionsUseInstanceIdentityAndDynamicValues()
    {
        var program = LoadPrograms("""
            {"id":"fixture:zongshi","revision":1,"minimumRulesVersion": 171,
             "modifiers":[{"id":"living-factions","query":"handLimit","operation":"add",
               "valueExpression":"livingFactionCount","priority":0}],
             "viewAs":[],"activations":[],"triggers":[],"contributions":[],"cardIdentities":[]}
            """).Single();
        var context = new SkillProgramRuleContext(
            new PlayerSkillContext(2, 3, 4, 5, TurnPhase.Play, IsOwnTurn: true),
            LivingFactionCount: 3);
        var contributions = SkillProgramRules.CollectContributions(
            SkillRuleQuery.HandLimit,
            context,
            [
                new(program.Id, "instance-a", program),
                new(program.Id, "instance-a", program),
                new(program.Id, "instance-b", program)
            ]);
        Require(contributions.Count == 2 &&
                contributions.OfType<FiniteRuleQueryContribution>().All(item => item.Value == 3) &&
                contributions.Select(item => item.SourceId).Distinct(StringComparer.Ordinal).Count() == 2 &&
                contributions.All(item => item.SourceId.Contains(program.Id, StringComparison.Ordinal)),
            "The same skill instance must not repeat across grants, while independent instances retain dynamic values.");

        var ambiguousSegments = LoadPrograms("""
            {"id":"fixture:a","revision":1,"minimumRulesVersion": 171,
             "modifiers":[{"id":"d","query":"handLimit","operation":"add","value":1,"priority":0}],
             "viewAs":[],"activations":[],"triggers":[],"contributions":[],"cardIdentities":[]},
            {"id":"fixture:a:b","revision":1,"minimumRulesVersion": 171,
             "modifiers":[{"id":"d","query":"handLimit","operation":"add","value":1,"priority":0}],
             "viewAs":[],"activations":[],"triggers":[],"contributions":[],"cardIdentities":[]}
            """).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var ambiguousSources = new[]
        {
            new SkillProgramRuleSource("fixture:a:b", "c", ambiguousSegments["fixture:a:b"]),
            new SkillProgramRuleSource("fixture:a", "b:c", ambiguousSegments["fixture:a"])
        };
        var ownerTwo = SkillProgramRules.CollectContributions(
            SkillRuleQuery.HandLimit,
            context,
            ambiguousSources);
        var ownerTwoAgain = SkillProgramRules.CollectContributions(
            SkillRuleQuery.HandLimit,
            context,
            ambiguousSources);
        var ownerThree = SkillProgramRules.CollectContributions(
            SkillRuleQuery.HandLimit,
            context with { Owner = context.Owner with { Seat = 3 } },
            ambiguousSources);
        Require(ownerTwo.Select(item => item.SourceId).Distinct(StringComparer.Ordinal).Count() == 2 &&
                ownerTwo.Select(item => item.SourceId).SequenceEqual(
                    ownerTwoAgain.Select(item => item.SourceId)) &&
                !ownerTwo.Select(item => item.SourceId).Intersect(
                    ownerThree.Select(item => item.SourceId), StringComparer.Ordinal).Any(),
            "Length-prefixed source identity must separate colon-bearing fields, stay stable and include the owner seat.");
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

    public static void SchemaTwelveRequiresExplicitModifierIdentityAndPriority()
    {
        Throws<InvalidOperationException>(() => LoadPrograms("""
            {"id":"fixture:missing-priority","revision":1,"minimumRulesVersion": 171,
             "modifiers":[{"id":"add","query":"handLimit","operation":"add","value":1}],
             "viewAs":[],"activations":[],"triggers":[],"contributions":[],"cardIdentities":[]}
            """));
        Throws<InvalidOperationException>(() => LoadPrograms("""
            {"id":"fixture:add-priority","revision":1,"minimumRulesVersion": 171,
             "modifiers":[{"id":"add","query":"handLimit","operation":"add","value":1,"priority":1}],
             "viewAs":[],"activations":[],"triggers":[],"contributions":[],"cardIdentities":[]}
            """));
        Throws<InvalidOperationException>(() => LoadPrograms("""
            {"id":"fixture:bad-unlimited","revision":1,"minimumRulesVersion": 171,
             "modifiers":[{"id":"unlimited","query":"handLimit","operation":"unlimited","value":0,"priority":0}],
             "viewAs":[],"activations":[],"triggers":[],"contributions":[],"cardIdentities":[]}
            """));
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

    public static void SumsBeforeClampingAndHandlesIntegerExtremes()
    {
        RuleQueryContribution[] cancellation =
        [
            new FiniteRuleQueryContribution("add:max", SkillRuleOperation.Add, int.MaxValue),
            new FiniteRuleQueryContribution("add:min", SkillRuleOperation.Add, int.MinValue)
        ];
        Require(Finite(RuleQueryReducer.Reduce(10, new(0, 100), cancellation)).Value == 9,
            "Adds must cancel in a wide accumulator before one final clamp.");

        RuleQueryContribution[] saturation =
        [
            new FiniteRuleQueryContribution("add:max-a", SkillRuleOperation.Add, int.MaxValue),
            new FiniteRuleQueryContribution("add:max-b", SkillRuleOperation.Add, int.MaxValue)
        ];
        Require(Finite(RuleQueryReducer.Reduce(int.MaxValue, new(-5, 50), saturation)).Value == 50,
            "Extreme finite totals must clamp without overflowing.");
        Require(Finite(RuleQueryReducer.Reduce(-100, new(0, 20), [])).Value == 0,
            "The base value participates in the same final clamp.");
        Throws<OverflowException>(() => RuleQueryReducer.Reduce(long.MaxValue, new(0, 50),
            [new FiniteRuleQueryContribution("add:one", SkillRuleOperation.Add, 1)]));
    }

    public static void KeepsUnlimitedSeparateAndStillValidatesFiniteConflicts()
    {
        RuleQueryContribution[] inputs =
        [
            new FiniteRuleQueryContribution("add:max", SkillRuleOperation.Add, int.MaxValue),
            new UnlimitedRuleQueryContribution("skill:unlimited")
        ];
        var result = RuleQueryReducer.Reduce(int.MaxValue, new(0, 9), inputs);
        Require(result is UnlimitedRuleQueryValue && result.IsUnlimited,
            "Unlimited must remain a distinct result and never enter finite arithmetic.");
        var evaluated = RuleQueryService.Evaluate(
            SkillRuleQuery.SlashLimit,
            new(0, int.MaxValue),
            [new RuleQueryBaseTerm("mode:base", 1)],
            inputs);
        Require(evaluated.Stages.Single() is { IsUnlimited: true, OutputValue: null },
            "Stage explanations must represent unlimited explicitly instead of exposing a finite zero placeholder.");

        Throws<InvalidOperationException>(() => RuleQueryReducer.Reduce(0, new(0, 9),
        [
            new UnlimitedRuleQueryContribution("skill:unlimited"),
            new FiniteRuleQueryContribution("set:a", SkillRuleOperation.Set, 1, 5),
            new FiniteRuleQueryContribution("set:b", SkillRuleOperation.Set, 2, 5)
        ]));
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

    public static void HasNoConcreteSkillDependency()
    {
        var referenced = typeof(RuleQueryReducer).Assembly.GetReferencedAssemblies()
            .Select(name => name.Name).ToArray();
        Require(!referenced.Any(name => name is "CardGame.Content.Standard" or "CardGame.Wpf"),
            "The reducer's assembly must not depend on concrete skill content or UI.");
        Require(typeof(RuleQueryReducer).GetFields(
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Length == 0,
            "The pure reducer must not retain game or caller state.");
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
