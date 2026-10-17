using CardGame.Content.Standard;
using CardGame.Core;

internal static class WeightedGeneralDraftChecks
{

    public static void PrivacyDiversityAndReplay()
    {
        var registry = Build(new Dictionary<string, int> { [Id(0, 1)] = 8, [Id(1, 1)] = 5 });
        var game = Start(registry, 31);
        var limited = game.PendingDecision!;
        var oppositePreference = Start(Build(new Dictionary<string, int> { [Id(5, 0)] = 1000 }), 31);
        Require(limited.ValidContentIds.SequenceEqual(oppositePreference.PendingDecision!.ValidContentIds),
            "Changing AI strength preferences must not change the sampled families or exact editions.");
        Require(limited.Choices.Select(choice => choice.Parameters["candidate-general-id"]).Distinct().Count() == 3 &&
                limited.ValidContentIds.Count == 6 && Enumerable.Range(1, 7).Append(-1)
                    .All(seat => game.CreateSnapshot(seat).PendingDecision is null),
            "Three distinct families must privately expose all six exact editions.");
        game = Cold(game, registry);
        var withInspection = Cold(game, registry);
        Accept(withInspection, new RequestGeneralRosterCommand(0, true, withInspection.Revision, withInspection.PendingDecision!.PromptId));
        var full = withInspection.PendingDecision!;
        Require(full.ValidContentIds.Count == 32 && full.ValidContentIds.Contains(Id(0, 1)),
            "The human's full roster must retain every available exact edition.");
        Accept(withInspection, new RequestGeneralRosterCommand(0, false, withInspection.Revision, full.PromptId));
        Accept(game, new RequestGeneralRosterCommand(0, false, game.Revision, game.PendingDecision!.PromptId));
        Require(withInspection.PendingDecision!.ValidContentIds.SequenceEqual(limited.ValidContentIds) &&
                game.PendingDecision!.ValidContentIds.SequenceEqual(limited.ValidContentIds),
            "Returning from full roster must preserve the original candidates, not just produce matching rerolls.");
        withInspection = Cold(withInspection, registry);
        for (var visit = 0; visit < 3; visit++)
        {
            Accept(withInspection, new RequestGeneralRosterCommand(0, true, withInspection.Revision, withInspection.PendingDecision!.PromptId));
            withInspection = Cold(withInspection, registry);
            Accept(withInspection, new RequestGeneralRosterCommand(0, false, withInspection.Revision, withInspection.PendingDecision!.PromptId));
            Require(withInspection.PendingDecision!.ValidContentIds.SequenceEqual(limited.ValidContentIds),
                "Repeated full-roster visits and cold restores must keep the same privately drafted editions.");
        }
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
        Accept(withInspection, new RequestGeneralRosterCommand(0, true, withInspection.Revision, withInspection.PendingDecision!.PromptId));
        Accept(withInspection, new SelectGeneralCommand(0, Id(0, 1), withInspection.Revision, withInspection.PendingDecision!.PromptId));
        for (var step = 0; step < 16 && !withInspection.Events.Any(item => item.Payload is SetupCompletedEvent); step++)
            withInspection.DriveAdvanceOneStep();
        Require(System.Text.Json.JsonSerializer.Serialize(withInspection.AiGeneralThoughts) ==
                System.Text.Json.JsonSerializer.Serialize(game.AiGeneralThoughts),
            "Roster browsing must not consume randomness or change later seats' actual candidates and AI choices.");
        Cold(game, registry);

        var automatic = Start(registry, 31, interactive: false);
        Require(automatic.CreateSnapshot(0, true).Players.Select(player => Family(player.GeneralId)).Distinct().Count() == 8,
            "Automatic setup must also draft distinct weighted families.");
        Cold(automatic, registry);

        // Every fixture edition is excluded from swaps, so an AI must retain
        // exactly its three direct draws, while the human can still swap.
        var directRegistry = Build(new Dictionary<string, int>(), excludeAllSwaps: true);
        var direct = Start(directRegistry, 31);
        Require(direct.PendingDecision!.ValidContentIds.Count == 6, "AI exclusions must not remove human edition buttons.");
        Accept(direct, new SelectGeneralCommand(0, direct.PendingDecision.ValidContentIds[0], direct.Revision, direct.PendingDecision.PromptId));
        for (var step = 0; step < 16 && !direct.Events.Any(item => item.Payload is SetupCompletedEvent); step++) direct.DriveAdvanceOneStep();
        var directRequests = direct.Events.Select(item => item.Payload).OfType<GeneralSelectionRequestedEvent>()
            .Where(item => item.ActorSeat != 0).ToArray();
        Require(directRequests.Length == 7 && directRequests.All(request => request.CandidateIds.Count == 3 &&
                request.CandidateIds.Select(Family).Distinct().Count() == 3),
            "Restricted AI editions must remain selectable when directly drawn, with no additional blocked swaps.");
        Cold(direct, directRegistry);

        var exclusions = new List<string> { Id(0, 1) };
        var frozen = Build(new Dictionary<string, int>(), excluded: exclusions);
        var hash = frozen.ContentHash;
        exclusions[0] = Id(1, 1);
        Require(frozen.Modes[Mode].AiGeneralSwapExcludedIds!.SequenceEqual([Id(0, 1)]) && frozen.ContentHash == hash &&
                Build(new Dictionary<string, int>(), excluded: exclusions).ContentHash != hash,
            "AI swap exclusions must freeze external lists and participate in the content fingerprint.");
        try
        {
            Build(new Dictionary<string, int>(), excluded: ["fixture:unknown"]);
            throw new Exception("An out-of-pool AI swap exclusion was accepted.");
        }
        catch (InvalidOperationException) { }

        var mixedExclusions = Enumerable.Range(0, 16).Select(family => Id(family, 1)).Append(Id(0, 2)).ToArray();
        var mixedRegistry = Build(new Dictionary<string, int>(), firstFamilyEditions: 3, excluded: mixedExclusions);
        var mixed = Start(mixedRegistry, 31);
        Accept(mixed, new SelectGeneralCommand(0, mixed.PendingDecision!.ValidContentIds[0], mixed.Revision, mixed.PendingDecision.PromptId));
        for (var step = 0; step < 16 && !mixed.Events.Any(item => item.Payload is SetupCompletedEvent); step++) mixed.DriveAdvanceOneStep();
        Require(mixed.Events.Select(item => item.Payload).OfType<GeneralSelectionRequestedEvent>()
                .Where(request => request.ActorSeat != 0).All(request => request.CandidateIds.GroupBy(Family).All(group =>
                    group.Contains(Id(group.Key, 0)) && group.Count(id => mixedExclusions.Contains(id)) <= 1)),
            "Ordinary edition swaps must remain available while at most the directly drawn restricted edition survives per family.");

        var dualRegistry = Build(new Dictionary<string, int>(), dual: true);
        var dual = Start(dualRegistry, 31);
        var primary = dual.PendingDecision!.ValidContentIds[0];
        Accept(dual, new SelectGeneralCommand(0, primary, dual.Revision, dual.PendingDecision.PromptId));
        dual.DriveAdvanceOneStep();
        var secondary = dual.PendingDecision!.ValidContentIds.ToArray();
        Require(secondary.Length == 6 && secondary.All(id => Family(id) != Family(primary)),
            "A dual general's second slot must clear the first draw and exclude the committed primary family.");
        Accept(dual, new RequestGeneralRosterCommand(0, true, dual.Revision, dual.PendingDecision.PromptId));
        dual = Cold(dual, dualRegistry);
        Accept(dual, new RequestGeneralRosterCommand(0, false, dual.Revision, dual.PendingDecision!.PromptId));
        Require(dual.PendingDecision!.ValidContentIds.SequenceEqual(secondary),
            "Cold restore and full-roster browsing must keep the second slot's own fixed candidates.");
    }

    private const string Mode = "identity:weighted-fixture-8";
    private static string Id(int family, int edition) => $"fixture:draft-{family}-{edition}";
    private static int Family(string id) => int.Parse(id.Split('-')[1]);
    private static ContentRegistry Build(IReadOnlyDictionary<string, int> weights, int familyCount = 16, int firstFamilyEditions = 2,
        bool excludeAllSwaps = false, IReadOnlyList<string>? excluded = null, bool dual = false) =>
        ContentRegistry.Build(new StandardContentPackage(), new DraftPackage(weights, familyCount, firstFamilyEditions, excludeAllSwaps, excluded, dual));
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
    private sealed class DraftPackage(IReadOnlyDictionary<string, int> weights, int familyCount, int firstFamilyEditions,
        bool excludeAllSwaps, IReadOnlyList<string>? excluded, bool dual) : IGameContentPackage
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
                { GeneralVariantGroups = groups, GeneralSelectionWeights = weights,
                    AiGeneralSwapExcludedIds = excludeAllSwaps ? pool : excluded, DualGenerals = dual });
        }
    }
}
