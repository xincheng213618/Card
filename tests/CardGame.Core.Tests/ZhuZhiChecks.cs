using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhuZhiChecks
{
    private const string GeneralId = "classic:zhu-zhi";
    private const string SkillId = "classic:anguo";
    private const string SyntheticSkillId = "fixture:coverage-discard";
    private const string SyntheticGeneralId = "fixture:coverage-owner";
    private const string RulesResource = "CardGame.Content.Standard.SkillPrograms.classic-zhu-zhi.rules.json";
    private const string PresentationResource = "CardGame.Content.Standard.SkillPrograms.classic-zhu-zhi.presentation.json";

    public static void DefinitionAndResourceContracts()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var general = registry.Generals[GeneralId];
        var skill = registry.Skills[SkillId];
        Require(general is { Name: "朱治", FactionId: "wu", BaseHp: 4, PortraitKey: "zhu_zhi" } &&
                general.SkillIds.SequenceEqual([SkillId]) &&
                registry.Modes["identity:classic-5"].GeneralPoolIds!.Contains(GeneralId) &&
                registry.Modes["identity:classic-8"].GeneralPoolIds!.Contains(GeneralId) &&
                skill.Program is { RuntimeVersion: SkillProgramCatalog.RuntimeVersion, MinimumRulesVersion: 172 } &&
                skill.Program.Activations.Single() is { UsesPerPhase: 1, MinTargets: 0, MaxTargets: 0 } &&
                (int)SkillProgramConditionKind.AttackRangeCoverageDecreased == 22,
            "Zhu Zhi must be a formal Wu/Fame V general with phase-limited schema-55 Anguo.");
        var rules = Resource(RulesResource);
        var presentation = Resource(PresentationResource);
        Reject(rules.Replace("\"sourceBind\": \"range-change\"", "\"sourceBind\": \"future\"", StringComparison.Ordinal),
            presentation, "unknown attack-range coverage");
        Reject(rules.Replace("\"allowSameOwnerHandReturn\": true", "\"allowSameOwnerHandReturn\": false", StringComparison.Ordinal)
                .Replace("\"zones\": [\"equipment\"]", "\"zones\": [\"hand\"]", StringComparison.Ordinal),
            presentation, "coverageResultBind requires equipment");
        Reject(rules.Replace("\"zones\": [\"equipment\"]", "\"zones\": [\"hand\"]", StringComparison.Ordinal)
                .Replace("\"coverageResultBind\": \"range-change\",", "", StringComparison.Ordinal),
            presentation, "public non-hand");
        Reject(rules.Replace("\"coverageResultBind\": \"range-change\",",
                    "\"coverageResultBind\": \"range-change\", \"skipIfNoCards\": true,", StringComparison.Ordinal),
            presentation, "unconditional non-skipping");
        Reject(rules.Replace("\"coverageResultBind\": \"range-change\",",
                    "\"coverageResultBind\": \"range-change\", \"condition\": { \"kind\": \"wounded\" },",
                    StringComparison.Ordinal),
            presentation, "unconditional non-skipping");
    }

    public static void AnguoReturnsWeaponAndReplays()
    {
        var (game, registry) = Create("classic:qilin-bow");
        Require(game.GetHumanLegalActions().All(action => action.ProgramSkillId != SkillId),
            "Anguo must be absent before any other living player has equipment.");
        ReachNextPlayAfterAiEquips(game);
        var targets = game.CreateCardZoneDiagnostics()
            .Where(card => card.Location.Zone == CardZoneKind.Equipment && card.Location.OwnerSeat is > 0)
            .Select(card => card.Location.OwnerSeat!.Value).Distinct().ToArray();
        Require(targets.Length > 0, "AI fixture failed to equip a public weapon.");
        var selectedSeat = targets.First(seat => game.GetAttackRange(seat) > 1);
        var coverageBefore = CountCoverage(game, selectedSeat);
        Require(coverageBefore > 2, "Fixture weapon must cover the opposite living seat.");
        var action = game.GetHumanLegalActions().Single(item => item.ProgramSkillId == SkillId);
        Accept(game.Submit(new UseProgramSkillCommand(0, SkillId, "return-equipment", [], [],
            game.Revision, game.PendingDecision!.PromptId)));
        var targetPrompt = RequirePrompt(game);
        Require(targetPrompt.Choices.Any(choice => choice.Targets.SequenceEqual([selectedSeat])) &&
                targetPrompt.Choices.All(choice => targets.Contains(choice.Targets.Single())),
            "Anguo target choices must contain only equipped other living players.");
        var atTarget = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        AnswerTarget(game, selectedSeat);
        AnswerTarget(atTarget, selectedSeat);
        Require(State(game) == State(atTarget), "Target-choice checkpoint diverged.");
        var equipment = RequirePrompt(game);
        Require(equipment.IsPrivate && equipment.Choices.Count > 0 &&
                equipment.Choices.All(choice => choice.Parameters.GetValueOrDefault("source-zone") == "Equipment"),
            "Anguo must select an actual public equipment card through its private command prompt.");
        var atEquipment = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        var chosen = equipment.Choices.First();
        Answer(game, chosen);
        Answer(atTarget, RequirePrompt(atTarget).Choices.First());
        Answer(atEquipment, RequirePrompt(atEquipment).Choices.First());
        var moveReason = $"skill-program.{SkillId}.SelectAndMoveOwnedCard";
        Require(game.CardMovements.Count(move => move.Reason.Value == moveReason &&
                    move.From == CardLocation.Equipment(selectedSeat) && move.To == CardLocation.Processing) == 1 &&
                game.CardMovements.Count(move => move.Reason.Value == moveReason &&
                    move.From == CardLocation.Processing && move.To == CardLocation.Hand(selectedSeat)) == 1 &&
                CountCoverage(game, selectedSeat) < coverageBefore &&
                game.CardMovements.Count(move => move.Reason.Value == $"skill-program.{SkillId}.Draw") == 1 &&
                State(game) == State(atTarget) && State(game) == State(atEquipment) &&
                Events(game).SequenceEqual(Events(atEquipment)),
            "Removing a coverage-changing public weapon must return it to its owner, then draw exactly one with replay.");
        Require(game.GetHumanLegalActions().All(item => item.ProgramSkillId != SkillId),
            "Anguo must not be usable twice in one Play phase.");
    }

    private static int CountCoverage(GameEngine game, int seat) =>
        Enumerable.Range(0, 4).Count(target => target != seat &&
            game.State.Players[target].IsAlive && game.GetCombatDistance(seat, target) <= game.GetAttackRange(seat));

    private static (GameEngine Game, ContentRegistry Registry) Create(string equipmentCardId,
        bool xiaojiTargets = false, bool rangeRescueTargets = false,
        bool rangePlusTwoTargets = false, bool syntheticDiscardOwner = false,
        bool lethalLossTargets = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
            new Scenario(equipmentCardId, xiaojiTargets, rangeRescueTargets,
                rangePlusTwoTargets, syntheticDiscardOwner, lethalLossTargets));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = Scenario.ModeId, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0,
            syntheticDiscardOwner ? SyntheticGeneralId : GeneralId,
            game.Revision, game.PendingDecision!.PromptId)));
        ReachPlay(game);
        return (game, registry);
    }

    private static void ReachNextPlayAfterAiEquips(GameEngine game)
    {
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        for (var i = 0; i < 600 &&
             !(game.PendingDecision?.Kind == DecisionKind.PlayCard && game.State.TurnNumber > 1); i++)
            Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard && game.State.TurnNumber > 1,
            $"Fixture did not return to Zhu Zhi Play: turn={game.State.TurnNumber}, pending={game.PendingDecision?.Kind}.");
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var i = 0; i < 128 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++) Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Fixture did not reach Play.");
    }
    private static void AnswerTarget(GameEngine game, int seat) =>
        Answer(game, RequirePrompt(game).Choices.Single(choice => choice.Targets.SequenceEqual([seat])));
    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(
        new AnswerPromptCommand(game.PendingDecision!.PlayerSeat, game.PendingDecision.PromptId,
            choice.Id, game.Revision)));
    private static PendingDecision RequirePrompt(GameEngine game) => game.PendingDecision is
        { Kind: DecisionKind.ProgramTrigger } prompt ? prompt :
        throw new InvalidOperationException($"Expected program prompt, got {game.PendingDecision?.Kind}.");
    private static void Advance(GameEngine game) => Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
    private static IReadOnlyList<string> Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static string Resource(string name)
    {
        using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing resource {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    private static void Reject(string rules, string presentation, string fragment)
    {
        try { _ = SkillProgramCatalog.Load(rules, presentation); }
        catch (InvalidOperationException error) when (error.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase))
        { return; }
        throw new InvalidOperationException($"Expected definition rejection containing '{fragment}'.");
    }
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Rejected command.");
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario(string equipmentCardId, bool xiaojiTargets,
        bool rangeRescueTargets, bool rangePlusTwoTargets, bool syntheticDiscardOwner,
        bool lethalLossTargets) : IGameContentPackage
    {
        public const string ModeId = "identity:classic-zhu-zhi-check-4";
        public PackageManifest Manifest { get; } = new("zhu-zhi-scenario", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            if (syntheticDiscardOwner)
            {
                var rules = Resource(RulesResource)
                    .Replace(SkillId, SyntheticSkillId, StringComparison.Ordinal)
                    .Replace("\"destination\": \"selectedTargetHand\",", "\"destination\": \"discardPile\",", StringComparison.Ordinal)
                    .Replace("\"targetRef\": { \"kind\": \"selectedTarget\" },", "", StringComparison.Ordinal)
                    .Replace("\"allowSameOwnerHandReturn\": true,", "", StringComparison.Ordinal)
                    .Replace("\"op\": \"draw\"", "\"op\": \"recover\"", StringComparison.Ordinal);
                var catalog = SkillProgramCatalog.Load(rules,
                    Resource(PresentationResource).Replace(SkillId, SyntheticSkillId, StringComparison.Ordinal));
                builder.AddSkill(new ContentSkillDefinition(SyntheticSkillId, "范围回复", "公共绑定测试")
                { Program = catalog.Programs[SyntheticSkillId],
                    ProgramPresentation = catalog.Presentations[SyntheticSkillId] });
                builder.AddGeneral(new ContentGeneralDefinition(SyntheticGeneralId, "公共范围测试",
                    "coverage_owner", SyntheticSkillId, "wu", BaseHp: 4));
            }
            if (rangeRescueTargets)
            {
                const string rules = """
                {"schemaVersion":62,"skills":[{"id":"fixture:range-rescue","revision":1,
                 "minimumRulesVersion": 171,"triggers":[{"id":"restore-public-range",
                 "window":"cardsMoved","subject":"owner","sourceZones":["equipment"],
                 "movementOccurrence":"perBatch","optional":false,"priority":0,
                 "effects":[{"op":"grantTurnRuleModifier","target":"owner",
                 "ruleQuery":"attackRange","ruleOperation":"unlimited"}]}]}]}
                """;
                const string presentation = """
                {"schemaVersion":3,"skills":{"fixture:range-rescue":{"name":"范围响应","description":"装备离开后，攻击范围改为无限。"}}}
                """;
                var catalog = SkillProgramCatalog.Load(rules, presentation);
                builder.AddSkill(new ContentSkillDefinition("fixture:range-rescue", "范围响应", "测试公共失装响应")
                { Program = catalog.Programs["fixture:range-rescue"],
                    ProgramPresentation = catalog.Presentations["fixture:range-rescue"] });
            }
            if (rangePlusTwoTargets)
            {
                const string rules = """
                {"schemaVersion":62,"skills":[{"id":"fixture:range-plus-two","revision":1,
                 "minimumRulesVersion": 171,"modifiers":[{"id":"public-plus-two",
                 "query":"attackRange","operation":"add","value":2,"priority":0}]}]}
                """;
                const string presentation = """
                {"schemaVersion":3,"skills":{"fixture:range-plus-two":{"name":"范围加二","description":"攻击范围加二。"}}}
                """;
                var catalog = SkillProgramCatalog.Load(rules, presentation);
                builder.AddSkill(new ContentSkillDefinition("fixture:range-plus-two", "范围加二", "测试公开范围")
                { Program = catalog.Programs["fixture:range-plus-two"],
                    ProgramPresentation = catalog.Presentations["fixture:range-plus-two"] });
            }
            if (lethalLossTargets)
            {
                const string rules = """
                {"schemaVersion":62,"skills":[{"id":"fixture:range-lethal","revision":1,
                 "minimumRulesVersion": 171,"triggers":[{"id":"lose-life-on-equipment-loss",
                 "window":"cardsMoved","subject":"owner","sourceZones":["equipment"],
                 "movementOccurrence":"perBatch","optional":false,"priority":0,
                 "effects":[{"op":"loseHp","target":"owner","amount":1}]}]}]}
                """;
                const string presentation = """
                {"schemaVersion":3,"skills":{"fixture:range-lethal":{"name":"致命失装","description":"装备离开后失去一点体力。"}}}
                """;
                var catalog = SkillProgramCatalog.Load(rules, presentation);
                builder.AddSkill(new ContentSkillDefinition("fixture:range-lethal", "致命失装", "测试嵌套死亡")
                { Program = catalog.Programs["fixture:range-lethal"],
                    ProgramPresentation = catalog.Presentations["fixture:range-lethal"] });
            }
            var targets = new[] { "fixture:zhu-zhi-1", "fixture:zhu-zhi-2", "fixture:zhu-zhi-3" };
            foreach (var id in targets)
                builder.AddGeneral(new ContentGeneralDefinition(id, "测试目标", "supporter",
                    lethalLossTargets ? "fixture:range-lethal" :
                    rangePlusTwoTargets ? "fixture:range-plus-two" :
                    rangeRescueTargets ? "fixture:range-rescue" :
                    xiaojiTargets ? "classic:xiaoji" : "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:zhu-zhi-deck", "朱治测试牌堆", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 120)
                    .Select(index => new ContentDeckPhysicalCard(
                        (xiaojiTargets || rangeRescueTargets || lethalLossTargets) && index >= 12
                            ? "standard:dodge" : equipmentCardId,
                        (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new ContentModeDefinition(ModeId, "朱治场景", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:zhu-zhi-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [syntheticDiscardOwner ? SyntheticGeneralId : GeneralId, .. targets]));
        }
    }
}
