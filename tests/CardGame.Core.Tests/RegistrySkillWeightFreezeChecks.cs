using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class RegistrySkillWeightFreezeChecks
{
    private const string WeightedSkill = "fixture:registry-weighted";
    private const string ProgramSkill = "fixture:registry-program-weighted";
    private const string NullSkill = "fixture:registry-null-weighted";
    private const string EmptySkill = "fixture:registry-empty-weighted";
    private const string OwnerGeneral = "fixture:registry-weight-owner";
    private const string Mode = "identity:registry-weight-freeze";

    public static void RegisteredWeightsDetachInputsAndRejectOutputMutation()
    {
        var weights = new Dictionary<Role, double>
        {
            [Role.Rebel] = 2.5, [Role.Lord] = 17.25, [Role.Loyalist] = -3.75
        };
        var programWeights = new Dictionary<Role, double>
        {
            [Role.Lord] = -12.5, [Role.Rebel] = 9.5
        };
        var emptyWeights = new Dictionary<Role, double>();
        var expected = weights.ToArray();
        var expectedProgram = programWeights.ToArray();
        var package = new FixturePackage(weights, programWeights, emptyWeights);
        var registry = ContentRegistry.Build(new StandardContentPackage(), package);
        var contentHash = registry.ContentHash;

        AssertRegisteredValues();
        Require(registry.GetSkill(WeightedSkill).Tags ==
                (SkillTag.Awakening | SkillTag.Locked | SkillTag.Limited),
            "Freezing weights must preserve awakening tag normalization.");
        Require(registry.GetSkill(ProgramSkill).Program is not null,
            "Both executable and non-program skills must freeze their supplied weights.");

        var game = Start(registry);
        var views = CaptureViews(game);
        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var skills = game.CreateSnapshot(0).Players[0].Skills!;
        Require(skills.Single(skill => skill.ContentId == WeightedSkill).SelectionWeights!.SequenceEqual(expected) &&
                skills.Single(skill => skill.ContentId == ProgramSkill).SelectionWeights!.SequenceEqual(expectedProgram) &&
                skills.Single(skill => skill.ContentId == NullSkill).SelectionWeights is null &&
                skills.Single(skill => skill.ContentId == EmptySkill).SelectionWeights is { Count: 0 },
            "Player skill views must preserve ordered enum/double weights and distinguish null from empty.");

        // The registering package still owns all three input dictionaries.
        weights.Clear();
        weights[Role.Renegade] = 999;
        programWeights[Role.Lord] = 999;
        programWeights.Remove(Role.Rebel);
        emptyWeights[Role.Rebel] = 999;
        AssertUnchanged();

        RejectMutation(registry.GetSkill(WeightedSkill).SelectionWeights!, Role.Lord);
        RejectMutation(registry.GetSkill(ProgramSkill).SelectionWeights!, Role.Lord);
        RejectMutation(registry.GetSkill(EmptySkill).SelectionWeights!, Role.Lord);
        RejectMutation(skills.Single(skill => skill.ContentId == WeightedSkill).SelectionWeights!, Role.Lord);
        AssertUnchanged();

        var restored = GameReplay.Restore(checkpoint, registry);
        Require(CaptureViews(restored).SequenceEqual(views),
            "Cold replay must retain the registered weights after input and output mutation attempts.");
        Require(CaptureViews(Start(registry)).SequenceEqual(views),
            "A later engine must compile the same skill views from the frozen registry.");

        void AssertRegisteredValues()
        {
            Require(registry.GetSkill(WeightedSkill).SelectionWeights!.SequenceEqual(expected) &&
                    registry.GetSkill(ProgramSkill).SelectionWeights!.SequenceEqual(expectedProgram) &&
                    registry.GetSkill(NullSkill).SelectionWeights is null &&
                    registry.GetSkill(EmptySkill).SelectionWeights is { Count: 0 },
                "A registry must own an ordered, detached copy of every non-null skill weight dictionary.");
        }

        void AssertUnchanged()
        {
            AssertRegisteredValues();
            Require(registry.ContentHash == contentHash && CaptureViews(game).SequenceEqual(views),
                "Neither package-owned inputs nor exposed weights may change the registered fingerprint or player views.");
        }
    }

    private static void RejectMutation(IReadOnlyDictionary<Role, double> weights, Role role)
    {
        Require(weights is IDictionary<Role, double> { IsReadOnly: true },
            "Exposed skill weights must reject mutable dictionary access.");
        var dictionary = (IDictionary<Role, double>)weights;
        try { dictionary[role] = 999; }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("Published skill weights accepted an assignment.");
    }

    private static string[] CaptureViews(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();

    private static GameEngine Start(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 922, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false
        }, registry);
        var started = game.Submit(new StartGameCommand());
        Require(started.Accepted, started.Error?.Message ?? "Weight fixture start failed.");
        var prompt = game.PendingDecision!;
        Require(prompt.ValidContentIds.Contains(OwnerGeneral), "The weighted owner must be offered.");
        var selected = game.Submit(new SelectGeneralCommand(0, OwnerGeneral, game.Revision, prompt.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Weight fixture selection failed.");
        return game;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FixturePackage(IReadOnlyDictionary<Role, double> weights,
        IReadOnlyDictionary<Role, double> programWeights, IReadOnlyDictionary<Role, double> emptyWeights)
        : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("registry-skill-weight-freeze", new Version(1, 0, 0));

        public void Register(IContentRegistryBuilder builder)
        {
            var rules = JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.RulesSchemaVersion,
                skills = new[]
                {
                    new
                    {
                        id = ProgramSkill, revision = 1,
                        activations = new[]
                        {
                            new
                            {
                                id = "active", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0,
                                targetKind = "anyLiving", usesPerTurn = (int?)null,
                                effects = new[] { new { op = "draw", target = "owner", amount = 1 } }
                            }
                        }
                    }
                }
            });
            var presentation = JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object>
                {
                    [ProgramSkill] = new { name = "Weighted program", description = "Weight freeze fixture" }
                }
            });
            var program = SkillProgramCatalog.Load(rules, presentation).Programs[ProgramSkill];
            builder.AddSkill(new(WeightedSkill, "Weighted skill", "Weight freeze fixture")
            { SelectionWeights = weights, Tags = SkillTag.Awakening });
            builder.AddSkill(new(ProgramSkill, "Weighted program", "Weight freeze fixture")
            { SelectionWeights = programWeights, Program = program });
            builder.AddSkill(new(NullSkill, "Null weights", "No explicit weights"));
            builder.AddSkill(new(EmptySkill, "Empty weights", "Explicitly empty weights")
            { SelectionWeights = emptyWeights });
            builder.AddGeneral(new(OwnerGeneral, "Weight owner", "supporter", WeightedSkill, "wei")
            { AdditionalSkillIds = [ProgramSkill, NullSkill, EmptySkill] });
            var peers = Enumerable.Range(1, 3).Select(index => $"fixture:registry-weight-peer-{index}").ToArray();
            foreach (var id in peers)
                builder.AddGeneral(new(id, "Weight peer", "supporter", "standard:none", "shu"));
            builder.AddMode(new(Mode, "Registry weight freeze", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "standard:basic-demo", GeneralCandidateCount: 4, GeneralPoolIds: [OwnerGeneral, .. peers]));
        }
    }
}
