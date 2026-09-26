using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class PindianModuleChecks
{
    private const string ProgramSkillId = "fixture:program-pindian";
    private const string ProgramRules =
        """{"schemaVersion":61,"skills":[{"id":"fixture:program-pindian","revision":1,"minimumRulesVersion":170,"modifiers":[],"viewAs":[],"activations":[{"id":"contest","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWithHand","usesPerTurn":1,"condition":{"kind":"always"},"effects":[{"op":"pindian","target":"selectedTarget","amount":1,"condition":{"kind":"always"}},{"op":"draw","target":"owner","amount":2,"condition":{"kind":"pindianNotWon"}}]}],"triggers":[],"contributions":[],"cardIdentities":[]}]}""";
    private const string ProgramPresentation =
        """{"schemaVersion":3,"skills":{"fixture:program-pindian":{"name":"程序拼点","description":"拼点未赢摸两张牌。"}}}""";

    public static void ActiveProgramPindianSuspendsConditionsAndReplays()
    {
        var program = SkillProgramCatalog.Load(ProgramRules, ProgramPresentation).Programs[ProgramSkillId];
        var registry = ProgramRegistry(program);
        var game = CreateProgram(registry);
        var play = game.PendingDecision!;
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill && candidate.ProgramSkillId == ProgramSkillId);
        var sourceCard = game.CreateSnapshot(0).Players[0].Hand[0];
        var before = game.CreateSnapshot(0).Players[0].HandCount;
        Accept(game.Submit(new UseProgramSkillCommand(
            0, ProgramSkillId, action.ProgramActivationId!, [sourceCard.Id], [1],
            game.Revision, play.PromptId)));
        Require(game.ResolutionStack[0] is ProgramSkillFrame { PindianResultBindings.Count: 0 } &&
                game.ResolutionStack[^1] is PindianFrame,
            "The program must suspend at its Pindian instruction until the opponent commits a card.");
        Require(game.CreateSnapshot(1).PendingDecision is { IsPrivate: true } &&
                game.CreateSnapshot(2).PendingDecision is null &&
                game.CreateSnapshot(2).PublicRevealedCards.Count == 0,
            "The opponent choice and both uncommitted cards must remain private.");
        var restored = VerifyReplay(game, registry);
        Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Accept(restored.Submit(new AdvanceOneStepCommand(restored.Revision)));
        Require(game.Events.Select(item => item.Payload).OfType<PindianResultDeterminedEvent>().Single().Result is
                    { SourceSeat: 0, OpponentSeat: 1, SourceRank: 7, OpponentRank: 7, SourceWon: false } &&
                game.ResolutionStack.Count == 0 &&
                game.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>().Single() is
                    { SkillId: ProgramSkillId, Completed: true } &&
                game.CreateSnapshot(0).Players[0].HandCount == before + 1 &&
                game.CreateCardZoneDiagnostics().All(card => card.Location != CardLocation.Processing),
            "A tie must discard both contest cards, execute the not-won draw once, and resume the parent.");
        Equivalent(game, restored);
        VerifyReplay(game, registry);
    }

    private static ContentRegistry ProgramRegistry(SkillProgram program) =>
        ContentRegistry.Build(new StandardContentPackage(), new ProgramFixturePackage(program));

    private static GameEngine CreateProgram(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7001, PlayerCount = 6,
            ModeId = "identity:classic-pindian-fixture", HumanSeat = 0, HumanRole = Role.Lord,
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false, MaxTurns = 20 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        var setup = game.PendingDecision!;
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:pindian-general-0", game.Revision, setup.PromptId)));
        for (var step = 0; step < 24; step++)
        {
            if (game.PendingDecision?.Kind == DecisionKind.PlayCard) return game;
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        }
        throw new InvalidOperationException("The fixed program Pindian play boundary was not reached.");
    }

    private static GameEngine VerifyReplay(GameEngine game, ContentRegistry registry)
    {
        var serializedFrames = JsonSerializer.Serialize(game.ResolutionStack);
        var frames = JsonSerializer.Deserialize<ResolutionFrame[]>(serializedFrames)!;
        Require(JsonSerializer.Serialize(frames) == serializedFrames, "All child and parent frames must be serializable data.");
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Equivalent(game, restored);
        return restored;
    }
    private static void Equivalent(GameEngine game, GameEngine restored)
    {
        static string[] Events(GameEngine engine) => engine.Events.Select(item =>
            $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(restored.CreateSnapshot(0, true)) &&
            Events(game).SequenceEqual(Events(restored)), "Replay must reproduce public events, private prompt and card zones exactly.");
    }
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Command rejected.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class ProgramFixturePackage(SkillProgram program) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-program-pindian", new Version(1, 0, 0));

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(new(ProgramSkillId, "程序拼点", "拼点未赢摸两张牌。") { Program = program });
            var ids = Enumerable.Range(0, 6).Select(index => $"fixture:pindian-general-{index}").ToArray();
            foreach (var id in ids)
                builder.AddGeneral(new(id, "Fixture", "supporter", ProgramSkillId, "wei"));
            builder.AddDeck(new("fixture:pindian-deck", "Fixed tied contests", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 96)
                    .Select(_ => new ContentDeckPhysicalCard("standard:crossbow", Suit.Spade, 7)).ToArray()
            });
            builder.AddMode(new("identity:classic-pindian-fixture", "Pindian fixture", 6, 6,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 3, [nameof(Role.Renegade)] = 1 },
                "fixture:pindian-deck", GeneralCandidateCount: 6, GeneralPoolIds: ids));
        }
    }
}
