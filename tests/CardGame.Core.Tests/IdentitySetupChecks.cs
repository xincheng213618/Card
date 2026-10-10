using CardGame.Content.Standard;
using CardGame.Core;

internal static class IdentitySetupChecks
{
    public static void RoleCandidatesPrivacyAndReplay()
    {
        var counts = new Dictionary<string, int>
        {
            [nameof(Role.Lord)] = 13, [nameof(Role.Loyalist)] = 8,
            [nameof(Role.Rebel)] = 8, [nameof(Role.Renegade)] = 13
        };
        var registry = ContentRegistry.Build(new StandardContentPackage(), new CandidatePackage(counts));
        var hash = registry.ContentHash;
        counts[nameof(Role.Lord)] = 1;
        Require(registry.Modes["identity:candidate-8"].RoleGeneralCandidateCounts![nameof(Role.Lord)] == 13 && registry.ContentHash == hash,
            "Registered candidate counts must detach from the package's mutable dictionary.");
        var changed = ContentRegistry.Build(new StandardContentPackage(), new CandidatePackage(counts));
        Require(changed.ContentHash != hash, "Candidate-count changes must affect the content fingerprint.");
        counts[nameof(Role.Lord)] = 0;
        try
        {
            ContentRegistry.Build(new StandardContentPackage(), new CandidatePackage(counts));
            throw new Exception("A nonpositive role candidate count was accepted.");
        }
        catch (InvalidOperationException) { }

        foreach (var size in new[] { 5, 8 })
        foreach (var role in new[] { Role.Lord, Role.Loyalist, Role.Rebel, Role.Renegade })
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 721019, PlayerCount = size, ModeId = $"identity:candidate-{size}", HumanSeat = 0,
                HumanRole = role, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false
            }, registry);
            game.DriveStart();
            var prompt = game.PendingDecision!;
            Require(prompt.Kind == DecisionKind.SelectGeneral && prompt.Choices.Count == Expected(role),
                "The human role must control the number of privately offered generals.");
            Require(game.CreateSnapshot(1).PendingDecision is null,
                "A different viewer must not receive the human's general choices.");
            game.DriveHumanSelectGeneral(prompt.ValidContentIds.Last());
            for (var step = 0; step < 64 && !game.Events.Any(item => item.Payload is SetupCompletedEvent); step++)
                game.DriveAdvanceOneStep();
            Require(game.Events.Any(item => item.Payload is SetupCompletedEvent), "The small selection fixture did not complete setup.");
            var requests = game.Events.Select(item => item.Payload).OfType<GeneralSelectionRequestedEvent>().ToArray();
            var players = game.CreateSnapshot(0, revealAll: true).Players;
            Require(requests.Length == size && requests.All(request =>
                request.CandidateIds.Count == Expected(players[request.ActorSeat].Role!.Value) &&
                request.CandidateIds.Distinct().Count() == request.CandidateIds.Count),
                "AI and human seats must receive the same role-based candidate policy without duplicates.");
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Require(SnapshotJson.Serialize(restored.CreateSnapshot(0)) == SnapshotJson.Serialize(game.CreateSnapshot(0)),
                "Role-based general choices must cold replay exactly.");
        }

        var classic = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
        foreach (var modeId in new[] { "identity:classic-5", "identity:classic-8" })
        {
            var mode = classic.Modes[modeId];
            Require(mode.RoleGeneralCandidateCounts!.All(entry => entry.Value == Expected(Enum.Parse<Role>(entry.Key))),
                "The real classic lobby modes must register the current 13/8/8/13 policy.");
            Require(mode.GeneralVariantGroups!["character:ling-tong"].Contains("classic:ling-tong") &&
                mode.GeneralVariantGroups["character:ling-tong"].Contains("boundary:ling-tong") &&
                new[] { "classic:zhao-yun", "boundary:zhao-yun", "sp:zhao-yun", "classic:shen-zhao-yun", "ol:shen-zhao-yun", "classic:gao-da-yi-hao" }
                    .All(id => mode.GeneralVariantGroups["character:zhao-yun"].Contains(id)),
                "The real classic roster must link both Ling Tong editions and every requested Zhao Yun edition.");
        }

        static int Expected(Role role) => role is Role.Lord or Role.Renegade ? 13 : 8;
    }

    public static void GeneralVariantsPrivacyAndReplay()
    {
        var versions = new List<string> { "fixture:variant-a", "fixture:variant-b", "fixture:variant-c" };
        var groups = new Dictionary<string, IReadOnlyList<string>> { ["character:fixture"] = versions };
        var registry = ContentRegistry.Build(new StandardContentPackage(), new VariantPackage(groups));
        var hash = registry.ContentHash;
        versions.RemoveAt(2);
        Require(registry.Modes[VariantPackage.Mode].GeneralVariantGroups!["character:fixture"].Count == 3 && registry.ContentHash == hash,
            "Variant groups must freeze their nested edition lists.");
        Require(ContentRegistry.Build(new StandardContentPackage(), new VariantPackage(groups)).ContentHash != hash,
            "Changing a permitted family must change the content fingerprint.");
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 5, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = VariantPackage.Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false }, registry);
        game.DriveStart();
        var prompt = game.PendingDecision!;
        Require(prompt.Choices.GroupBy(choice => choice.Parameters["candidate-general-id"]).Count() == 5 &&
            versions.All(id => prompt.ValidContentIds.Contains(id)) && prompt.ValidContentIds.Contains("fixture:variant-c") &&
            game.CreateSnapshot(1).PendingDecision is null,
            "Families must occupy one candidate slot and publish every available edition only to the selecting viewer.");
        var before = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(SnapshotJson.Serialize(before.CreateSnapshot(0)) == SnapshotJson.Serialize(game.CreateSnapshot(0)),
            "The pending family choices must cold replay exactly.");
        var invalid = game.Submit(new SelectGeneralCommand(0, "fixture:not-in-pool", game.Revision, prompt.PromptId));
        Require(!invalid.Accepted && game.PendingDecision!.PromptId == prompt.PromptId, "A related choice must not permit arbitrary registered generals.");
        game.DriveHumanSelectGeneral("fixture:variant-c");
        Require(game.CreateSnapshot(0).Players[0].GeneralId == "fixture:variant-c", "Selecting a version must commit that exact general, not its primary candidate.");
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0)) == SnapshotJson.Serialize(game.CreateSnapshot(0)), "The chosen version must cold replay exactly.");
    }

    public static void RebelKillDrawsThree()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new KillRewardPackage());
        foreach (var role in new[] { Role.Lord, Role.Loyalist, Role.Rebel, Role.Renegade })
        {
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 5, HumanSeat = 0,
                HumanRole = role, ModeId = KillRewardPackage.Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false }, registry);
            game.DriveStart();
            game.DriveHumanSelectGeneral("fixture:kill-owner");
            for (var step = 0; step < 64 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            {
                if (game.PendingDecision is { PlayerSeat: 0 } waiting)
                {
                    var choice = waiting.Choices.FirstOrDefault(candidate => candidate.Cards.Count == 1) ?? waiting.Choices.Last();
                    Require(game.Submit(new AnswerPromptCommand(0, waiting.PromptId, choice.Id, game.Revision)).Accepted,
                        "A real pre-turn response was rejected.");
                }
                else game.DriveAdvanceOneStep();
            }
            var snapshot = game.CreateSnapshot(0, true);
            var rebel = snapshot.Players.First(player => player.Seat != 0 && player.Role == Role.Rebel);
            var hand = snapshot.Players[0].Hand.Count;
            var movements = game.CardMovements.Count;
            var result = game.Submit(new UseProgramSkillCommand(0, "fixture:kill", "kill", [], [rebel.Seat], game.Revision, game.PendingDecision!.PromptId));
            Require(result.Accepted, result.Error?.Message ?? "The actual damage activation was rejected.");
            for (var step = 0; step < 32 && (game.CreateSnapshot(0, true).Players[rebel.Seat].IsAlive ||
                game.CreateSnapshot(0).Players[0].Hand.Count < hand + 3); step++)
            {
                if (game.PendingDecision is { PlayerSeat: 0 } pending)
                {
                    var pass = pending.Choices.Single(choice => choice.Parameters.GetValueOrDefault("response") == "let-die");
                    Require(game.Submit(new AnswerPromptCommand(0, pending.PromptId, pass.Id, game.Revision)).Accepted, "Dying decline was rejected.");
                }
                else game.DriveAdvanceOneStep();
            }
            Require(!game.CreateSnapshot(0, true).Players[rebel.Seat].IsAlive && game.CreateSnapshot(0).Players[0].Hand.Count == hand + 3 &&
                game.CardMovements.Skip(movements).Count(move => move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(0)) == 3,
                "Every identity must receive exactly three physical cards when it kills a Rebel.");
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Require(SnapshotJson.Serialize(restored.CreateSnapshot(0)) == SnapshotJson.Serialize(game.CreateSnapshot(0)), "The Rebel death and its three-card reward must cold replay without paying twice.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class CandidatePackage(IReadOnlyDictionary<string, int> counts) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("identity-candidate-check", new Version(1, 0, 0),
            [new PackageDependency("standard", StandardContentPackage.CurrentVersion)]);

        public void Register(IContentRegistryBuilder builder)
        {
            var pool = Enumerable.Range(0, 24).Select(index => $"fixture:candidate-{index}").ToArray();
            foreach (var id in pool) builder.AddGeneral(new(id, "候选", "supporter", "standard:none", "wei"));
            foreach (var size in new[] { 5, 8 })
                builder.AddMode(new($"identity:candidate-{size}", "Candidate fixture", size, size,
                    new Dictionary<string, int>
                    {
                        [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = size == 5 ? 1 : 2,
                        [nameof(Role.Rebel)] = size == 5 ? 2 : 4, [nameof(Role.Renegade)] = 1
                    }, "standard:basic-demo", GeneralPoolIds: pool) { RoleGeneralCandidateCounts = counts });
        }
    }

    private sealed class VariantPackage(IReadOnlyDictionary<string, IReadOnlyList<string>> groups) : IGameContentPackage
    {
        internal const string Mode = "identity:variant-check-5";
        public PackageManifest Manifest { get; } = new("variant-check", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var pool = new[] { "fixture:variant-a", "fixture:variant-b", "fixture:variant-c", "fixture:other-1", "fixture:other-2", "fixture:other-3", "fixture:other-4" };
            foreach (var id in pool.Append("fixture:not-in-pool")) builder.AddGeneral(new(id, id, "supporter", "standard:none", "wei"));
            builder.AddMode(new(Mode, "版本检查", 5, 5, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 }, "standard:basic-demo", GeneralCandidateCount: 5, GeneralPoolIds: pool)
                { GeneralVariantGroups = groups });
        }
    }

    private sealed class KillRewardPackage : IGameContentPackage
    {
        internal const string Mode = "identity:kill-reward-5";
        public PackageManifest Manifest { get; } = new("kill-reward-check", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:kill","revision":1,
                "activations":[{"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
                "effects":[{"op":"damage","target":"selectedTarget","amount":5}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:kill":{"name":"真实伤害","description":"实际造成致命伤害"}}}""");
            builder.AddSkill(new("fixture:kill", "真实伤害", "实际造成致命伤害") { Program = catalog.Programs["fixture:kill"] });
            var pool = Enumerable.Range(0, 8).Select(index => index == 0 ? "fixture:kill-owner" : $"fixture:kill-other-{index}").ToArray();
            foreach (var id in pool) builder.AddGeneral(new(id, id, "supporter", id == pool[0] ? "fixture:kill" : "standard:none", "wei"));
            builder.AddDeck(new("fixture:kill-deck", "奖励牌堆", 4, 2, [new("standard:slash", 40), new("standard:dodge", 40)]));
            builder.AddMode(new(Mode, "击杀奖励检查", 5, 5, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 }, "fixture:kill-deck", GeneralCandidateCount: 8, GeneralPoolIds: pool));
        }
    }
}
