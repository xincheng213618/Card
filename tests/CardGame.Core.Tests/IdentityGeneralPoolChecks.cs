using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class IdentityGeneralPoolChecks
{
    public static void ModePoolsAndSelectionBoundaries()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var five = registry.Modes["identity:classic-5"];
        var eight = registry.Modes["identity:classic-8"];
        var dual = registry.Modes["identity:classic-dual-8"];
        Need(five.GeneralSelectionPoolIds is { Count: 57 } old && old.All(id => id.StartsWith("classic:", StringComparison.Ordinal)) &&
            old.Contains("classic:cao-pi") && old.Contains("classic:zuo-ci") && old.Contains("classic:sima-yi"),
            "Five-player selection contains the ordinary standard, wind, fire, forest and mountain editions.");
        Need(!five.GeneralSelectionPoolIds!.Intersect(eight.GeneralSelectionPoolIds!).Any() &&
            eight.GeneralSelectionPoolIds!.Contains("boundary:sima-yi") &&
            eight.GeneralSelectionPoolIds!.Contains("mou:zhuge-liang") && eight.GeneralSelectionPoolIds!.Contains("mou:cao-pi") &&
            eight.GeneralSelectionPoolIds!.SequenceEqual(dual.GeneralSelectionPoolIds!) &&
            five.GeneralPoolIds!.SequenceEqual(eight.GeneralPoolIds!) && eight.GeneralPoolIds!.Contains("classic:cao-pi"),
            "Selection separates exact editions while preserving the full transformation library and the advanced dual roster.");
        foreach (var mode in new[] { five, eight, dual })
        {
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, ModeId = mode.Id, PlayerCount = mode.MaxPlayers,
                HumanRole = Role.Lord, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            Need(game.Submit(new StartGameCommand()).Accepted, "Identity draft starts.");
            var first = game.PendingDecision!;
            Need(first.Choices.Select(choice => choice.Parameters["candidate-general-id"]).Distinct().Count() == 13,
                "Splitting the selection pool must preserve the Lord's thirteen fixed candidate families.");
            Need(game.Submit(new RequestGeneralRosterCommand(0, true, game.Revision, first.PromptId)).Accepted,
                "The current mode's complete selection roster can be inspected.");
            var full = game.PendingDecision!;
            Need(full.ValidContentIds.ToHashSet().SetEquals(mode.GeneralSelectionPoolIds!),
                "Full-roster inspection and edition buttons must obey the same setup roster as random draws.");
            if (mode.Id != five.Id)
            {
                var rejected = game.Submit(new SelectGeneralCommand(0, "classic:cao-pi", game.Revision, full.PromptId));
                Need(!rejected.Accepted, "An old edition cannot be injected into eight-player setup through a stale command.");
            }
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Need(SnapshotJson.Serialize(game.CreateSnapshot(0)) == SnapshotJson.Serialize(restored.CreateSnapshot(0)),
                "The separated full selection roster cold replays with its own prompt and original candidate cache.");
            var automatic = GameEngine.CreateStandard(new GameOptions { Seed = 31, ModeId = mode.Id, PlayerCount = mode.MaxPlayers,
                UseInteractiveSetup = false }, registry);
            Need(automatic.CreateSnapshot(0, true).Players.All(player => mode.GeneralSelectionPoolIds!.Contains(player.GeneralId) &&
                (player.SecondaryGeneralId is null || mode.GeneralSelectionPoolIds!.Contains(player.SecondaryGeneralId))),
                "Constructor-based automatic setup must also honor exact-edition eligibility.");
        }
    }

    public static void ExcludedEditionsRemainRealAvatarsAndMetadataIsFrozen()
    {
        var eligible = Enumerable.Range(0, 4).Select(index => "fixture:pool-seat-" + index).ToList();
        var registry = ContentRegistry.Build(new StandardContentPackage(), new LibraryPackage(eligible));
        var hash = registry.ContentHash;
        eligible[0] = "fixture:pool-donor-0";
        Need(registry.Modes[Mode].GeneralSelectionPoolIds![0] == "fixture:pool-seat-0" && registry.ContentHash == hash &&
            ContentRegistry.Build(new StandardContentPackage(), new LibraryPackage(eligible)).ContentHash != hash,
            "Selection membership is detached from caller lists and enters the ordinary content fingerprint.");
        foreach (var invalid in new[] { new[] { "unknown" }, new[] { "fixture:pool-seat-0", "fixture:pool-seat-0" },
                     new[] { "fixture:pool-seat-0" } })
        {
            var rejected = false;
            try { ContentRegistry.Build(new StandardContentPackage(), new LibraryPackage(invalid)); }
            catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "Unknown, duplicate and undersized selection rosters must be rejected before setup.");
        }
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, ModeId = Mode, PlayerCount = 4,
            HumanRole = Role.Lord, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false }, registry);
        Need(game.Submit(new StartGameCommand()).Accepted, "Native avatar fixture starts.");
        var draft = game.PendingDecision!;
        Need(game.Submit(new SelectGeneralCommand(0, "fixture:pool-seat-0", game.Revision, draft.PromptId)).Accepted,
            "The genuine avatar owner is one of the four setup-eligible characters.");
        for (var step = 0; step < 32 && game.PendingDecision?.Kind != DecisionKind.ProgramTrigger; step++) game.DriveAdvanceOneStep();
        var prompt = game.PendingDecision!;
        Need(prompt.Kind == DecisionKind.ProgramTrigger && prompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("value"))
                .OfType<string>().ToHashSet().SetEquals(Enumerable.Range(0, 3).Select(index => "fixture:pool-donor-" + index)),
            "The actual game-start avatar library must still draw all three excluded donor editions.");
        Need(Enumerable.Range(1, 3).All(seat => game.CreateSnapshot(seat).PendingDecision is null),
            "Excluded donor availability must not expose the private transformation choice to other seats.");
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(SnapshotJson.Serialize(game.CreateSnapshot(0)) == SnapshotJson.Serialize(copy.CreateSnapshot(0)),
            "A genuine pending transformation choice cold replays independently from setup eligibility.");
    }

    private const string Mode = "identity:separate-selection-library";
    private sealed class LibraryPackage(IReadOnlyList<string> selection) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("selection-library-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:pool-avatar","revision":1,
                "triggers":[{"id":"initial","window":"gameStarting","subject":"owner","optional":false,
                "effects":[{"op":"initializePrivateGeneralLibrary","target":"owner","amount":3,"excludedSkillTags":["lord"]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { ["fixture:pool-avatar"] = new
                        { name = "真实化身", description = "从完整武将库取得三张化身" } } }));
            builder.AddSkill(new("fixture:pool-avatar", "真实化身", "真实初始化身") { Program = catalog.Programs["fixture:pool-avatar"] });
            builder.AddSkill(new("fixture:pool-donor-skill", "化身技能", "无需额外动作的技能"));
            var seats = Enumerable.Range(0, 4).Select(index => "fixture:pool-seat-" + index).ToArray();
            var donors = Enumerable.Range(0, 3).Select(index => "fixture:pool-donor-" + index).ToArray();
            foreach (var (id, index) in seats.Select((id, index) => (id, index)))
                builder.AddGeneral(new(id, id, "supporter", index == 0 ? "fixture:pool-avatar" : "standard:none", "wei"));
            foreach (var id in donors) builder.AddGeneral(new(id, id, "supporter", "fixture:pool-donor-skill", "shu"));
            builder.AddMode(new(Mode, "独立选将与化身库", 4, 4, new Dictionary<string, int>
                { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                "standard:basic-demo", 4, seats.Concat(donors).ToArray()) { GeneralSelectionPoolIds = selection });
        }
    }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
