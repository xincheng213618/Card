using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheLiruEncounterChecks
{
    public static void PreLiruCampaignsRestoreOnlyTheirExactDefinitions()
    {
        const string edition = StandardShanheCampaignCatalog.Returning2026EditionId;
        var catalog = typeof(StandardShanheCampaignCatalog);
        var beforeLiru = catalog.GetMethod("CreateReturning2026Definition", BindingFlags.Static | BindingFlags.NonPublic)!;
        var withCodex = catalog.GetMethod("WithCodexRewards", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var tier in new[] { 1, 2, 3 })
        foreach (var codex in new[] { false, true })
        {
            // Exercise the preserved pre-addition constructor, including its original
            // narrative and encounter ordering, rather than removing an encounter from
            // the new definition and leaving its altered story in the fingerprint.
            var historical = (ShanheCampaignDefinition)beforeLiru.Invoke(null, [tier, false])!;
            if (codex) historical = (ShanheCampaignDefinition)withCodex.Invoke(null, [historical, edition])!;
            var current = codex ? StandardShanheCampaignCatalog.CreateCodexDefinition(tier, edition)
                : StandardShanheCampaignCatalog.CreateDefinition(tier, edition);
            Need(ShanheCampaignContent.GetContentHash(historical) != ShanheCampaignContent.GetContentHash(current) &&
                current.Nodes.Single(n => n.Id == "luoyang").Encounters!.Any(e => e.Id == "luoyang:liru-s5"),
                "New expeditions retain the new Li Ru route while historical definitions keep their distinct complete fingerprint.");
            Verify(historical, tier, codex);
            if (tier == 1 && codex)
            {
                var withoutChapters = catalog.GetMethod("WithoutChapterRewards", BindingFlags.Static | BindingFlags.NonPublic)!;
                var withoutInheritance = catalog.GetMethod("WithoutOpeningInheritance", BindingFlags.Static | BindingFlags.NonPublic)!;
                var earlier = (ShanheCampaignDefinition)withoutChapters.Invoke(null, [historical])!;
                earlier = (ShanheCampaignDefinition)withoutInheritance.Invoke(null, [earlier])!;
                Verify(earlier with { Nodes = earlier.Nodes.Select(n => n with { RewardSkipGold = 0 }).ToArray() }, tier, codex);
            }
        }

        static void Verify(ShanheCampaignDefinition historical, int tier, bool codex)
        {
            var session = ShanheCampaignSession.Create(historical, new(83, "classic:guan-yu") { UseCodexRewardPool = codex },
                StandardShanheCampaignCatalog.CreateBattleRegistry);
            var start = session.Snapshot.AvailableNodes.First();
            var moved = session.Submit(new MoveShanheNodeCommand(start.Id, session.Revision));
            Need(moved.Accepted, moved.Error ?? "The historical expedition must retain a real accepted map command.");
            var encoded = ShanheCampaignJson.Serialize(session.CreateCheckpoint());
            var source = ShanheCampaignJson.Deserialize(encoded);
            var definition = StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(source, tier);
            var restored = ShanheCampaignSession.Restore(source, definition, StandardShanheCampaignCatalog.CreateBattleRegistry);
            Need(ShanheCampaignContent.GetContentHash(definition) == source.CampaignHash &&
                definition.Nodes.Single(n => n.Id == "luoyang").Encounters!.All(e => e.Id != "luoyang:liru-s5") &&
                ShanheCampaignJson.Serialize(restored.CreateCheckpoint()) == encoded &&
                JsonSerializer.Serialize(restored.Snapshot) == JsonSerializer.Serialize(session.Snapshot),
                "A pre-Li-Ru expedition must recover its exact old route, content hash, accepted commands and current choices without a silent content upgrade.");
            var rejected = false;
            try { StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(source with { CampaignHash = new string('0', 64) }, tier); }
            catch (ArgumentException) { rejected = true; }
            Need(rejected && ShanheCampaignJson.Serialize(source) == encoded,
                "Historical candidates cannot bypass full fingerprint validation or change the source checkpoint.");
        }
    }

    public static void ActualSingleEnemyRecipeAndConditionalRegistryRecover()
    {
        const string boss = StandardShanheS5LiruPackage.LiruId;
        const string encounterId = "luoyang:liru-s5";
        var skillIds = new[] { "boundary:juece-current", StandardShanheS5LiruPackage.MiejiId, StandardShanheS5LiruPackage.FenchengId };
        foreach (var tier in new[] { 1, 2, 3 })
        {
            var original = StandardShanheCampaignCatalog.CreateDefinition(tier, StandardShanheCampaignCatalog.LocalEditionId);
            Need(original.Nodes.SelectMany(n => n.Encounters ?? []).All(e => e.Id != encounterId && e.Enemies.All(m => m.GeneralId != boss)),
                "The 2024 edition keeps its own original Li Ru rather than importing the S5 encounter.");
            var definition = StandardShanheCampaignCatalog.CreateDefinition(tier, StandardShanheCampaignCatalog.Returning2026EditionId);
            var encounter = definition.Nodes.Single(n => n.Id == "luoyang").Encounters!.Single(e => e.Id == encounterId);
            Need(encounter.Enemies is [{ GeneralId: boss, Hp: null, MaxHpBonus: 0, InitialHandBonus: 0, DrawBonus: 0, SlashBonus: 0, StartingHand: null }] &&
                (encounter.Allies?.Count ?? 0) == 0 && encounter.Enemies.All(m => (m.ExtraSkillIds?.Count ?? 0) == 0),
                "All three local tiers keep the published single-enemy base roster without growth, fixed cards or companions.");
            if (tier != 1) continue;

            var route = new ShanheCampaignDefinition("fixture-shanhe-liru", "毒士谋深入战", ["classic:guan-yu"], ["luoyang"],
                [new("luoyang", "毒士谋深", ShanheNodeKind.BossBattle, [], Encounters: [encounter])], []);
            var campaign = ShanheCampaignSession.Create(route, new(83, "classic:guan-yu"), StandardShanheCampaignCatalog.CreateBattleRegistry);
            var result = campaign.Submit(new MoveShanheNodeCommand("luoyang", campaign.Revision));
            Need(result.Accepted, result.Error ?? "The actual Li Ru map command must prepare a battle.");
            var setup = campaign.PendingBattleSetup!;
            var encoded = ShanheCampaignJson.Serialize(campaign.CreateCheckpoint());
            var restored = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(encoded), route, StandardShanheCampaignCatalog.CreateBattleRegistry);
            Need(ShanheBattleSetup.ValueEquals(setup, restored.PendingBattleSetup) && ShanheCampaignJson.Serialize(restored.CreateCheckpoint()) == encoded,
                "Campaign command replay preserves the exact enemy roster and prepared battle.");

            var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup);
            var game = StandardShanheCampaignCatalog.CreateBattle(setup);
            Need(game.Submit(new StartGameCommand()).Accepted, "The actual native Li Ru battle must start.");
            var npc = game.CreateSnapshot(0).Players.Single(p => p.GeneralId == boss);
            Need(game.PlayerCount == 2 && npc is { Hp: 3, MaxHp: 3, HandCount: 4, Armor: 0 } && npc.Equipment.Count == 0 &&
                npc.Skills!.Select(s => s.ContentId).Order().SequenceEqual(skillIds.Order()) &&
                registry.GetDeck(registry.Modes[ShanheBattleSetup.BattleModeId].DeckId!) is { InitialHandSize: 4, DrawPerTurn: 2 },
                "The real source form starts 3/3, H4/D2, with precisely Juece, the costless Mieji and the repeatable Fencheng.");
            var checkpoint = GameCheckpointJson.Serialize(game.CreateCheckpoint());
            var recovered = GameReplay.Restore(GameCheckpointJson.Deserialize(checkpoint), registry);
            Need(GameCheckpointJson.Serialize(recovered.CreateCheckpoint()) == checkpoint &&
                SnapshotJson.Serialize(recovered.CreateSnapshot(0)) == SnapshotJson.Serialize(game.CreateSnapshot(0)),
                "The native battle recovers its source content fingerprint, command prefix and player view.");

            var plain = setup with { EnemyRoster = [new("classic:cao-cao")] };
            var ordinary = StandardShanheCampaignCatalog.CreateBattleRegistry(plain);
            var originalHash = ordinary.ContentHash;
            foreach (var id in skillIds.Skip(1))
            {
                var equipped = plain with { ExtraSkillIds = [id] };
                var withSkill = StandardShanheCampaignCatalog.CreateBattleRegistry(equipped);
                var equippedGame = StandardShanheCampaignCatalog.CreateBattle(equipped);
                Need(withSkill.Skills.ContainsKey(id) && equippedGame.Submit(new StartGameCommand()).Accepted &&
                    equippedGame.CreateSnapshot(0).Players[0].Skills!.Count(s => s.ContentId == id) == 1,
                    "An explicitly granted S5 skill imports its exact package and can enter a real battle.");
            }
            Need(!ordinary.Generals.ContainsKey(boss) && !ordinary.Skills.ContainsKey(StandardShanheS5LiruPackage.MiejiId) &&
                !ordinary.Skills.ContainsKey(StandardShanheS5LiruPackage.FenchengId) &&
                StandardShanheCampaignCatalog.CreateBattleRegistry(plain).ContentHash == originalHash,
                "Warm boss and extra-skill registries cannot add unearned S5 content to the ordinary recipe cache.");
        }
    }

    private static void Need(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
