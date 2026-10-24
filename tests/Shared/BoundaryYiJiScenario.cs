using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryYiJiScenario
{
    private const string Jijie = "boundary:jijie-mobile", Jiyuan = "boundary:jiyuan-mobile";
    private const string Driver = "fixture:boundary-yiji-driver";

    internal static (ContentRegistry Registry, GameEngine Game) CreateScene()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 37, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "identity:boundary-yiji", UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:boundary-yiji", game.Revision, Decision(game)!.PromptId));
        Reach(game, DecisionKind.PlayCard);
        return (registry, game);
    }

    private static PendingDecision? Decision(GameEngine game) => game.PendingDecision ?? game.CreateSnapshot(0).PendingDecision;

    private static void Reach(GameEngine game, DecisionKind kind)
    {
        for (var step = 0; step < 100; step++)
        {
            if (Decision(game) is { PlayerSeat: 0 } prompt && prompt.Kind == kind) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed Yi Ji scenario did not reach " + kind + ": " + Decision(game)?.Prompt);
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        if (!result.Accepted) throw new InvalidOperationException(result.Error?.Message ?? "The fixed Yi Ji scenario rejected a command.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-yiji", new(1, 0, 0), []);

        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "boundary:yi-ji-mobile");
            var program = SkillProgramCatalog.Load(
                "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"" + Driver +
                "\",\"revision\":1,\"activations\":[" +
                "{\"id\":\"give-two\",\"usesPerTurn\":null,\"minCards\":2,\"maxCards\":2,\"sourceZones\":[\"hand\"],\"minTargets\":1,\"maxTargets\":1,\"targetKind\":\"otherLiving\",\"effects\":[{\"op\":\"giveSelected\",\"target\":\"selectedTarget\"}]} ," +
                "{\"id\":\"hurt\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":1,\"maxTargets\":1,\"targetKind\":\"otherLiving\",\"effects\":[{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":4}]}]}]}",
                "{\"schemaVersion\":" + SkillProgramCatalog.PresentationSchemaVersion + ",\"skills\":{\"" + Driver + "\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[Driver];
            builder.AddSkill(new(Driver, "fixture", "fixture") { Program = program });
            builder.AddGeneral(new("fixture:boundary-yiji", "界伊籍", "boundary-yi-ji", Jijie, "shu", 3, [Jiyuan, Driver]));
            for (var seat = 1; seat < 4; seat++)
                builder.AddGeneral(new($"fixture:boundary-yiji-{seat}", "其他" + seat, "supporter", "standard:none", "wei", 4, null));
            builder.AddDeck(new("fixture:boundary-yiji-deck", "固定", 4, 1, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(_ =>
                    new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 5)).ToArray()
            });
            builder.AddMode(new("identity:boundary-yiji", "固定", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:boundary-yiji-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:boundary-yiji", "fixture:boundary-yiji-1", "fixture:boundary-yiji-2", "fixture:boundary-yiji-3"]));
        }
    }
}
