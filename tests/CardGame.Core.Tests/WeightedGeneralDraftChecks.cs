using CardGame.Content.Standard;
using CardGame.Core;

internal static class WeightedGeneralDraftChecks
{
    public static void RegistryContracts()
    {
        var weights = new Dictionary<string, int> { [Id(0, 1)] = 8, [Id(1, 1)] = 5 };
        var registry = Build(weights);
        var hash = registry.ContentHash;
        weights[Id(0, 1)] = 3;
        Require(registry.Modes[Mode].GeneralSelectionWeights![Id(0, 1)] == 8 && registry.ContentHash == hash,
            "Registered weights must detach from mutable package inputs.");
        Require(Build(weights).ContentHash != hash, "Weights must participate in the content fingerprint.");
        Require(Build(new Dictionary<string, int> { [Id(1, 1)] = 5, [Id(0, 1)] = 8 }).ContentHash == hash,
            "Dictionary insertion order must not affect the fingerprint.");
        foreach (var invalid in new[]
                 {
                     new Dictionary<string, int> { [Id(0, 1)] = 0 },
                     new Dictionary<string, int> { [Id(0, 1)] = -1 },
                     new Dictionary<string, int> { [Id(0, 1)] = 1001 },
                     new Dictionary<string, int> { ["fixture:unknown"] = 2 }
                 })
        {
            try { Build(invalid); throw new Exception("Invalid draft weights were accepted."); }
            catch (InvalidOperationException) { }
        }
        try { Build(weights, familyCount: 7); throw new Exception("A weighted pool with too few unique families was accepted."); }
        catch (InvalidOperationException) { }
        var classic = StandardContentRegistry.CreateWithClassicGenerals();
        Require(classic.Modes["identity:classic-8"].GeneralSelectionWeights is { Count: > 30 } &&
                classic.Modes["identity:classic-5"].GeneralSelectionWeights is null,
            "Only the eight-player classic roster opts into the curated preferences.");
    }

    public static void PrivacyDiversityAndReplay()
    {
        var registry = Build(new Dictionary<string, int> { [Id(0, 1)] = 8, [Id(1, 1)] = 5 });
        var game = Start(registry, 31);
        var limited = game.PendingDecision!;
        Require(limited.Choices.Select(choice => choice.Parameters["candidate-general-id"]).Distinct().Count() == 3 &&
                limited.ValidContentIds.Count == 6 && Enumerable.Range(1, 7).Append(-1)
                    .All(seat => game.CreateSnapshot(seat).PendingDecision is null),
            "Three distinct families must privately expose all six exact editions.");
        game = Cold(game, registry);
        var withInspection = Cold(game, registry);
        Accept(withInspection, new RequestGeneralRosterCommand(0, true, withInspection.Revision, withInspection.PendingDecision!.PromptId));
        var full = withInspection.PendingDecision!;
        Require(full.ValidContentIds.Count == 32 && full.Choices.First(choice =>
                choice.Parameters["candidate-general-id"] == Id(0, 1)).Parameters["general-id"] == Id(0, 1),
            "Full roster must keep all versions and prefer the weighted edition for its family.");
        Accept(withInspection, new RequestGeneralRosterCommand(0, false, withInspection.Revision, full.PromptId));
        Accept(game, new RequestGeneralRosterCommand(0, false, game.Revision, game.PendingDecision!.PromptId));
        Require(withInspection.PendingDecision!.ValidContentIds.SequenceEqual(game.PendingDecision!.ValidContentIds),
            "Inspecting the full roster must consume no draft randomness.");
        game = Cold(game, registry);
        Accept(game, new RequestGeneralRosterCommand(0, true, game.Revision, game.PendingDecision!.PromptId));
        Accept(game, new SelectGeneralCommand(0, Id(0, 1), game.Revision, game.PendingDecision!.PromptId));
        for (var step = 0; step < 16 && !game.Events.Any(item => item.Payload is SetupCompletedEvent); step++)
            game.DriveAdvanceOneStep();
        var selections = game.Events.Select(item => item.Payload).OfType<GeneralSelectedEvent>().ToArray();
        Require(selections.Length == 8 && selections.Select(item => Family(item.GeneralId)).Distinct().Count() == 8,
            "Every seat must commit a different character family, including across editions.");
        var requests = game.Events.Select(item => item.Payload).OfType<GeneralSelectionRequestedEvent>()
            .Where(item => item.ActorSeat != 0).ToArray();
        Require(requests.Select(request => string.Join(",", request.CandidateIds.Select(Family).Distinct().Order())).Distinct().Count() >= 4 &&
                requests.All(request => !request.CandidateIds.Any(id => Family(id) == 0)),
            "AI seats must draw fresh candidates and never receive the already chosen family's other edition.");
        Require(game.AiGeneralThoughts.All(thought => thought.Candidates.Any(candidate =>
                candidate.GeneralId == thought.SelectedGeneralId)), "AI must choose within its own published candidates.");
        Cold(game, registry);

        var automatic = Start(registry, 31, interactive: false);
        Require(automatic.CreateSnapshot(0, true).Players.Select(player => Family(player.GeneralId)).Distinct().Count() == 8,
            "Automatic setup must also draft distinct weighted families.");
        Cold(automatic, registry);
    }

