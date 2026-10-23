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
}
