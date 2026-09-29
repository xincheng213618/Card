using CardGame.Content.Standard;
using CardGame.Core;

internal static class SpGuanYuChecks
{
    private const string GeneralId = "sp:guan-yu";
    private const string WushengSkillId = "sp:guan-yu-wusheng";
    private const string DanjiSkillId = "sp:danji";
    private const string MashuSkillId = "sp:guan-yu-mashu";
    private const string NuzhanSkillId = "sp:nuzhan";
    private const string ValidationRules =
        """{"schemaVersion":62,"skills":[{"id":"fixture:awakening","revision":1,"minimumRulesVersion": 171,"modifiers":[],"viewAs":[],"activations":[],"triggers":[{"id":"awakening","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"condition":{"kind":"all","children":[{"kind":"compare","left":{"kind":"currentHandCount"},"operator":"greaterThan","right":{"kind":"currentHp"}},{"kind":"lordGeneralNotIn","generalIds":["classic:liu-bei"]}]},"effects":[{"op":"changeMaximumHp","target":"owner","amount":-1},{"op":"grantSkills","target":"owner","skillIds":["sp:guan-yu-mashu","sp:nuzhan"]}]}],"contributions":[],"cardIdentities":[]}]}""";
    private const string ValidationPresentation =
        """{"schemaVersion":3,"skills":{"fixture:awakening":{"name":"觉醒夹具","description":"验证通用觉醒状态节点。"}}}""";

    public static void ProgramPrimitivesAndConditionsValidate()
    {
        var program = SkillProgramCatalog.Load(ValidationRules, ValidationPresentation)
            .Programs["fixture:awakening"];
        var trigger = program.Triggers.Single();
        Require(program is { RuntimeVersion: SkillProgramCatalog.RuntimeVersion, MinimumRulesVersion: 171 } &&
                trigger.Condition.Evaluate(new SkillProgramTriggerFacts(
                    0, 3, true, CurrentHandCount: 4, LordGeneralId: "classic:cao-cao")) &&
                !trigger.Condition.Evaluate(new SkillProgramTriggerFacts(
                    0, 3, true, CurrentHandCount: 3, LordGeneralId: "classic:cao-cao")) &&
                !trigger.Condition.Evaluate(new SkillProgramTriggerFacts(
                    0, 3, true, CurrentHandCount: 4, LordGeneralId: "classic:liu-bei")),
            "Schema 25 must compose frozen hand-count and Lord-general facts without a Danji-specific predicate.");

        Reject(
            ValidationRules.Replace(
                "\"sp:guan-yu-mashu\",\"sp:nuzhan\"",
                "\"sp:nuzhan\",\"sp:nuzhan\"",
                StringComparison.Ordinal),
            "duplicate values");
        Reject(
            ValidationRules.Replace(
                "\"generalIds\":[\"classic:liu-bei\"]",
                "\"generalIds\":[]",
                StringComparison.Ordinal),
            "must not be empty");
        Reject(
            ValidationRules.Replace("\"amount\":-1", "\"amount\":0", StringComparison.Ordinal),
            "non-zero value");

        try
        {
            _ = ContentRegistry.Build(new MissingGrantPackage(program));
            throw new InvalidOperationException("A grant to unknown content skills was accepted.");
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("grants unknown skill", StringComparison.Ordinal))
        {
        }
    }

    private static GameEngine CreateGame(ContentRegistry registry) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17,
            PlayerCount = 4,
            ModeId = ScenarioPackage.ModeId,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 40
        }, registry);

    private static ContentRegistry CreateRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reject(string rules, string expectedMessage)
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, ValidationPresentation);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new InvalidOperationException($"Expected schema rejection containing '{expectedMessage}'.");
    }

    private sealed class MissingGrantPackage(SkillProgram program) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new(
            "missing-awakening-grant-fixture",
            new Version(1, 0, 0),
            []);

        public void Register(IContentRegistryBuilder builder) =>
            builder.AddSkill(new ContentSkillDefinition(
                program.Id,
                "觉醒夹具",
                "验证未知授予技能。")
            {
                Program = program,
                Tags = SkillTag.Awakening,
                ExecutionForms = SkillExecutionForm.Trigger
            });
    }

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-sp-guan-yu-test-4";
        private const string DeckId = "fixture:sp-guan-yu-deck";
        private static readonly string[] BlankGeneralIds =
            ["fixture:blank-guan-yu-1", "fixture:blank-guan-yu-2", "fixture:blank-guan-yu-3"];

        public PackageManifest Manifest { get; } = new(
            "sp-guan-yu-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 69, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var (id, index) in BlankGeneralIds.Select((id, index) => (id, index)))
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    $"怒斩目标{index + 1}",
                    "supporter",
                    "standard:none",
                    "qun",
                    BaseHp: 4));
            }

            var cards = new[]
            {
                (Id: "standard:draw_two", Suit: Suit.Heart),
                (Id: "standard:qinggang_sword", Suit: Suit.Heart),
                (Id: "standard:peach", Suit: Suit.Heart),
                (Id: "standard:slash", Suit: Suit.Diamond),
                (Id: "standard:draw_two", Suit: Suit.Diamond)
            };
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "SP关羽觉醒与怒斩测试牌堆",
                InitialHandSize: 8,
                DrawPerTurn: 0,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(0, 64)
                    .Select(index => cards[index % cards.Length])
                    .Select((card, index) => new ContentDeckPhysicalCard(
                        card.Id,
                        card.Suit,
                        index % 13 + 1))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "SP关羽觉醒与怒斩测试",
                4,
                4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 4,
                GeneralPoolIds: [GeneralId, .. BlankGeneralIds]));
        }
    }
}