    public static void SamplingAndAiPreferences()
    {
        var weights = new Dictionary<string, int> { [Id(0, 1)] = 8 };
        var weighted = Build(weights);
        var uniform = Build(new Dictionary<string, int>());
        var extraEditions = Build(new Dictionary<string, int>(), firstFamilyEditions: 8);
        var boosted = 0;
        var baseline = 0;
        var extraEditionOffers = 0;
        var seen = new HashSet<int>();
        for (var seed = 1; seed <= 128; seed++)
        {
            var offered = Start(weighted, seed).PendingDecision!.ValidContentIds;
            boosted += offered.Contains(Id(0, 1)) ? 1 : 0;
            baseline += Start(uniform, seed).PendingDecision!.ValidContentIds.Contains(Id(0, 1)) ? 1 : 0;
            extraEditionOffers += Start(extraEditions, seed).PendingDecision!.ValidContentIds.Contains(Id(0, 1)) ? 1 : 0;
            foreach (var id in offered) seen.Add(Family(id));
        }
        Console.WriteLine($"[DRAFT] 128 seeded openings, boosted family offered: {baseline} uniform -> {boosted} weighted; families seen: {seen.Count}/16.");
        Require(boosted > baseline * 2 && boosted < 128 && seen.Count == 16,
            "Weighting must measurably increase strong-family offers without fixing the roster or eliminating ordinary families.");
        Require(extraEditionOffers < baseline * 2,
            "Eight editions must not multiply a character's draft tickets compared with two editions.");
        var view = Start(weighted, 31).CreateSnapshot(0);
        var plain = new GeneralDefinition(Id(0, 0), "普通版本", "supporter", []);
        var strong = plain with { Id = Id(0, 1), Name = "强势版本" };
        var brain = new SimpleAiBrain(0, 31);
        var (selected, thought) = brain.ChooseGeneral(view, Role.Lord, [plain, strong], 1, weights);
        Require(selected.Id == strong.Id && thought.Candidates.Single(candidate => candidate.GeneralId == strong.Id)
                .Reason.Contains("权重 8", StringComparison.Ordinal),
            "Equal base skills must favor the weighted exact edition and explain the public preference.");
    }

    private const string Mode = "identity:weighted-fixture-8";
    private static string Id(int family, int edition) => $"fixture:draft-{family}-{edition}";
    private static int Family(string id) => int.Parse(id.Split('-')[1]);
    private static ContentRegistry Build(IReadOnlyDictionary<string, int> weights, int familyCount = 16, int firstFamilyEditions = 2) =>
        ContentRegistry.Build(new StandardContentPackage(), new DraftPackage(weights, familyCount, firstFamilyEditions));
    private static GameEngine Start(ContentRegistry registry, int seed, bool interactive = true)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, ModeId = Mode, PlayerCount = 8,
            HumanSeat = 0, HumanRole = Role.Lord, UseInteractiveSetup = interactive, AdvanceAfterHumanCommands = false }, registry);
        game.DriveStart();
        return game;
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "Draft command rejected.");
    }
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        foreach (var seat in Enumerable.Range(0, 8).Append(-1))
            Require(SnapshotJson.Serialize(restored.CreateSnapshot(seat)) == SnapshotJson.Serialize(game.CreateSnapshot(seat)),
                "Every viewer's draft and completed setup must cold replay exactly.");
        return restored;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private sealed class DraftPackage(IReadOnlyDictionary<string, int> weights, int familyCount, int firstFamilyEditions) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("weighted-draft-fixture", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var groups = Enumerable.Range(0, familyCount).ToDictionary(index => $"family:{index}",
                index => (IReadOnlyList<string>)Enumerable.Range(0, index == 0 ? firstFamilyEditions : 2)
                    .Select(edition => Id(index, edition)).ToArray());
            var pool = groups.Values.SelectMany(ids => ids).ToArray();
            foreach (var id in pool) builder.AddGeneral(new(id, id, "supporter", "standard:none", "wei"));
            builder.AddMode(new(Mode, "加权选将检查", 8, 8, new Dictionary<string, int>
                { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 2, [nameof(Role.Rebel)] = 4, [nameof(Role.Renegade)] = 1 },
                "standard:basic-demo", GeneralPoolIds: pool)
                { GeneralVariantGroups = groups, GeneralSelectionWeights = weights });
        }
    }
}
