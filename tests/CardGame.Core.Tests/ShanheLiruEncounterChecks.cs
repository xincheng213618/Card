using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheLiruEncounterChecks
{
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
