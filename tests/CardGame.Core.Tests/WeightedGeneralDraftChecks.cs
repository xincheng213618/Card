using CardGame.Content.Standard;
using CardGame.Core;

internal static class WeightedGeneralDraftChecks
{

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
