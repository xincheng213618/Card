using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheThreeBossEncounterChecks
{
    public static void PreviousExpeditionsKeepExactRoutesAndPendingRecipesCold()
    {
        const string edition = StandardShanheCampaignCatalog.Returning2026EditionId;
        var catalog = typeof(StandardShanheCampaignCatalog);
        var previous = catalog.GetMethod("CreateReturning2026Definition", BindingFlags.Static | BindingFlags.NonPublic)!;
        var codex = catalog.GetMethod("WithCodexRewards", BindingFlags.Static | BindingFlags.NonPublic)!;
        var resources = catalog.GetMethod("WithStartingResourcesContract", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var tier in new[] { 1, 2, 3 })
        foreach (var useCodex in new[] { false, true })
        {
            var old = (ShanheCampaignDefinition)previous.Invoke(null, [tier])!;
            if (useCodex) old = (ShanheCampaignDefinition)codex.Invoke(null, [old, edition])!;
            var unsupportedOlderHash = ShanheCampaignContent.GetContentHash(old);
            old = (ShanheCampaignDefinition)resources.Invoke(null, [old])!;
            var current = useCodex ? StandardShanheCampaignCatalog.CreateCodexDefinition(tier, edition)
                : StandardShanheCampaignCatalog.CreateDefinition(tier, edition);
            Need(ShanheCampaignContent.GetContentHash(old) != ShanheCampaignContent.GetContentHash(current),
                "The new encounter pool has a distinct complete content fingerprint.");
            var session = ShanheCampaignSession.Create(old, new(83, "classic:guan-yu") { UseCodexRewardPool = useCodex },
                StandardShanheCampaignCatalog.CreateBattleRegistry);
            Need(session.Submit(new MoveShanheNodeCommand(session.Snapshot.AvailableNodes.First().Id, session.Revision)).Accepted,
                "The previous expedition accepts its real departure command.");
            if (session.Snapshot.Phase == ShanheCampaignPhase.NodeChoice)
                Need(session.Submit(new ChooseShanheNodeOptionCommand(session.Snapshot.NodeChoices.First().Id, null, session.Revision)).Accepted,
                    "The old departure choice enters its real pending encounter.");
            if (session.PendingBattleSetup is null)
                Need(session.Submit(new MoveShanheNodeCommand(session.Snapshot.AvailableNodes.First().Id, session.Revision)).Accepted,
                    "Leaving the departure enters the original route's real battle.");
            Need(session.PendingBattleSetup is not null, "Compatibility is exercised with a prepared battle recipe.");
            var encoded = ShanheCampaignJson.Serialize(session.CreateCheckpoint());
            var checkpoint = ShanheCampaignJson.Deserialize(encoded);
            var selected = StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(checkpoint, tier);
            var recovered = ShanheCampaignSession.Restore(checkpoint, selected, StandardShanheCampaignCatalog.CreateBattleRegistry);
            Need(ShanheCampaignContent.GetContentHash(selected) == checkpoint.CampaignHash &&
                !selected.Nodes.SelectMany(n => n.Encounters ?? []).Any(IsNewEncounter) &&
                selected.StartingResourcesExpireOnUse && ShanheCampaignJson.Serialize(recovered.CreateCheckpoint()) == encoded &&
                ShanheBattleSetup.ValueEquals(session.PendingBattleSetup, recovered.PendingBattleSetup),
                "All old routes, current resource semantics, accepted commands and exact pending recipes survive cold recovery without new bosses.");
            var rejected = false;
            try { StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(checkpoint with { CampaignHash = new string('0', 64) }, tier); }
            catch (ArgumentException) { rejected = true; }
            Need(rejected && ShanheCampaignJson.Serialize(checkpoint) == encoded, "Fallbacks still require the full original fingerprint.");
            rejected = false;
            try { StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(checkpoint with { CampaignHash = unsupportedOlderHash }, tier); }
            catch (ArgumentException) { rejected = true; }
            Need(rejected, "Earlier starting-resource definitions are no longer selected as a compatibility fallback.");
        }
    }

    public static void ActualEncounterRecipesAndConditionalPackagesRecoverCold()
    {
        const string edition = StandardShanheCampaignCatalog.Returning2026EditionId;
        var current = StandardShanheCampaignCatalog.CreateDefinition(1, edition);
        var encounters = current.Nodes.SelectMany(n => n.Encounters ?? []).Where(IsNewEncounter).ToArray();
        Need(encounters.Length == 3 && StandardShanheCampaignCatalog.CreateDefinition(1, StandardShanheCampaignCatalog.LocalEditionId)
            .Nodes.SelectMany(n => n.Encounters ?? []).All(e => !IsNewEncounter(e)), "Only the 2026 edition adds the three source forms.");
        foreach (var encounter in encounters)
        {
            Need(encounter.Enemies.Count == 1 && (encounter.Allies?.Count ?? 0) == 0,
                "The actual local recipe preserves a single enemy without an invented companion.");
            var route = new ShanheCampaignDefinition("fixture-shanhe-three-bosses", "S5单敌配方", ["classic:guan-yu"], ["boss"],
                [new("boss", "首领", ShanheNodeKind.Battle, [], Encounters: [encounter])], []);
            var session = ShanheCampaignSession.Create(route, new(83, "classic:guan-yu"), StandardShanheCampaignCatalog.CreateBattleRegistry);
            Need(session.Submit(new MoveShanheNodeCommand("boss", session.Revision)).Accepted, "The real map command prepares the new encounter.");
            var setup = session.PendingBattleSetup!;
            var encoded = ShanheCampaignJson.Serialize(session.CreateCheckpoint());
            var restored = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(encoded), route, StandardShanheCampaignCatalog.CreateBattleRegistry);
            Need(ShanheBattleSetup.ValueEquals(setup, restored.PendingBattleSetup) && ShanheCampaignJson.Serialize(restored.CreateCheckpoint()) == encoded,
                "Each new pending roster recovers through actual campaign commands.");
            var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup);
            var game = StandardShanheCampaignCatalog.CreateBattle(setup);
            Need(game.Submit(new StartGameCommand()).Accepted && game.PlayerCount == 2 && registry.Generals.ContainsKey(encounter.Enemies[0].GeneralId),
                "Each conditionally composed content package starts its actual native battle.");
            var saved = GameCheckpointJson.Serialize(game.CreateCheckpoint());
            var recovered = GameReplay.Restore(GameCheckpointJson.Deserialize(saved), registry);
            Need(GameCheckpointJson.Serialize(recovered.CreateCheckpoint()) == saved &&
                SnapshotJson.Serialize(recovered.CreateSnapshot(0)) == SnapshotJson.Serialize(game.CreateSnapshot(0)),
                "The native game restores its content hash, command prefix and player view.");
            var plain = setup with { EnemyRoster = [new("classic:cao-cao")] };
            var ordinary = StandardShanheCampaignCatalog.CreateBattleRegistry(plain);
            var ordinaryHash = ordinary.ContentHash;
            foreach (var skill in registry.Generals[encounter.Enemies[0].GeneralId].SkillIds)
                Need(StandardShanheCampaignCatalog.CreateBattleRegistry(plain with { ExtraSkillIds = [skill] }).Skills.ContainsKey(skill),
                    "An explicit skill grant composes its package even without its general.");
            Need(!ordinary.Generals.ContainsKey(encounter.Enemies[0].GeneralId) &&
                StandardShanheCampaignCatalog.CreateBattleRegistry(plain).ContentHash == ordinaryHash,
                "Warming boss and skill registries cannot change an ordinary recipe's content.");
        }
    }

    private static bool IsNewEncounter(ShanheEncounterDefinition encounter) => encounter.Id is
        "guangcheng:zhangliang-s5" or "dagu:yuanshao-s5" or "huanyuan:xuchu-s5";

    private static void Need(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
