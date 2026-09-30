using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2013LiRuChecks
{
    private const string General = "classic:li-ru";
    private const string Mode = "identity:classic-li-ru-check";
    public static void FireChainAndDyingResumeReplay()
    {
        var registry = Registry("standard:iron_chain", 3);
        var game = Start(registry);
        PlayBoundary(game);
        var chain = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.IronChain && a.CardId is not null);
        Accept(game.Submit(new PlayCardCommand(0, chain.CardId!.Value, [1, 4], game.Revision, game.PendingDecision!.PromptId)));
        Settle(game);
        PlayBoundary(game);
        var action = game.GetHumanLegalActions().Single(a => a.ProgramSkillId == "classic:fencheng");
        Accept(game.Submit(new UseProgramSkillCommand(0, "classic:fencheng", action.ProgramActivationId!, [], [], game.Revision, game.PendingDecision!.PromptId)));
        Settle(game);
        var damage = game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().ToArray();
        Require(damage.Select(d => d.TargetSeat).Order().SequenceEqual([1, 4]) && damage.All(d => d.Nature == DamageNature.Fire && d.SourceSeat == 0 && d.Amount == 2),
            "Fencheng fire damage must use the normal chain propagation path.");
        Require(!game.CreateSnapshot(0, true).Players[1].IsChained && !game.CreateSnapshot(0, true).Players[4].IsChained,
            "Chained damage must clear both chain states before completing the walk.");
        EqualReplay(game, registry);

        var dyingRegistry = Registry("standard:slash", 0, 2);
        var dying = Start(dyingRegistry);
        PlayBoundary(dying);
        var fire = dying.GetHumanLegalActions().Single(a => a.ProgramSkillId == "classic:fencheng");
        Accept(dying.Submit(new UseProgramSkillCommand(0, "classic:fencheng", fire.ProgramActivationId!, [], [], dying.Revision, dying.PendingDecision!.PromptId)));
        Settle(dying);
        Require(dying.Events.Select(e => e.Payload).OfType<PlayerDyingEvent>().Any(), "Fencheng must enter the ordinary dying/rescue boundary.");
        EqualReplay(dying, dyingRegistry);
    }



    private static ContentRegistry Registry(string card, int initial, int bankHp = 8, bool observer = false) => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Scenario(card, initial, bankHp, observer));
    private static GameEngine Start(ContentRegistry registry, int seed = 7, string general = General)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, PlayerCount = 5, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, general, game.Revision, game.PendingDecision!.PromptId)));
        return game;
    }
    private static void PlayBoundary(GameEngine game)
    {
        for (var step = 0; step < 100; step++)
        { if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return; Advance(game); }
        throw new InvalidOperationException("Li Ru play phase not reached.");
    }
    private static void Settle(GameEngine game)
    { for (var step = 0; step < 200 && game.ResolutionStack.Count != 0; step++) Advance(game); }
    private static void Advance(GameEngine game) => Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Li Ru command rejected.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(game.CreateSnapshot(0, true));
    private static void EqualReplay(GameEngine game, ContentRegistry registry)
    {
        var checkpoint = game.CreateCheckpoint();
        var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint)), registry);
        Require(State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)) &&
            JsonSerializer.Serialize(game.CardMovements) == JsonSerializer.Serialize(replay.CardMovements) &&
            GameCheckpointJson.Serialize(checkpoint) == GameCheckpointJson.Serialize(replay.CreateCheckpoint()),
            "Li Ru command, event, physical movement and checkpoint replay must be exact.");
    }
    private static string[] Events(GameEngine game) => game.Events.Select(e =>
        $"{e.Id}|{e.ParentId}|{e.Sequence}|{e.Revision}|{e.CorrelationId}|{e.Payload.GetType().FullName}|{JsonSerializer.Serialize(e.Payload, e.Payload.GetType())}").ToArray();
    private sealed class Scenario(string cardId, int initial, int bankHp, bool observer) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("li-ru-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);
        public void Register(IContentRegistryBuilder builder)
        {
            var pool = new List<string> { observer ? "fixture:li-ru-observer" : General };
            if (observer)
            {
                var rules = $$"""
                  {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:cost-observer","revision":1,
                  "minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"triggers":[{"id":"after-cost","window":"cardsMoved",
                  "subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","optional":false,
                  "effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
                  """;
                const string presentation = """{"schemaVersion":3,"skills":{"fixture:cost-observer":{"name":"移动观察","description":"测试"}}}""";
                var program = SkillProgramCatalog.Load(rules, presentation).Programs["fixture:cost-observer"];
                builder.AddSkill(new ContentSkillDefinition("fixture:cost-observer", "移动观察", "测试") { Program = program });
                builder.AddGeneral(new("fixture:li-ru-observer", "李儒测试", "supporter", "classic:mieji", "qun", BaseHp: 3,
                    AdditionalSkillIds: ["classic:juece", "classic:fencheng", "fixture:cost-observer"]));
            }
            for (var index = 0; index < 4; index++)
            { var id = "fixture:li-ru-bank-" + index; pool.Add(id); builder.AddGeneral(new(id, "测试对手", "supporter", "standard:none", "qun", BaseHp: bankHp)); }
            builder.AddDeck(new("fixture:li-ru-deck", "测试牌堆", initial, 0, [])
                { PhysicalCards = Enumerable.Range(0, 160).Select(i => new ContentDeckPhysicalCard(cardId == "mixed" ? i % 4 == 0 ? "standard:duel" : "standard:slash" : cardId, Suit.Spade, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "李儒测试", 5, 5, new Dictionary<string, int> { [nameof(Role.Lord)] = 1,
                [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 }, "fixture:li-ru-deck", GeneralCandidateCount: 5, GeneralPoolIds: pool));
        }
    }
}
