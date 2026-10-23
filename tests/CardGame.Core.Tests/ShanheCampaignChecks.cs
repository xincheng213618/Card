using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static partial class ShanheCampaignChecks
{
    internal const string Player = "fixture:shanhe-player";
    internal const string Enemy = "fixture:shanhe-enemy";
    private const string Force = "fixture:shanhe-force";
    private const string Guard = "fixture:shanhe-guard";
    private const string Raid = "fixture:shanhe-raid";

    public static void ContentRoutesAndPublishedNestedListsAreFrozen()
    {
        var enemySkills = new List<string> { Guard };
        var enemies = new List<ShanheCombatantDefinition> { new(Enemy, ExtraSkillIds: enemySkills) };
        var next = new List<string> { "finish" };
        var cards = new List<ShanheStartingCard> { new(CardKind.Slash) };
        var rewards = new List<ShanheRewardDefinition> { new("supply", "粮草", StartingCards: cards) };
        var nodes = new List<ShanheNodeDefinition>
        {
            new("start", "渡口", ShanheNodeKind.Battle, next, [new("one", "守军", enemies)], ["supply"]),
            new("finish", "归乡", ShanheNodeKind.Camp, [], Choices: [new("rest", "归乡")])
        };
        var definition = new ShanheCampaignDefinition("fixture:frozen-campaign", "远征", [Player], ["start"], nodes, rewards);
        var session = Create(definition);
        var hash = session.ContentHash;
        enemySkills.Clear(); enemies.Clear(); next.Clear(); cards.Clear(); rewards.Clear(); nodes.Clear();
        Require(session.Definition.Nodes.Count == 2 && session.Definition.Nodes[0].NextNodeIds.SequenceEqual(["finish"]) &&
            session.Definition.Nodes[0].Encounters![0].Enemies[0].ExtraSkillIds!.SequenceEqual([Guard]) &&
            session.Definition.Rewards[0].StartingCards!.Count == 1 && session.ContentHash == hash,
            "Publishing campaign content must detach every nested package-owned list.");
        RejectMutation(session.Definition.Nodes);
        RejectMutation(session.Definition.Nodes[0].NextNodeIds);
        RejectMutation(session.Definition.Nodes[0].Encounters![0].Enemies);
        RejectMutation(session.Definition.Nodes[0].Encounters![0].Enemies[0].ExtraSkillIds!);
        RejectMutation(session.Snapshot.AvailableNodes);
        var cyclic = session.Definition with
        {
            Nodes = [session.Definition.Nodes[0], session.Definition.Nodes[1] with { NextNodeIds = ["start"] }]
        };
        MustThrow(() => ShanheCampaignContent.FreezeAndValidate(cyclic));
        MustThrow(() => ShanheCampaignContent.FreezeAndValidate(session.Definition with { StartingNodeIds = ["missing"] }));
        Require(ShanheCampaignContent.GetContentHash(session.Definition with { VictoryText = "另一结局" }) != hash,
            "Narrative and route data belong to the campaign fingerprint.");
    }

    public static void StoryShopSkillReplacementAndColdJournalStayAtomic()
    {
        var definition = StoryDefinition();
        var session = Create(definition);
        var before = Snapshot(session);
        Require(!session.Submit(new MoveShanheNodeCommand("final", 0)).Accepted && Snapshot(session) == before,
            "An unavailable node must not change revision or expedition state.");
        Accept(session, new MoveShanheNodeCommand("shop", session.Revision));
        var stockBefore = session.Snapshot.ShopItems;
        Require(stockBefore.Select(item => item.ChoiceId).SequenceEqual(["guard", "raid", "printed"]) &&
            stockBefore.Single(item => item.ChoiceId == "printed").State == ShanheShopItemState.AlreadyOwned &&
            stockBefore.Single(item => item.ChoiceId == "guard") is { CanPurchase: true, GoldCost: 2, RequiresSkillReplacement: false } &&
            stockBefore.All(item => item.Revision == session.Revision),
            "The immutable stock includes an already-owned printed skill and exact prices without hiding unavailable merchandise.");
        RejectMutation(stockBefore);
        var journalBeforeQueries = session.AcceptedCommands.Count;
        for (var query = 0; query < 3; query++) _ = session.Snapshot.ShopItems;
        Require(session.AcceptedCommands.Count == journalBeforeQueries && ReferenceEquals(stockBefore, session.Snapshot.ShopItems),
            "Reading shop eligibility never pays, rerolls stock or issues a command.");
        before = Snapshot(session);
        Require(session.Snapshot.NodeChoices.All(c => c.Id != "printed") &&
            !session.Submit(new ChooseShanheNodeOptionCommand("printed", null, session.Revision)).Accepted && Snapshot(session) == before,
            "A general's printed skill must be excluded from the shop and rejected without paying gold or occupying a slot.");
        Accept(session, new ChooseShanheNodeOptionCommand("guard", null, session.Revision));
        Require(session.Snapshot.Gold == 6 && session.Snapshot.ExtraSkillIds.SequenceEqual([Guard]), "A paid shop skill consumes one slot and its exact gold cost.");
        var raidStock = session.Snapshot.ShopItems.Single(item => item.ChoiceId == "raid");
        Require(session.Snapshot.ShopItems.Single(item => item.ChoiceId == "guard").IsPurchased &&
            raidStock is { CanPurchase: true, RequiresSkillReplacement: true } && raidStock.ReplaceableSkillIds.SequenceEqual([Guard]) &&
            stockBefore.Single(item => item.ChoiceId == "guard").CanPurchase,
            "A purchase preserves its product row, publishes exact replacement choices, and cannot mutate a previously issued stock view.");
        RejectMutation(raidStock.ReplaceableSkillIds);
        before = Snapshot(session);
        Require(!session.Submit(new ChooseShanheNodeOptionCommand("raid", null, session.Revision)).Accepted && Snapshot(session) == before,
            "A full slot requires an explicit legal replacement before the price is paid.");
        Require(!session.Submit(new ChooseShanheNodeOptionCommand("raid", "missing", session.Revision)).Accepted && Snapshot(session) == before,
            "Invalid replacement must leave gold, skills and the shop stock unchanged.");
        Accept(session, new ChooseShanheNodeOptionCommand("raid", Guard, session.Revision));
        Require(session.Snapshot.Gold == 4 && session.Snapshot.ExtraSkillIds.SequenceEqual([Raid]) &&
            session.Snapshot.NodeChoices.Select(c => c.Id).SequenceEqual(["$leave"]), "The purchased shop choices disappear without duplicating skills.");
        Require(!session.Submit(new ChooseShanheNodeOptionCommand("raid", null, session.Revision)).Accepted, "A purchased choice cannot be paid or issued twice.");
        Cold(session, definition);
        VerifyShopAffordabilityAndBuildCapsUseTheRealPreparationPath(definition);

        var duplicateOnly = new ShanheCampaignDefinition("fixture:duplicate-only", "重逢", [Player], ["event"],
        [
            new("event", "故技重逢", ShanheNodeKind.Event, ["end"], Choices: [new("known", "旧有技法", GoldCost: 2, RewardId: "printed")]),
            new("end", "关隘", ShanheNodeKind.Battle, [], [new("end", "守军", [new(Enemy)])])
        ], [new("printed", "本将技能", SkillId: Force)], StartingGold: 5);
        var known = Create(duplicateOnly);
        Accept(known, new MoveShanheNodeCommand("event", 0));
        Require(known.Snapshot.NodeChoices.Select(c => c.Id).SequenceEqual(["$leave"]),
            "A story node with only already-owned skills must still offer a legal continuation.");
        Accept(known, new ChooseShanheNodeOptionCommand("$leave", null, known.Revision));
        Require(known.Snapshot.Gold == 5 && known.Snapshot.ExtraSkillIds.Count == 0 && known.Snapshot.AvailableNodes.Single().Id == "end",
            "Continuing past an unavailable duplicate skill must preserve resources and reach the next route.");
        Cold(known, duplicateOnly);
        var detachedLog = session.AcceptedCommands;
        RejectMutation(detachedLog);
        Accept(session, new ChooseShanheNodeOptionCommand("$leave", null, session.Revision));
        Require(detachedLog.Count == 3 && session.Revision == 4 && session.Snapshot.AvailableNodes.Single().Id == "battle",
            "A published command prefix must stay frozen as the live expedition advances.");
        Cold(session, definition);
    }

    private static void VerifyShopAffordabilityAndBuildCapsUseTheRealPreparationPath(ShanheCampaignDefinition definition)
    {
        var poor = Create(definition with { StartingGold = 1 });
        Accept(poor, new MoveShanheNodeCommand("shop", poor.Revision));
        var before = Snapshot(poor);
        Require(poor.Snapshot.ShopItems.Single(item => item.ChoiceId == "guard") is
                { State: ShanheShopItemState.InsufficientResources, CanAfford: false, CanPurchase: false } &&
            !poor.Submit(new ChooseShanheNodeOptionCommand("guard", null, poor.Revision)).Accepted && before == Snapshot(poor),
            "Insufficient gold is authoritative stock state and the same purchase rejects without mutation.");
        Cold(poor, definition with { StartingGold = 1 });
        var noSlot = Create(definition with { ExtraSkillSlots = 0 });
        Accept(noSlot, new MoveShanheNodeCommand("shop", noSlot.Revision));
        Require(noSlot.Snapshot.ShopItems.Single(item => item.ChoiceId == "guard") is
                { State: ShanheShopItemState.NoSkillSlot, CanAfford: true, CanPurchase: false } &&
            !noSlot.Submit(new ChooseShanheNodeOptionCommand("guard", null, noSlot.Revision)).Accepted,
            "Affordable skills still require a real free or replaceable slot.");
        var cappedDefinition = definition with
        {
            StartingBonuses = new(MaxHp: 40),
            Rewards = definition.Rewards.Append(new("over-cap", "上限已满", Bonuses: new(MaxHp: 1))).ToArray(),
            Nodes = definition.Nodes.Select(node => node.Id == "shop" ? node with
            { Choices = node.Choices!.Append(new("over-cap", "上限已满", RewardId: "over-cap", LeavesNode: false)).ToArray() } : node).ToArray()
        };
        var capped = Create(cappedDefinition);
        Accept(capped, new MoveShanheNodeCommand("shop", capped.Revision));
        before = Snapshot(capped);
        Require(capped.Snapshot.ShopItems.Single(item => item.ChoiceId == "over-cap") is
                { State: ShanheShopItemState.RewardLimitReached, CanAfford: true, CanPurchase: false } &&
            !capped.Submit(new ChooseShanheNodeOptionCommand("over-cap", null, capped.Revision)).Accepted && before == Snapshot(capped),
            "A nominally free item cannot bypass the same build limits enforced by the actual command.");
        Cold(capped, cappedDefinition);
    }

    public static void RealCompletedBattlesGateRewardsAndReplayTheWholeStory()
    {
        var definition = StoryDefinition();
        var session = Create(definition);
        Accept(session, new MoveShanheNodeCommand("shop", session.Revision));
        Accept(session, new ChooseShanheNodeOptionCommand("$leave", null, session.Revision));
        Accept(session, new MoveShanheNodeCommand("battle", session.Revision));
        var setup = session.PendingBattleSetup!;
        var battle = CreateBattle(setup);
        Require(!session.CompleteBattle(battle, session.Revision).Accepted, "An unstarted battle cannot issue victory rewards.");
        FinishBattle(battle, "defeat");
        var checkpoint = battle.CreateCheckpoint();
        var before = Snapshot(session);
        var tampered = checkpoint with { ContentHash = new string('0', checkpoint.ContentHash.Length) };
        Require(!session.Submit(new CompleteShanheBattleCommand(GameCheckpointJson.Serialize(tampered), session.Revision)).Accepted && Snapshot(session) == before,
            "The campaign must retain the ordinary battle content-hash gate.");
        var wrongSetup = checkpoint with { Options = checkpoint.Options with { ShanheBattle = setup with { NodeId = "another-node" } } };
        Require(!session.Submit(new CompleteShanheBattleCommand(GameCheckpointJson.Serialize(wrongSetup), session.Revision)).Accepted && Snapshot(session) == before,
            "A genuine finished battle from another node cannot claim this reward.");
        var revision = session.Revision;
        Require(session.CompleteBattle(battle, revision).Accepted && session.Snapshot.Phase == ShanheCampaignPhase.Reward &&
            session.Snapshot.Gold == 12 && session.Snapshot.ExtraSkillSlots == 2 && session.Snapshot.RewardOffers.Select(r => r.Id).SequenceEqual(["training"]),
            "Only an exact replayed victory grants its encounter gold, capacity and reward prompt.");
        Require(!session.CompleteBattle(battle, revision).Accepted && session.Snapshot.Gold == 12, "Duplicate battle completion cannot pay again.");
        Cold(session, definition);
        Accept(session, new ChooseShanheRewardCommand("training", null, session.Revision));
        Require(session.Snapshot.Bonuses is { MaxHp: 1, Draw: 1, Slash: 1, InitialHand: 1 } && session.Snapshot.MaxHp == 5 && session.Snapshot.Hp == 4,
            "A tactic reward grows the persistent maximum and other attributes without implicitly recovering health.");
        Accept(session, new MoveShanheNodeCommand("camp", session.Revision));
        var campBefore = Snapshot(session);
        Require(!session.Submit(new ChooseShanheNodeOptionCommand("rest", "missing", session.Revision)).Accepted && Snapshot(session) == campBefore,
            "A failed camp choice must not retain its gold debit or healing.");
        Accept(session, new ChooseShanheNodeOptionCommand("rest", null, session.Revision));
        Require(session.Snapshot.Gold == 10 && session.Snapshot.Hp == 5, "The camp pays its content-defined cost and caps healing at the expedition maximum.");
        Accept(session, new MoveShanheNodeCommand("story", session.Revision));
        Accept(session, new ChooseShanheNodeOptionCommand("aid", null, session.Revision));
        Require(session.Snapshot.Gold == 13 && session.Snapshot.Hp == 4 && session.Snapshot.StartingCards.Count == 1 && session.Snapshot.StartingEquipment.Count == 1,
            "Camp and story choices apply their healing, health price, supplies and equipment once.");
        Cold(session, definition);
        Accept(session, new MoveShanheNodeCommand("final", session.Revision));
        Require(session.PendingBattleSetup is { Hp: 4, MaxHpBonus: 1, DrawBonus: 1, SlashBonus: 1, InitialHandBonus: 1 } final &&
            final.StartingCards.Single().Kind == CardKind.Slash && final.StartingEquipment.Single().Kind == CardKind.Crossbow,
            "The next real encounter receives the exact persistent opening recipe.");
        var finalBattle = CreateBattle(session.PendingBattleSetup!);
        FinishBattle(finalBattle, "defeat");
        Require(session.CompleteBattle(finalBattle, session.Revision).Accepted && session.Snapshot.Phase == ShanheCampaignPhase.Reward &&
            session.Snapshot.ExtraSkillSlots == 3 && session.Snapshot.RewardOffers.Single().Id == "ending",
            "The last boss must grant its skill slot and retain its final reward prompt before ending the expedition.");
        Cold(session, definition);
        Accept(session, new ChooseShanheRewardCommand("ending", null, session.Revision));
        Require(session.Snapshot.Phase == ShanheCampaignPhase.Won && session.Snapshot.Gold == 15 &&
            session.Snapshot.EndingText == "洛阳已定，百姓归家。" && session.Snapshot.CompletedNodeIds.Count == 5 &&
            session.Snapshot.StartingEquipment.Single().Kind == CardKind.QinglongCrescentBlade,
            "The route ending follows the final reward, with equipment replacing the old item in the same slot.");
        Cold(session, definition);
        Require(!session.Submit(new MoveShanheNodeCommand("shop", session.Revision)).Accepted, "A completed expedition cannot restart its map through a stray command.");

        var loss = Create(definition);
        Accept(loss, new MoveShanheNodeCommand("shop", loss.Revision));
        Accept(loss, new ChooseShanheNodeOptionCommand("$leave", null, loss.Revision));
        Accept(loss, new MoveShanheNodeCommand("battle", loss.Revision));
        var losingBattle = CreateBattle(loss.PendingBattleSetup!);
        FinishBattle(losingBattle, "surrender");
        Require(loss.CompleteBattle(losingBattle, loss.Revision).Accepted && loss.Snapshot.Phase == ShanheCampaignPhase.Lost &&
            loss.Snapshot.Gold == 8 && loss.Snapshot.RewardOffers.Count == 0 && loss.Snapshot.EndingText == definition.DefeatText,
            "A real defeat ends the run without awarding gold or a loot prompt.");
        Cold(loss, definition);
    }

    public static void SeededEntryEncounterAndContentFingerprintRemainStable()
    {
        var encounters = new ShanheEncounterDefinition[]
        {
            new("east", "东路", [new(Enemy)], VictoryGold: 2),
            new("west", "西路", [new(Enemy, MaxHpBonus: 1)], VictoryGold: 3)
        };
        var definition = new ShanheCampaignDefinition("fixture:seeded", "分路远征", [Player], ["east", "west"],
        [
            new("east", "虎牢", ShanheNodeKind.Battle, [], encounters),
            new("west", "广成", ShanheNodeKind.Battle, [], encounters)
        ], []) { RandomStartingNode = true };
        var first = Create(definition);
        var second = Create(definition);
        Require(first.Snapshot.AvailableNodes.Count == 1 && Snapshot(first) == Snapshot(second), "The same seed chooses one reproducible entry node.");
        var entry = first.Snapshot.AvailableNodes.Single().Id;
        Accept(first, new MoveShanheNodeCommand(entry, 0));
        Accept(second, new MoveShanheNodeCommand(entry, 0));
        Require(ShanheBattleSetup.ValueEquals(first.PendingBattleSetup, second.PendingBattleSetup), "Encounter selection and its battle seed must remain deterministic.");
        RejectMutation(first.PendingBattleSetup!.EnemyRoster);
        RejectMutation(first.PendingBattleSetup.ExtraSkillIds);
        Cold(first, definition);
        var saved = first.CreateCheckpoint();
        MustThrow(() => ShanheCampaignSession.Restore(saved with { CampaignHash = "different" }, definition, Registry));
        MustThrow(() => ShanheCampaignSession.Restore(saved, definition with { IntroText = "changed story" }, Registry));
        MustThrow(() => ShanheCampaignSession.Restore(saved with { Revision = saved.Revision + 1 }, definition, Registry));
    }

    public static void DecliningPublishedRewardsPaysOnceAndReplaysWithoutReroll()
    {
        var definition = new ShanheCampaignDefinition("fixture:skip-reward", "取舍", [Player], ["start"],
        [
            new("start", "首战", ShanheNodeKind.Battle, ["end"], [new("first", "守军", [new(Enemy)], VictoryGold: 7)],
                ["guard", "raid", "opening"]) { RewardSkipGold = 100 },
            new("end", "决战", ShanheNodeKind.BossBattle, [], [new("last", "守军", [new(Enemy)], VictoryGold: 11)],
                ["guard", "raid", "opening"]) { RewardSkipGold = 100 }
        ], [new("guard", "守御", SkillId: Guard), new("raid", "突袭", SkillId: Raid),
            new("opening", "行囊", Bonuses: new(InitialHand: 1))], StartingGold: 8, ExtraSkillSlots: 0);
        var session = Create(definition);
        var before = Snapshot(session);
        Require(!session.Snapshot.CanSkipReward && session.Snapshot.RewardSkipGold == 0 &&
            !session.Submit(new SkipShanheRewardCommand(0)).Accepted && Snapshot(session) == before,
            "A map has no reward to decline and cannot issue compensation.");
        foreach (var nodeId in new[] { "start", "end" })
        {
            Accept(session, new MoveShanheNodeCommand(nodeId, session.Revision));
            before = Snapshot(session);
            Require(!session.Submit(new SkipShanheRewardCommand(session.Revision)).Accepted && Snapshot(session) == before,
                "An unfinished battle cannot be skipped for reward currency.");
            var battle = CreateBattle(session.PendingBattleSetup!); FinishBattle(battle, "defeat");
            Require(session.CompleteBattle(battle, session.Revision).Accepted, "Only a real completed victory opens the reward offer.");
            var offered = session.Snapshot;
            var offerIds = offered.RewardOffers.Select(reward => reward.Id).ToArray();
            Require(offered.Phase == ShanheCampaignPhase.Reward && offered.CanSkipReward && offered.RewardSkipGold == 100 &&
                offerIds.Length == 3 && offered.Gold == (nodeId == "start" ? 15 : 126),
                "The three frozen candidates and separate base victory gold precede the optional one-hundred compensation.");
            Cold(session, definition);
            session = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(session.CreateCheckpoint())), definition, Registry);
            Require(session.Snapshot.RewardOffers.Select(reward => reward.Id).SequenceEqual(offerIds),
                "Cold restoration neither redraws nor replaces the published candidate order.");
            var revision = session.Revision;
            Accept(session, new SkipShanheRewardCommand(revision));
            Require(session.Snapshot.Gold == offered.Gold + 100 && !session.Snapshot.CanSkipReward &&
                session.Snapshot.RewardSkipGold == 0 && session.Snapshot.RewardOffers.Count == 0 &&
                session.Snapshot.ExtraSkillIds.Count == 0 && session.Snapshot.Bonuses == new ShanheTacticBonuses() &&
                session.Snapshot.CompletedNodeIds.Count(id => id == nodeId) == 1 && offered.CanSkipReward &&
                offered.RewardOffers.Select(reward => reward.Id).SequenceEqual(offerIds),
                "Declining pays once, grants no candidate or slot, finishes exactly this node and leaves the old view immutable.");
            before = Snapshot(session);
            Require(!session.Submit(new SkipShanheRewardCommand(revision)).Accepted &&
                !session.Submit(new SkipShanheRewardCommand(session.Revision)).Accepted &&
                !session.Submit(new ChooseShanheRewardCommand(offerIds[0], null, session.Revision)).Accepted &&
                !session.CompleteBattle(battle, session.Revision).Accepted && Snapshot(session) == before,
                "Stale input, a repeated decline, an old reward and repeated victory proof cannot claim this offer twice.");
            Cold(session, definition);
        }
        Require(session.Snapshot.Phase == ShanheCampaignPhase.Won && session.Snapshot.Gold == 226 &&
            session.AcceptedCommands.OfType<SkipShanheRewardCommand>().Count() == 2,
            "The final declined reward closes the expedition and both exact commands survive cold journal replay.");
        var disabled = definition with { Nodes = definition.Nodes.Select(node => node with { RewardSkipGold = 0 }).ToArray() };
        var disabledRun = Create(disabled); Accept(disabledRun, new MoveShanheNodeCommand("start", 0));
        var disabledBattle = CreateBattle(disabledRun.PendingBattleSetup!); FinishBattle(disabledBattle, "defeat");
        Require(disabledRun.CompleteBattle(disabledBattle, disabledRun.Revision).Accepted, "Legacy content still offers ordinary rewards.");
        before = Snapshot(disabledRun);
        Require(!disabledRun.Snapshot.CanSkipReward && !disabledRun.Submit(new SkipShanheRewardCommand(disabledRun.Revision)).Accepted &&
            Snapshot(disabledRun) == before, "A zero compensation definition cannot be upgraded by a forged command.");
        MustThrow(() => ShanheCampaignSession.Restore(session.CreateCheckpoint(), disabled, Registry));
        MustThrow(() => ShanheCampaignContent.FreezeAndValidate(definition with
        { Nodes = definition.Nodes.Select(node => node with { RewardSkipGold = -1 }).ToArray() }));
        VerifyLegacyCompensationDefinitionKeepsExactFingerprint();
    }

    private static void VerifyLegacyCompensationDefinitionKeepsExactFingerprint()
    {
        foreach (var edition in StandardShanheCampaignCatalog.Editions)
        {
            var current = StandardShanheCampaignCatalog.CreateDefinition(1, edition.Id);
            var legacy = current with { Nodes = current.Nodes.Select(node => node with { RewardSkipGold = 0 }).ToArray() };
            var saved = ShanheCampaignSession.Create(legacy, new(71, legacy.PlayerGeneralIds[0]),
                StandardShanheCampaignCatalog.CreateBattleRegistry).CreateCheckpoint();
            var reconstructed = StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(saved, 1);
            Require(reconstructed.Nodes.All(node => node.RewardSkipGold == 0) &&
                ShanheCampaignContent.GetContentHash(reconstructed) == saved.CampaignHash &&
                !JsonSerializer.Serialize(reconstructed).Contains("RewardSkipGold", StringComparison.Ordinal),
                "A pre-compensation save selects the exact legacy definition; optional zero fields preserve the original hash shape.");
            _ = ShanheCampaignSession.Restore(saved, reconstructed, StandardShanheCampaignCatalog.CreateBattleRegistry);
            MustThrow(() => StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(saved with { CampaignHash = "different" }, 1));
        }
    }

    public static void TacticsUseIndependentGrantsAndRejectAliases()
    {
        var definition = new ShanheCampaignDefinition("fixture:tactics", "战法构筑", [Player], ["shop"],
        [
            new("shop", "军议", ShanheNodeKind.Shop, ["battle"], Choices:
            [
                new("skill", "传习突袭", GoldCost: 2, RewardId: "skill", LeavesNode: false),
                new("tactic", "习得反刺", GoldCost: 1, RewardId: "tactic", LeavesNode: false),
                new("alias", "再次反刺", GoldCost: 3, RewardId: "alias", LeavesNode: false)
            ]),
            new("battle", "决战", ShanheNodeKind.BossBattle, [], [new("gate", "守军", [new(Enemy)])], ["alias", "gold"])
        ],
        [
            new("skill", "武将技能", SkillId: Raid),
            new("tactic", "反刺") { TacticSkillId = Guard },
            new("alias", "反刺别名") { TacticSkillId = Guard },
            new("gold", "军资", Gold: 2)
        ], StartingGold: 10, ExtraSkillSlots: 1);
        var session = Create(definition);
        Accept(session, new MoveShanheNodeCommand("shop", 0));
        Accept(session, new ChooseShanheNodeOptionCommand("skill", null, session.Revision));
        Accept(session, new ChooseShanheNodeOptionCommand("tactic", null, session.Revision));
        Require(session.Snapshot.ExtraSkillSlots == 1 && session.Snapshot.ExtraSkillIds.SequenceEqual([Raid]) &&
            session.Snapshot.TacticIds.SequenceEqual(["tactic"]) && session.Snapshot.Gold == 7 &&
            session.Snapshot.NodeChoices.All(c => c.Id != "alias"),
            "A tactic preserves its reward provenance while granting a separate program without using the full general-skill slot.");
        var before = Snapshot(session);
        Require(!session.Submit(new ChooseShanheNodeOptionCommand("alias", null, session.Revision)).Accepted && Snapshot(session) == before,
            "A second reward id for the same tactic program must be rejected before costs or grants commit.");
        RejectMutation(session.Snapshot.TacticIds);
        Cold(session, definition);
        Accept(session, new ChooseShanheNodeOptionCommand("$leave", null, session.Revision));
        Accept(session, new MoveShanheNodeCommand("battle", session.Revision));
        var pending = session.PendingBattleSetup!;
        Require(pending.TacticSkillIds.SequenceEqual([Guard]) && pending.ExtraSkillIds.SequenceEqual([Raid]) &&
            !ShanheBattleSetup.ValueEquals(pending, pending with { TacticSkillIds = [] }),
            "The encounter recipe and structural setup validation must include its independent tactic programs.");
        RejectMutation(pending.TacticSkillIds);
        Cold(session, definition);
        var battle = CreateBattle(pending);
        FinishBattle(battle, "defeat");
        Require(session.CompleteBattle(battle, session.Revision).Accepted && session.Snapshot.RewardOffers.Select(r => r.Id).SequenceEqual(["gold"]),
            "A completed battle must exclude aliases of previously acquired tactic programs from its offered rewards.");
        Accept(session, new ChooseShanheRewardCommand("gold", null, session.Revision));
        Cold(session, definition);
        MustThrow(() => Create(definition with { Rewards = [.. definition.Rewards, new("missing", "缺失战法") { TacticSkillId = "missing:program" }] }));
    }

    private static ShanheCampaignDefinition StoryDefinition() => new("fixture:story", "讨董行记", [Player], ["shop"],
    [
        new("shop", "行商", ShanheNodeKind.Shop, ["battle"], Choices:
        [new("guard", "学守御", 2, RewardId: "guard", LeavesNode: false), new("raid", "学突袭", 2, RewardId: "raid", LeavesNode: false),
            new("printed", "重复本将技能", 2, RewardId: "printed", LeavesNode: false)]) { StoryText = "行商展开了行囊。" },
        new("battle", "渡口守军", ShanheNodeKind.Battle, ["camp"], [new("gate", "守军", [new(Enemy)], VictoryGold: 4)], ["training", "printed"])
            { AddSkillSlotsOnVictory = 1, StoryText = "守军拦住去路。" },
        new("camp", "营地", ShanheNodeKind.Camp, ["story"], Choices: [new("rest", "休整", GoldCost: 2, RewardId: "heal")]),
        new("story", "受难村庄", ShanheNodeKind.Event, ["final"], Choices: [new("aid", "协助村民", HpCost: 1, RewardId: "supplies")]),
        new("final", "洛阳", ShanheNodeKind.BossBattle, [], [new("dong", "董卓军", [new(Enemy)])], ["ending"])
            { VictoryText = "洛阳已定，百姓归家。", AddSkillSlotsOnVictory = 1 }
    ],
    [
        new("guard", "守御", SkillId: Guard), new("raid", "突袭", SkillId: Raid), new("printed", "本将技能", SkillId: Force),
        new("training", "操练", Bonuses: new(1, 1, 1, 1)), new("heal", "休养", Heal: 3),
        new("supplies", "村民馈赠", Gold: 3, StartingCards: [new(CardKind.Slash)], StartingEquipment: [new(CardKind.Crossbow)]),
        new("ending", "缴获军械", Gold: 2, StartingEquipment: [new(CardKind.QinglongCrescentBlade)])
    ], StartingGold: 8, ExtraSkillSlots: 1) { IntroText = "提剑出发。", VictoryText = "远征结束。", DefeatText = "此役失利，来日再战。" };

    private static ShanheCampaignSession Create(ShanheCampaignDefinition definition) => ShanheCampaignSession.Create(definition, new(71, Player), Registry);
    internal static ContentRegistry Registry(ShanheBattleSetup setup) => setup.RebirthAvailable
        ? ContentRegistry.Build(new StandardContentPackage(), new FixtureBattlePackage(), new StandardShanheRankPrivilegePackage())
        : ContentRegistry.Build(new StandardContentPackage(), new FixtureBattlePackage());
    internal static GameEngine CreateBattle(ShanheBattleSetup setup) => GameEngine.CreateStandard(new()
    {
        Seed = setup.BattleSeed, PlayerCount = 1 + setup.EnemyRoster.Count + setup.AllyRoster.Count, HumanSeat = 0,
        HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
        ShanheBattle = setup, AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false,
        AiPolicyVersion = AiDecisionPolicy.CurrentVersion, MaxTurns = 12
    }, Registry(setup));
    internal static void FinishBattle(GameEngine game, string action)
    {
        Accept(game, new StartGameCommand());
        for (var step = 0; step < 50 && game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }, "The bounded fixture must reach the player's real play prompt.");
        Accept(game, new UseProgramSkillCommand(0, Force, action, [], action == "defeat" ? [game.State.Players.Last().Seat] : [], game.Revision, game.PendingDecision!.PromptId));
        for (var step = 0; step < 50 && game.State.Status != EngineStatus.Completed; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(game.State.Status == EngineStatus.Completed, "The fixture's real death must finish through normal command resolution.");
    }
    private static void Cold(ShanheCampaignSession session, ShanheCampaignDefinition definition)
    {
        var restored = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(session.CreateCheckpoint())), definition, Registry);
        Require(Snapshot(session) == Snapshot(restored), "Cold campaign replay must preserve map, choices, skills, opening recipes, story and battle identity.");
    }
    private static string Snapshot(ShanheCampaignSession session) => JsonSerializer.Serialize(session.Snapshot);
    private static void Accept(ShanheCampaignSession session, ShanheCampaignCommand command)
    {
        var result = session.Submit(command);
        Require(result.Accepted, result.Error ?? "Campaign command rejected.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.ToString() ?? "Battle command rejected.");
    }
    private static void RejectMutation<T>(IReadOnlyList<T> values)
    {
        Require(values is IList<T> { IsReadOnly: true }, "Published campaign collections must be read-only.");
        if (values.Count == 0) return;
        try { ((IList<T>)values)[0] = values[0]; }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("A published campaign collection accepted mutation.");
    }
    private static void MustThrow(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; }
        throw new InvalidOperationException("Invalid campaign content/checkpoint was accepted.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class FixtureBattlePackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-battle", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var programs = new object[]
            {
                new { id = Force, revision = 1, activations = new object[]
                {
                    new { id = "defeat", minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1, targetKind = "otherLiving", usesPerTurn = (int?)null,
                        effects = new[] { new { op = "loseHp", target = "selectedTarget", amount = 20 } } },
                    new { id = "surrender", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null,
                        effects = new[] { new { op = "loseHp", target = "owner", amount = 20 } } }
                } },
                new { id = Guard, revision = 1, modifiers = new[] { new { id = "guard", query = "attackRange", operation = "add", value = 1, priority = 0 } } },
                new { id = Raid, revision = 1, modifiers = new[] { new { id = "raid", query = "attackRange", operation = "add", value = 1, priority = 0 } } }
            };
            var catalog = SkillProgramCatalog.Load(
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = programs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new[] { Force, Guard, Raid }.ToDictionary(id => id, id => new { name = id, description = "Bounded Shanhe behavior fixture" }) }));
            foreach (var entry in catalog.Programs) builder.AddSkill(new(entry.Key, entry.Key, "fixture") { Program = entry.Value });
            builder.AddGeneral(new(Player, "行者", "", Force, "shu"));
            builder.AddGeneral(new(Enemy, "守军", "", Guard, "qun"));
            builder.AddDeck(new("fixture:shanhe-dodge", "闪牌堆", 4, 2, [new("standard:dodge", 40)]));
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "山河战斗", 2, 2, new Dictionary<string, int>(),
                "fixture:shanhe-dodge", GeneralPoolIds: [Player, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 1 }));
        }
    }
}
