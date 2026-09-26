using CardGame.Content.Standard;
using CardGame.Core;

internal static class SkillOwnershipChecks
{
    public static void PrintedLordSkillsFollowIdentityAndReplayBoundary()
    {
        Require(GameCheckpoint.CurrentRulesVersion >= 97,
            "Structured printed-skill ownership requires rules v97 or newer.");
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(registry.Generals["classic:sun-quan"].SkillIds
                .SequenceEqual(["classic:zhiheng", "classic:jiuyuan"]),
            "The immutable Sun Quan content definition must retain both printed skills.");

        var rebel = SelectClassicGeneral(registry, "classic:sun-quan", Role.Rebel);
        var rebelSnapshot = Human(rebel);
        Require(rebelSnapshot.Role == Role.Rebel &&
                rebelSnapshot.Skills?.Select(skill => skill.ContentId)
                    .SequenceEqual(["classic:zhiheng"]) == true,
            "A current non-Lord must own Zhiheng but not the printed Lord skill Jiuyuan.");

        var lord = SelectClassicGeneral(registry, "classic:sun-quan", Role.Lord);
        Require(Human(lord).Skills?.Select(skill => skill.ContentId)
                    .SequenceEqual(["classic:zhiheng", "classic:jiuyuan"]) == true,
            "A current Lord must retain every printed Sun Quan skill in stable order.");
    }

    public static void LordTagFiltersGenericRuntimeDiscovery()
    {
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new LordTaggedActiveFixture());
        var current = CreateTeamGame(registry);
        ReachHumanPlay(current);
        var currentActions = current.GetHumanLegalActions();
        Require(Human(current).Role != Role.Lord &&
                currentActions.All(action => action.Skill != SkillKind.Zhiheng) &&
                currentActions.All(action => action.ProgramSkillId != LordTaggedActiveFixture.ProgramSkillId) &&
                currentActions.All(action => action.ConversionSource?.SkillId != LordTaggedActiveFixture.LongdanSkillId),
            "Current rules must remove Lord-tagged printed skills before passive, program and conversion discovery.");
    }

    private static PlayerSnapshot Human(GameEngine game) =>
        game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.IsHuman);

    private static GameEngine SelectClassicGeneral(
        ContentRegistry registry,
        string generalId,
        Role role)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = 0,
                HumanRole = role,
                ModeId = "identity:classic-5",
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 220
            }, registry);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Classic ownership fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual([generalId])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                generalId,
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? $"Could not select {generalId}.");
            return game;
        }

        throw new InvalidOperationException($"No bounded {role} fixture offered {generalId}.");
    }

    private static GameEngine CreateTeamGame(ContentRegistry registry) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1,
            PlayerCount = 4,
            HumanSeat = 0,
            HumanRole = null,
            HumanTeamId = "team:blue",
            ModeId = LordTaggedActiveFixture.ModeId,
            UseInteractiveSetup = false,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 20
        }, registry);

    private static void ReachHumanPlay(GameEngine game)
    {
        var started = game.Submit(new StartGameCommand());
        Require(started.Accepted, started.Error?.Message ?? "Lord-tagged runtime fixture failed to start.");
        for (var step = 0; step < 20 &&
             game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
        {
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Lord-tagged runtime fixture failed to advance.");
        }

        Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "Lord-tagged runtime fixture did not reach the human play boundary.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class LordTaggedActiveFixture : IGameContentPackage
    {
        public const string ModeId = "team:lord-tagged-active-2v2";
        public const string ProgramSkillId = "fixture:lord-program";
        public const string LongdanSkillId = "fixture:lord-longdan";
        private const string SkillId = "fixture:lord-zhiheng";
        private const string DeckId = "fixture:lord-skills-deck";
        private static readonly string[] GeneralIds = Enumerable.Range(0, 4)
            .Select(index => $"fixture:lord-active-{index}")
            .ToArray();

        public PackageManifest Manifest { get; } = new(
            "fixture-lord-tagged-active",
            new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var programs = SkillProgramCatalog.Load(ProgramRules, ProgramPresentation);
            builder.AddSkill(new ContentSkillDefinition(
                SkillId,
                "主公制衡",
                "仅用于验证主公技标签的通用拥有资格。",
                SkillKind.Zhiheng)
            {
                Tags = SkillTag.Lord,
                ExecutionForms = SkillExecutionForm.Trigger
            });
            builder.AddSkill(new ContentSkillDefinition(
                ProgramSkillId,
                "主公整备",
                "仅用于验证配置程序的主公技拥有资格。")
            {
                Program = programs.Programs[ProgramSkillId],
                Tags = SkillTag.Lord,
                ExecutionForms = SkillExecutionForm.Trigger
            });
            builder.AddSkill(new ContentSkillDefinition(
                LongdanSkillId,
                "主公龙胆",
                "仅用于验证旧式转化来源的主公技拥有资格。",
                SkillKind.Longdan)
            {
                Tags = SkillTag.Lord,
                ExecutionForms = SkillExecutionForm.Trigger
            });
            foreach (var generalId in GeneralIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    generalId,
                    "主公技资格测试",
                    "sun_quan",
                    SkillId,
                    "wu",
                    AdditionalSkillIds: [ProgramSkillId, LongdanSkillId]));
            }

            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "主公技资格测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 0,
                [new ContentDeckCardCount("standard:dodge", 30)]));

            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "主公技资格 2v2",
                4,
                4,
                new Dictionary<string, int>(),
                DeckId,
                GeneralCandidateCount: 1,
                GeneralPoolIds: GeneralIds,
                ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int>
                {
                    ["team:blue"] = 2,
                    ["team:red"] = 2
                }));
        }

        private const string ProgramRules = """
            {"schemaVersion":59,"skills":[{"id":"fixture:lord-program","revision":1,
            "modifiers":[],"viewAs":[],"activations":[{"id":"prepare","minCards":0,"maxCards":0,
            "minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,
            "effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
            """;

        private const string ProgramPresentation = """
            {"schemaVersion":3,"skills":{"fixture:lord-program":{"name":"主公整备",
            "description":"仅用于验证配置程序的主公技拥有资格。"}}}
            """;
    }
}
