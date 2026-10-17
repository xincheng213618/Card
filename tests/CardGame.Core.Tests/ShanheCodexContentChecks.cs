using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheCodexContentChecks
{
    public static void EditionsCheckpointChoiceAndSpecialEquipmentStayPlayable()
    {
        foreach (var edition in StandardShanheCampaignCatalog.Editions)
        foreach (var difficulty in new[] { 1, 2, 3 })
        {
            var codex = StandardShanheCampaignCatalog.CreateCodexDefinition(difficulty, edition.Id);
            var options = new ShanheCampaignOptions(71, "classic:cao-cao")
            {
                UseCodexRewardPool = true,
                UnlockedRewardIds = [.. StandardShanheCampaignCatalog.DefaultCodexUnlockedRewardIds, "codex-57", "codex-88"]
            };
            var session = ShanheCampaignSession.Create(codex, options, StandardShanheCampaignCatalog.CreateBattleRegistry);
            Require(session.Snapshot.Phase == ShanheCampaignPhase.Map && codex.Rewards.Any(reward => reward.Id == "codex-57"),
                "Every difficulty and edition must construct a real codex expedition with its complete registered reward skills.");
            var checkpoint = ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(session.CreateCheckpoint()));
            var restoredDefinition = StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(checkpoint, difficulty);
            var restored = ShanheCampaignSession.Restore(checkpoint, restoredDefinition, StandardShanheCampaignCatalog.CreateBattleRegistry);
            Require(restored.ContentHash == session.ContentHash && restored.CreateCheckpoint().Options.UseCodexRewardPool,
                "The true options flag must rebuild and replay the codex definition under its exact fingerprint.");
            Reject(() => StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(
                checkpoint with { Options = checkpoint.Options with { UseCodexRewardPool = false } }, difficulty));

            var legacyDefinition = StandardShanheCampaignCatalog.CreateDefinition(difficulty, edition.Id);
            var legacy = ShanheCampaignSession.Create(legacyDefinition, new(71, "classic:cao-cao"), StandardShanheCampaignCatalog.CreateBattleRegistry);
            var legacyCheckpoint = ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(legacy.CreateCheckpoint()));
            var legacyRestoredDefinition = StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(legacyCheckpoint, difficulty);
            Require(ShanheCampaignContent.GetContentHash(legacyRestoredDefinition) == legacy.ContentHash &&
                legacyRestoredDefinition.Rewards.All(reward => reward.Id != "codex-57"),
                "The absent/default false flag must retain the historical reward pool and its original fingerprint.");
        }

        var definition = StandardShanheCampaignCatalog.CreateCodexDefinition(1, StandardShanheCampaignCatalog.LocalEditionId);
        var recipe = new ShanheBattleSetup(73, 1, "fixture:codex-equipment", "classic:cao-cao", 4,
            0, 0, 0, 0, [], [], [], [new("classic:ma-chao")], [], ShanheCampaignContent.GetContentHash(definition));
        var oldRegistry = StandardShanheCampaignCatalog.CreateBattleRegistry(recipe);
        Require(!oldRegistry.Skills.ContainsKey("hulao:lion-belt"),
            "A recipe without codex equipment must retain the original battle registration path.");
        foreach (var rewardIds in new[] { new[] { "codex-57", "codex-88", "codex-54" }, new[] { "codex-115", "codex-101" } })
        {
            var equipment = rewardIds.SelectMany(id => definition.Rewards.Single(reward => reward.Id == id).StartingEquipment!).ToArray();
            var setup = recipe with { StartingEquipment = equipment };
            var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup);
            Require(equipment.All(card => registry.Cards.Values.Any(registered => registered.LegacyKind == card.Kind)) &&
                registry.Skills["hulao:lion-belt"].Program is not null && registry.Skills["shenwu:delusion-crown"].Program is not null &&
                registry.ContentHash != oldRegistry.ContentHash,
                "Special equipment recipes need their real physical cards and existing native programs in a separately cached battle registry.");
            var battle = StandardShanheCampaignCatalog.CreateBattle(setup);
            var owner = battle.CreateSnapshot(0).Players[0];
            Require(owner.Equipment.Select(card => new ShanheStartingCard(card.Kind, card.Suit, card.Rank))
                .OrderBy(card => card.Kind).SequenceEqual(equipment.OrderBy(card => card.Kind)),
                "Each purchased special equipment recipe must enter genuine equipment zones with its exact source face.");
            if (equipment.Any(card => card.Kind == CardKind.LionBelt))
                Require(owner.Skills!.Any(skill => skill.ContentId == "hulao:lion-belt") &&
                    owner.Skills!.Any(skill => skill.ContentId == "shenwu:delusion-crown"),
                    "Equipping the belt and crown must grant their actual native programs, not just a display row.");
            var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(battle.CreateCheckpoint())), registry);
            Require(cold.CreateSnapshot(0).Players[0].Equipment.Select(card => card.Kind).SequenceEqual(owner.Equipment.Select(card => card.Kind)),
                "The equipment-bearing battle must also reconstruct from its ordinary cold checkpoint.");
        }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("A checkpoint was accepted under the wrong codex reward pool.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
