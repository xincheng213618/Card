using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class NiuJinChecks
{
    private const string Mode = "identity:niu-jin-fixture";
    private const string Driver = "fixture:niu-jin-driver";
    private const string Quiet = "fixture:niu-jin-quiet";

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

    private static PlayerSnapshot View(GameEngine game, int seat) => game.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? Prompt(GameEngine game) => game.PendingDecision;
    private static bool IsOwnPlay(GameEngine game) => Prompt(game) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard };
    private static void ToOwnPlay(GameEngine game) => Reach(game, () => IsOwnPlay(game));

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
