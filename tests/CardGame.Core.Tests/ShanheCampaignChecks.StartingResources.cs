using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static partial class ShanheCampaignChecks
{
    public static void StartingResourceFlagFlowsAcrossBattlesAndKeepsLegacyJson()
    {
        var route = RankDefinition();
        var legacy = route with
        {
            StartingNodeIds = ["supply"],
            Nodes = [new("supply", "整备", ShanheNodeKind.Camp, ["first"], Choices: [new("pack", "领取行囊", RewardId: "pack")]), .. route.Nodes],
            Rewards = [new("pack", "行囊", StartingCards: [new(CardKind.Slash, Suit.Spade, 7)], StartingEquipment: [new(CardKind.Crossbow, Suit.Club, 1)])]
        };
        var current = legacy with { StartingResourcesExpireOnUse = true };
        Require(!JsonSerializer.Serialize(legacy).Contains("\"StartingResourcesExpireOnUse\"", StringComparison.Ordinal) &&
            JsonSerializer.Serialize(current).Contains("\"StartingResourcesExpireOnUse\":true", StringComparison.Ordinal) &&
            ShanheCampaignContent.GetContentHash(legacy) != ShanheCampaignContent.GetContentHash(current),
            "The additive resource flag leaves legacy definition JSON unchanged while explicitly fingerprinting the new contract.");
        foreach (var definition in new[] { legacy, current })
        {
            var session = ShanheCampaignSession.Create(definition, new(71, Player), Registry);
            Accept(session, new MoveShanheNodeCommand("supply", session.Revision));
            Accept(session, new ChooseShanheNodeOptionCommand("pack", null, session.Revision));
            foreach (var node in new[] { "first", "second" })
            {
                Accept(session, new MoveShanheNodeCommand(node, session.Revision));
                var setup = session.PendingBattleSetup!;
                Require(setup.StartingResourcesExpireOnUse == definition.StartingResourcesExpireOnUse &&
                    setup.StartingCards is [{ Kind: CardKind.Slash }] && setup.StartingEquipment is [{ Kind: CardKind.Crossbow }] &&
                    !ShanheBattleSetup.ValueEquals(setup, setup with { StartingResourcesExpireOnUse = !setup.StartingResourcesExpireOnUse }),
                    "Every encounter receives the frozen expedition resource contract and original physical resources; recipe equality includes that contract.");
                if (!definition.StartingResourcesExpireOnUse)
                    Require(!JsonSerializer.Serialize(setup).Contains("\"StartingResourcesExpireOnUse\"", StringComparison.Ordinal) &&
                        !JsonSerializer.Serialize(setup).Contains("\"PlayerRank\"", StringComparison.Ordinal),
                        "Legacy encounter JSON omits both the false resource flag and historical null player rank.");
                Cold(session, definition);
                var battle = CreateBattle(setup);
                FinishBattle(battle, "defeat");
                Require(session.CompleteBattle(battle, session.Revision).Accepted, "Each resource recipe must pass a real independently replayed battle completion.");
                Cold(session, definition);
            }
            Require(session.Snapshot.Phase == ShanheCampaignPhase.Won && session.Snapshot.StartingEquipment.Count == 1 &&
                session.Snapshot.StartingCards.Count == 1,
                "The two completed encounters retain their campaign loadout without changing the selected resource contract.");
        }
    }

    public static void StartingResourceCatalogRestoresExactLegacyAndPreLiru()
    {
        var catalog = typeof(StandardShanheCampaignCatalog);
        var legacyBuilder = catalog.GetMethod("CreateLegacyDefinition", BindingFlags.Static | BindingFlags.NonPublic)!;
        var preLiruBuilder = catalog.GetMethod("CreateReturning2026Definition", BindingFlags.Static | BindingFlags.NonPublic)!;
        var codexBuilder = catalog.GetMethod("WithCodexRewards", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var edition in StandardShanheCampaignCatalog.Editions)
        foreach (var codex in new[] { false, true })
        {
            var current = codex ? StandardShanheCampaignCatalog.CreateCodexDefinition(1, edition.Id)
                : StandardShanheCampaignCatalog.CreateDefinition(1, edition.Id);
            Require(current.StartingResourcesExpireOnUse && current.Rewards.Where(r => r.StartingEquipment?.Count > 0).All(r =>
                    r.Description.Contains("加入手牌", StringComparison.Ordinal) && !r.Description.Contains("开始时装备", StringComparison.Ordinal)) &&
                current.Rewards.Where(r => r.StartingCards?.Count > 0 || r.StartingEquipment?.Count > 0).All(r =>
                    r.Description.Contains("先被技能获得", StringComparison.Ordinal) && !r.Description.Contains("普通实体牌流转", StringComparison.Ordinal)),
                "Both new catalog editions and codex compositions publish the hand-entry and post-use resource rule through their actual reward descriptions.");
            var historical = (ShanheCampaignDefinition)legacyBuilder.Invoke(null, [1, edition.Id])!;
            if (codex) historical = (ShanheCampaignDefinition)codexBuilder.Invoke(null, [historical, edition.Id])!;
            Require(!historical.StartingResourcesExpireOnUse && historical.Rewards.Single(r => r.Id == "armor").Description.Contains("每场开始装备八卦阵", StringComparison.Ordinal) &&
                historical.Rewards.Single(r => r.Id == "peach").Description.Contains("本地版按普通实体牌流转", StringComparison.Ordinal),
                "The old constructor retains its original automatic-equipment narrative rather than trying to reverse new presentation text.");
            Verify(current, codex);
            Verify(historical, codex);
            if (codex && edition.Id == StandardShanheCampaignCatalog.Returning2026EditionId)
            {
                var preLiru = (ShanheCampaignDefinition)preLiruBuilder.Invoke(null, [1, false])!;
                preLiru = (ShanheCampaignDefinition)codexBuilder.Invoke(null, [preLiru, edition.Id])!;
                Verify(preLiru, codex);
            }
        }

        static void Verify(ShanheCampaignDefinition original, bool codex)
        {
            var session = ShanheCampaignSession.Create(original, new(83, "classic:guan-yu") { UseCodexRewardPool = codex },
                StandardShanheCampaignCatalog.CreateBattleRegistry);
            var start = session.Snapshot.AvailableNodes.First();
            Accept(session, new MoveShanheNodeCommand(start.Id, session.Revision));
            if (session.Snapshot.Phase == ShanheCampaignPhase.NodeChoice)
                Accept(session, new ChooseShanheNodeOptionCommand(session.Snapshot.NodeChoices.First().Id, null, session.Revision));
            if (session.Snapshot.Phase == ShanheCampaignPhase.Map)
                Accept(session, new MoveShanheNodeCommand(session.Snapshot.AvailableNodes.First().Id, session.Revision));
            Require(session.PendingBattleSetup is { } setup && setup.StartingResourcesExpireOnUse == original.StartingResourcesExpireOnUse,
                "The catalog witness must reach a real current or historical pending battle recipe.");
            var encoded = ShanheCampaignJson.Serialize(session.CreateCheckpoint());
            var checkpoint = ShanheCampaignJson.Deserialize(encoded);
            var recoveredDefinition = StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(checkpoint, 1);
            var restored = ShanheCampaignSession.Restore(checkpoint, recoveredDefinition, StandardShanheCampaignCatalog.CreateBattleRegistry);
            Require(recoveredDefinition.StartingResourcesExpireOnUse == original.StartingResourcesExpireOnUse &&
                ShanheCampaignContent.GetContentHash(recoveredDefinition) == checkpoint.CampaignHash &&
                ShanheBattleSetup.ValueEquals(session.PendingBattleSetup, restored.PendingBattleSetup) &&
                ShanheCampaignJson.Serialize(restored.CreateCheckpoint()) == encoded && Snapshot(restored) == Snapshot(session),
                "Exact restoration preserves the old or new full content hash, unchanged nullable rank, accepted map choices and entire pending recipe.");
            MustThrow(() => StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(checkpoint with { CampaignHash = new string('0', 64) }, 1));
            Require(ShanheCampaignJson.Serialize(checkpoint) == encoded, "Rejected fallback selection leaves its source checkpoint untouched.");
        }
    }
}
