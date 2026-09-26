using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class RuleQueryIntegrationChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string FixtureSkillId = "fixture:engine-rule-query";

    public static void FormalContentPreservesVersionBoundaries()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var expectedVersions = new Dictionary<string, Version>(StringComparer.Ordinal)
        {
            ["standard"] = StandardContentPackage.CurrentVersion,
            ["standard-active-skills"] = StandardActiveSkillExpansionPackage.CurrentVersion,
            ["standard-classic-generals"] = StandardClassicGeneralPackage.CurrentVersion
        };
        Require(expectedVersions.All(expected => current.Packages.Any(package =>
                    package.Id == expected.Key && package.Version == expected.Value)),
            "The current registry must publish the configured package bundle.");

        var expectedLegacyKinds = new Dictionary<string, SkillKind>(StringComparer.Ordinal)
        {
            ["standard:paoxiao"] = SkillKind.Paoxiao,
            ["standard:mashu"] = SkillKind.Mashu,
            ["classic:paoxiao"] = SkillKind.Paoxiao,
            ["classic:mashu"] = SkillKind.Mashu,
            ["sp:guan-yu-mashu"] = SkillKind.Mashu,
            ["classic:yicong"] = SkillKind.Yicong,
            ["classic:zongshi"] = SkillKind.Zongshi
        };
        foreach (var expected in expectedLegacyKinds)
        {
            var currentSkill = current.Skills[expected.Key];
            Require(currentSkill.LegacyKind is null &&
                    currentSkill.Program is
                    {
                        MinimumRulesVersion: 117,
                        RuntimeVersion: "skill-program-v12"
                    } program &&
                    program.Id == expected.Key &&
                    program.Modifiers.Count > 0,
                $"Current skill '{expected.Key}' must be a rules-117 schema-12 program without a legacy executor.");
        }
    }

    public static void EngineTracksDynamicSourcesAndInstanceIdentity()
    {
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(),
            new EngineRuleQueryFixturePackage());
        var game = GameEngine.CreateStandard(new GameOptions { PlayerCount = 5, Seed = 117 }, registry);
        var owner = Players(game)[0];
        ClearGrants(owner);

        Require(game.GetAttackRange(owner.Seat) == 1 && game.GetCombatDistance(owner.Seat, 2) == 2,
            "The engine query fixture must start from unmodified range and distance baselines.");

        owner.SkillGrants.Grant(new SkillGrant("fixture:first-source", FixtureSkillId, "shared-instance", "acquired:first"));
        owner.SkillGrants.Grant(new SkillGrant("fixture:second-source", FixtureSkillId, "shared-instance", "acquired:second"));
        var shared = Evaluate(game, "EvaluateAttackRange", owner);
        Require(Finite(shared).Value == 3 && shared.Value.Contributions.Count == 1,
            "Two grants of the same program instance must contribute once inside the actual engine query.");

        owner.SkillGrants.Grant(new SkillGrant("fixture:distinct-source", FixtureSkillId, "distinct-instance", "acquired:third"));
        var distinct = Evaluate(game, "EvaluateAttackRange", owner);
        var distance = Evaluate(game, "EvaluateDistance", owner, Players(game)[2]);
        Require(Finite(distinct).Value == 5 && distinct.Value.Contributions.Count == 2 &&
                Finite(distance).Value == 1 && distance.Value.Contributions.Count == 2 &&
                distinct.Value.Contributions.Select(item => item.SourceId).Distinct(StringComparer.Ordinal).Count() == 2,
            "Independent program instances must compose for range and distance with distinct stable sources.");

        owner.SkillGrants.SetEnabled("fixture:first-source", false);
        Require(Finite(Evaluate(game, "EvaluateAttackRange", owner)).Value == 5,
            "Disabling one of two grants for the same instance must leave that instance effective.");
        owner.SkillGrants.SetEnabled("fixture:second-source", false);
        Require(Finite(Evaluate(game, "EvaluateAttackRange", owner)).Value == 3,
            "Disabling the final grant for a shared instance must remove only that instance contribution.");
        owner.SkillGrants.RemoveGrant("fixture:distinct-source");
        Require(game.GetAttackRange(owner.Seat) == 1 && game.GetCombatDistance(owner.Seat, 2) == 2,
            "Removing the last enabled instance must restore both engine query baselines.");
    }

    public static void EngineConsumesFormalUnlimitedSlashProgram()
    {
        var registry = StandardContentRegistry.Create();
        var game = GameEngine.CreateStandard(new GameOptions { PlayerCount = 5, Seed = 117 }, registry);
        var owner = Players(game)[0];
        ClearGrants(owner);

        Require(Evaluate(game, "EvaluateSlashUseLimit", owner).Value is FiniteRuleQueryValue { Value: 1 },
            "The engine slash-use query must begin at its finite mode baseline.");
        owner.SkillGrants.Grant(new SkillGrant(
            "fixture:paoxiao", "standard:paoxiao", "fixture:paoxiao-instance", "acquired:test"));
        var enabled = Evaluate(game, "EvaluateSlashUseLimit", owner);
        Require(enabled.Value is UnlimitedRuleQueryValue &&
                enabled.Value.Contributions is [UnlimitedRuleQueryContribution],
            "The current Paoxiao program must produce an explicit unlimited slash-use result in the engine.");
        owner.SkillGrants.SetEnabled("fixture:paoxiao", false);
        Require(Evaluate(game, "EvaluateSlashUseLimit", owner).Value is FiniteRuleQueryValue { Value: 1 },
            "Disabling the Paoxiao grant must restore the finite slash-use limit without stale state.");
    }

    private static CharacterState[] Players(GameEngine game) =>
        ((System.Collections.IEnumerable)typeof(GameEngine).GetField("_players", PrivateInstance)!
            .GetValue(game)!).Cast<CharacterState>().ToArray();

    private static void ClearGrants(CharacterState player)
    {
        foreach (var grant in player.SkillGrants.Grants)
            player.SkillGrants.RemoveGrant(grant.GrantId);
    }

    private static RuleQueryEvaluation Evaluate(GameEngine game, string methodName, params CharacterState[] players)
    {
        var method = typeof(GameEngine).GetMethod(methodName, PrivateInstance)!;
        var arguments = players.Cast<object?>().ToList();
        var parameters = method.GetParameters();
        if (parameters.Length == arguments.Count + 1 && parameters[^1].IsOptional &&
            parameters[^1].ParameterType == typeof(int?))
            arguments.Add(null);
        return (RuleQueryEvaluation)method.Invoke(game, arguments.ToArray())!;
    }

    private static FiniteRuleQueryValue Finite(RuleQueryEvaluation evaluation) =>
        evaluation.Value as FiniteRuleQueryValue ??
        throw new InvalidOperationException("Expected a finite engine rule-query result.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class EngineRuleQueryFixturePackage : IGameContentPackage
    {
        private static readonly SkillProgram Program = SkillProgramCatalog.Load(
            """
            {"schemaVersion":12,"skills":[{"id":"fixture:engine-rule-query","revision":1,
              "minimumRulesVersion":117,"modifiers":[
                {"id":"range-plus-two","query":"attackRange","operation":"add","value":2,"priority":0},
                {"id":"distance-minus-one","query":"outgoingDistance","operation":"add","value":-1,"priority":0}],
              "viewAs":[],"activations":[],"triggers":[],"contributions":[],"cardIdentities":[]}]}
            """,
            """
            {"schemaVersion":1,"skills":{"fixture:engine-rule-query":{"name":"Engine query fixture","description":"Engine query fixture"}}}
            """).Programs[FixtureSkillId];

        public PackageManifest Manifest { get; } = new(
            "engine-rule-query-fixture",
            new Version(1, 0, 0),
            [new PackageDependency("standard-active-skills", new Version(1, 1, 0))]);

        public void Register(IContentRegistryBuilder builder) =>
            builder.AddSkill(new ContentSkillDefinition(FixtureSkillId, "Engine query fixture", "Engine query fixture")
            {
                Program = Program
            });
    }
}
