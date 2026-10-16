using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class NiuJinChecks
{
    private const string Mode = "identity:niu-jin-fixture";
    private const string Driver = "fixture:niu-jin-driver";
    private const string Quiet = "fixture:niu-jin-quiet";
    private const string LieweiDrawReason = "skill-program.ol:liewei.Draw";

    public static void OpeningDrawsToEightWithoutDrawingOrReducingAtFour()
    {
        foreach (var population in new[] { 4, 8 })
        {
            var (game, registry) = Start(population);
            var expectedExtra = Math.Max(0, population - 4);
            Require(View(game, 0).Hand.Count == 4 + expectedExtra,
                "Cuorui draws only the shortfall to actual population, retaining four original cards in a four-player game.");
            Require(game.CardMovements.Count(move => move.To == CardLocation.Hand(0) &&
                move.Reason.Value == "skill-program.hand-count.draw") == expectedExtra,
                "The real opening movement contains exactly the population shortfall rather than a replacement initial-hand rule.");
            game = Cold(game, registry, population);
            Require(View(game, 0).Hand.Count == 4 + expectedExtra,
                "Cold recovery does not repeat opening Cuorui draws.");
        }
    }

    public static void DelayedTargetSkipsExactlyNextJudgmentAndRetainsTheCard()
    {
        var (game, registry) = Start(4);
        var lightning = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Lightning);
        var entity = lightning.CardId!.Value;
        Accept(game, new PlayCardCommand(0, entity, lightning.TargetSeats,
            game.Revision, Prompt(game)!.PromptId, lightning.PlayedCardKind, lightning.TargetCardId));
        ToOwnPlay(game);
        Require(Location(game, entity) == CardLocation.Judgment(0),
            "A real self-used Lightning enters the owner's judgment area after arming Cuorui.");
        Require(Events<ProgramBooleanStateChangedEvent>(game).Any(change =>
            change.SkillId == "ol:cuorui" && change.StateId == "skip-next-judgment" && change.Value),
            "The actual delayed target records a public pending skip before the turn ends.");
        var initialHp = View(game, 0).Hp;
        var originalTurn = game.CreateSnapshot(0).TurnNumber;
        game = Cold(game, registry, 4);
        EndOwnPlay(game);
        Reach(game, () => IsOwnPlay(game) && game.CreateSnapshot(0).TurnNumber > originalTurn);
        Require(Location(game, entity) == CardLocation.Judgment(0) &&
            !Events<LightningResolvedEvent>(game).Any(result => result.CardId == entity) &&
            View(game, 0).Hp == initialHp,
            "The next actual judgment phase is skipped, leaving the same delayed entity in place without its thunder damage.");
        Require(Events<ProgramBooleanStateChangedEvent>(game).Count(change =>
            change.SkillId == "ol:cuorui" && change.StateId == "skip-next-judgment" && !change.Value) == 1,
            "The cross-turn skip debt is consumed exactly once.");
        var skippedTurn = game.CreateSnapshot(0).TurnNumber;
        game = Cold(game, registry, 4);
        EndOwnPlay(game);
        Reach(game, () => IsOwnPlay(game) && game.CreateSnapshot(0).TurnNumber > skippedTurn);
        Require(Events<LightningResolvedEvent>(game).Count(result =>
            result.CardId == entity && result.JudgmentTargetSeat == 0 && result.Hit) == 1 &&
            Location(game, entity) == CardLocation.DiscardPile && View(game, 0).Hp == initialHp - 3,
            "On the following turn the retained real Lightning judges normally and hits the fixed Spade-seven deck once.");
        _ = Cold(game, registry, 4);
        AuditIssuedDebtAfterSourceLossAndAlreadySkipped();
    }

    public static void OnlyTheActualKillerOffersOptionalThreeCardDrawAndColdRecovery()
    {
        var (game, registry) = Start(8);
        ActivateDriver(game, "source-less", [1]);
        ToOwnPlay(game);
        Require(Events<PlayerDiedEvent>(game).Any(death => death.VictimSeat == 1 && death.KillerSeat is null) &&
            !Events<ProgramBindingResolvedEvent>(game).Any(binding => binding.SkillId == "ol:liewei"),
            "Real source-less HP loss death does not offer Liewei to the character that requested that loss.");
        game = Cold(game, registry, 8);

        ActivateDriver(game, "wound", [3]);
        ToOwnPlay(game);
        Require(View(game, 3).Hp == 1, "The native Duel target is genuinely wounded to one HP before its other-source kill.");
        ActivateDriver(game, "duel-others", [2, 3]);
        ToOwnPlay(game);
        Require(Events<PlayerDiedEvent>(game).Any(death => death.KillerSeat is { } killer && killer != 0) &&
            !Events<ProgramBindingResolvedEvent>(game).Any(binding => binding.SkillId == "ol:liewei") &&
            LieweiDraws(game) == 0,
            "A native Duel between other participants attributes the kill to its actual participant and cannot offer Niu Jin's draw.");

        ActivateDriver(game, "kill", [4]);
        Reach(game, () => Prompt(game)?.SkillPrompt?.SkillId == "ol:liewei" &&
            Prompt(game)!.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Require(Events<PlayerDiedEvent>(game).Any(death => death.VictimSeat == 4 && death.KillerSeat == 0),
            "The positive case records Niu Jin as the actual native damage killer before the optional trigger.");
        Require(Prompt(game)!.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip"),
            "Liewei presents both activation and decline choices.");
        game = Cold(game, registry, 8);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        ToOwnPlay(game);
        Require(LieweiDraws(game) == 3 && Events<ProgramBindingResolvedEvent>(game).Count(binding =>
            binding.SkillId == "ol:liewei" && binding.Activated && binding.Completed) == 1,
            "One accepted real kill trigger moves exactly three cards, separately from the identity-mode kill reward.");
        game = Cold(game, registry, 8);
        ActivateDriver(game, "kill", [5]);
        Reach(game, () => Prompt(game)?.SkillPrompt?.SkillId == "ol:liewei");
        game = Cold(game, registry, 8);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
        ToOwnPlay(game);
        Require(LieweiDraws(game) == 3 && Events<ProgramBindingResolvedEvent>(game).Count(binding =>
            binding.SkillId == "ol:liewei" && !binding.Activated) == 1,
            "Declining a later actual kill adds no Liewei cards and cold recovery does not repeat the accepted draw.");
        _ = Cold(game, registry, 8);
    }

    public static void DynamicHandPopulationRejectsAmbiguousAndUnsupportedParameters()
    {
        foreach (var parameters in new[]
        {
            "\"numberExpression\":\"livingPlayerCount\",\"amount\":4",
            "\"numberExpression\":\"currentHp\"",
            "\"numberExpression\":\"notAnExpression\""
        })
        {
            var rejected = false;
            try { _ = ParseDynamic(parameters); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Dynamic hand-population draws reject competing fixed values and unsupported expressions.");
        }
        Require(ParseDynamic("\"numberExpression\":\"livingPlayerCount\"").Programs[Driver]
                .Triggers.Single().Effects.Single().NumberExpression == SkillProgramNumberExpression.LivingPlayerCount,
            "The additive population expression survives parsing as gameplay data.");
    }

    private static SkillProgramCatalog ParseDynamic(string parameters) => SkillProgramCatalog.Load(
        $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"triggers":[{"id":"opening","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"drawToHandCount","target":"owner",{{parameters}}}]}]}]}""",
        Presentation([Driver]));

    private static int LieweiDraws(GameEngine game) => game.CardMovements.Count(move =>
        move.To == CardLocation.Hand(0) && move.Reason.Value == LieweiDrawReason);

    private static void AuditIssuedDebtAfterSourceLossAndAlreadySkipped()
    {
        // Host-level audit only. These mutations intentionally make no Replay claim;
        // the surrounding public-command scenario supplies the cold-recovery proof.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var (game, _) = Start(4);
        var lightning = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Lightning);
        Accept(game, new PlayCardCommand(0, lightning.CardId!.Value, lightning.TargetSeats,
            game.Revision, Prompt(game)!.PromptId, lightning.PlayedCardKind, lightning.TargetCardId));
        ToOwnPlay(game);
        var players = (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", flags)!.GetValue(game)!;
        var source = players[0].SkillGrants.Grants.Single(grant => grant.SkillId == "ol:cuorui");
        players[0].SkillGrants.RemoveGrant(source.GrantId);
        var pending = typeof(GameEngine).GetField("_pendingTurnDelayedEffects", flags)!;
        pending.SetValue(game, Enum.Parse(pending.FieldType, "SkipJudgmentPhase"));
        var consume = typeof(GameEngine).GetMethod("ConsumeNiuJinJudgmentSkip", flags)!;
        _ = consume.Invoke(game, null);
        var readState = typeof(GameEngine).GetMethod("GetProgramBooleanState", flags, null,
            [typeof(int), typeof(string), typeof(string), typeof(string)], null)!;
        Require(readState.Invoke(game, [0, "ol:cuorui", source.SkillInstanceId, "skip-next-judgment"]) is false &&
            pending.GetValue(game)!.ToString()!.Contains("SkipJudgmentPhase", StringComparison.Ordinal),
            "An already issued skip debt is consumed even after actual source removal and coalesces with an existing native skip.");
        _ = consume.Invoke(game, null);
        Require(readState.Invoke(game, [0, "ol:cuorui", source.SkillInstanceId, "skip-next-judgment"]) is false,
            "A repeated host boundary cannot recreate a consumed source-less debt for a future reacquisition.");
    }

    private static IEnumerable<T> Events<T>(GameEngine game) where T : IGameEvent =>
        game.Events.Select(item => item.Payload).OfType<T>();

    private static PlayerSnapshot View(GameEngine game, int seat) => game.CreateSnapshot(seat).Players[seat];
    private static CardLocation Location(GameEngine game, int entity) =>
        game.CreateCardZoneDiagnostics().Single(item => item.CardId == entity).Location;
    private static PendingDecision? Prompt(GameEngine game) => game.PendingDecision;
    private static bool IsOwnPlay(GameEngine game) => Prompt(game) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard };
    private static void ToOwnPlay(GameEngine game) => Reach(game, () => IsOwnPlay(game));
    private static void EndOwnPlay(GameEngine game) =>
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));

    private static void ActivateDriver(GameEngine game, string activation, IReadOnlyList<int> targets) =>
        Accept(game, new UseProgramSkillCommand(0, Driver, activation, [], targets, game.Revision, Prompt(game)!.PromptId));

    private static void Reach(GameEngine game, Func<bool> reached)
    {
        for (var step = 0; step < 180; step++)
        {
            if (reached()) return;
            if (Prompt(game) is { PlayerSeat: 0 } prompt &&
                prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("response") is "pass" or "let-die" or "take-damage"))
                Answer(game, choice => choice.Parameters.GetValueOrDefault("response") is "pass" or "let-die" or "take-damage");
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Niu Jin did not reach the real command boundary: " +
            JsonSerializer.Serialize(new { Prompt = Prompt(game)?.Kind, Skill = Prompt(game)?.SkillPrompt?.SkillId,
                Frames = game.ResolutionStack.Select(frame => new { frame.Id, frame.Kind }) }));
    }

    private static void Answer(GameEngine game, Func<PromptChoice, bool> choose)
    {
        var prompt = Prompt(game)!;
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            prompt.Choices.First(choose).Id, game.Revision));
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "Niu Jin fixture command was rejected.");
    }

    private static GameEngine Cold(GameEngine game, ContentRegistry registry, int population)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, population).All(viewer =>
                SnapshotJson.Serialize(game.CreateSnapshot(viewer)) == SnapshotJson.Serialize(restored.CreateSnapshot(viewer))) &&
            game.CardMovements.SequenceEqual(restored.CardMovements) &&
            JsonSerializer.Serialize(game.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType()))) ==
            JsonSerializer.Serialize(restored.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType()))),
            "All private viewers, movement history and typed pending frames restore from the real command prefix.");
        return restored;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static (GameEngine Game, ContentRegistry Registry) Start(int population)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(population));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = population, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 30
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, () => Prompt(game) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(game, new SelectGeneralCommand(0, "ol:niu-jin", game.Revision, Prompt(game)!.PromptId));
        ToOwnPlay(game);
        return (game, registry);
    }

    private static string Presentation(IEnumerable<string> ids) => JsonSerializer.Serialize(new
    {
        schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
        skills = ids.ToDictionary(id => id, id => new { name = id, description = "Native Niu Jin behavior fixture." })
    });

    private sealed class Fixture(int population) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("niu-jin-fixture", new Version(1, 0, 0), []);

        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(new DriverBuilder(builder), "ol:niu-jin");
            var catalog = SkillProgramCatalog.Load(
                $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"activations":[
                    {"id":"source-less","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":4}]},
                    {"id":"wound","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":3}]},
                    {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":4}]},
                    {"id":"duel-others","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"startVirtualDuel","target":"owner"}]}
                  ]},
                  {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"skip-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
                ]}
                """, Presentation([Driver, Quiet]));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                {
                    Program = program, ProgramPresentation = catalog.Presentations[id],
                    SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -10000d)
                });
            var pool = new List<string> { "ol:niu-jin" };
            for (var index = 1; index < population; index++)
            {
                var id = "fixture:niu-jin-peer-" + index;
                pool.Add(id);
                builder.AddGeneral(new(id, "Quiet peer", "supporter", Quiet, "wei", 4));
            }
            builder.AddDeck(new("fixture:niu-jin-deck", "Fixed Lightning cards", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 160)
                    .Select(_ => new ContentDeckPhysicalCard("standard:lightning", Suit.Spade, 7)).ToArray()
            });
            builder.AddMode(new(Mode, "Native Niu Jin", population, population,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = population - 1 },
                "fixture:niu-jin-deck", GeneralCandidateCount: population, GeneralPoolIds: pool));
        }
    }

    private sealed class DriverBuilder(IContentRegistryBuilder builder) : IContentRegistryBuilder
    {
        public void AddCard(ContentCardDefinition definition) => builder.AddCard(definition);
        public void AddSkill(ContentSkillDefinition definition) => builder.AddSkill(definition);
        public void AddGeneral(ContentGeneralDefinition definition) => builder.AddGeneral(definition.Id == "ol:niu-jin"
            ? definition with { AdditionalSkillIds = [.. (definition.AdditionalSkillIds ?? []), Driver] }
            : definition);
        public void AddDeck(ContentDeckRecipe definition) => builder.AddDeck(definition);
        public void AddMode(ContentModeDefinition definition) => builder.AddMode(definition);
    }
}
