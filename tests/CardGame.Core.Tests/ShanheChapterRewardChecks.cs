using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheChapterRewardChecks
{
    public static void ThreeBossBundlesFreezeBothChoicesAndRequireExplicitChapterConfirmation()
    {
        var definition = FixtureDefinition();
        var session = Create(definition);
        for (var chapter = 1; chapter <= 3; chapter++)
        {
            var node = "boss-" + chapter;
            Accept(session, new MoveShanheNodeCommand(node, session.Revision));
            var battle = ShanheCampaignChecks.CreateBattle(session.PendingBattleSetup!);
            ShanheCampaignChecks.FinishBattle(battle, "defeat");
            var gold = session.Snapshot.Gold;
            var slots = session.Snapshot.ExtraSkillSlots;
            Require(session.CompleteBattle(battle, session.Revision).Accepted, "A real native fixture victory must enter the chapter bundle.");
            var summary = session.Snapshot.BossRewardSummary ?? throw new InvalidOperationException("A chapter victory must publish its summary.");
            Require(session.Snapshot.Phase == ShanheCampaignPhase.Reward &&
                summary.ChapterNumber == chapter && summary.FixedGold == 500 && summary.SkillSlots == 1 &&
                session.Snapshot.Gold == gold + 500 && session.Snapshot.ExtraSkillSlots == slots + 1 &&
                summary.Groups.Count == 2 && summary.Groups.All(group => group.Offers.Count == 3 && !group.IsResolved) &&
                !summary.CanConfirm && session.Snapshot.AvailableNodes.Count == 0 &&
                session.Snapshot.RewardOffers.Count == 0 && !session.Snapshot.CanSkipReward,
                "Each chapter boss credits exactly 500 and one slot once, then freezes separate tactic/card three-choice groups before advancing.");
            RejectMutation(summary.Groups);
            foreach (var group in summary.Groups) RejectMutation(group.Offers);
            Cold(session, definition);
            var before = Snapshot(session);
            Require(!session.CompleteBattle(battle, session.Revision).Accepted &&
                !session.Submit(new SkipShanheRewardCommand(session.Revision)).Accepted &&
                !session.Submit(new ChooseShanheRewardCommand(summary.Groups[0].Offers[0].Id, null, session.Revision)).Accepted &&
                !session.Submit(new ConfirmShanheBossRewardCommand(session.Revision)).Accepted &&
                !session.Submit(new MoveShanheNodeCommand("boss-" + (chapter + 1), session.Revision)).Accepted && Snapshot(session) == before,
                "Duplicate victory, ordinary reward commands, skip, early confirmation and direct movement cannot bypass the boss bundle.");

            var cards = summary.Groups.Single(group => group.Id == "cards");
            var tactics = summary.Groups.Single(group => group.Id == "tactics");
            var oldRevision = session.Revision;
            var card = cards.Offers[0];
            Accept(session, new ChooseShanheBossRewardCommand(cards.Id, card.Id, null, session.Revision));
            var afterCard = session.Snapshot.BossRewardSummary!;
            Require(session.Snapshot.StartingCards.Count == chapter && session.Snapshot.Gold == gold + 500 &&
                afterCard.Groups.Single(group => group.Id == cards.Id).SelectedReward?.Id == card.Id &&
                afterCard.Groups.Single(group => group.Id == tactics.Id).Offers.Select(reward => reward.Id).SequenceEqual(tactics.Offers.Select(reward => reward.Id)) &&
                session.Snapshot.CompletedNodeIds.Count == chapter - 1 && !afterCard.CanConfirm,
                "The card group can be claimed first, grants only its selected physical card, and cannot reroll the other frozen group or finish the chapter.");
            if (chapter == 1) Cold(session, definition);
            before = Snapshot(session);
            Require(!session.Submit(new ChooseShanheBossRewardCommand(cards.Id, card.Id, null, session.Revision)).Accepted &&
                !session.Submit(new ChooseShanheBossRewardCommand(tactics.Id, card.Id, null, session.Revision)).Accepted &&
                !session.Submit(new ChooseShanheBossRewardCommand(tactics.Id, tactics.Offers[0].Id, null, oldRevision)).Accepted &&
                !session.Submit(new ConfirmShanheBossRewardCommand(session.Revision)).Accepted && Snapshot(session) == before,
                "Claimed groups, a reward from another group, stale revisions and unfinished confirmation all reject without mutation.");
            Accept(session, new ChooseShanheBossRewardCommand(tactics.Id, tactics.Offers[0].Id, null, session.Revision));
            Require(session.Snapshot.BossRewardSummary is { CanConfirm: true } &&
                session.Snapshot.Phase == ShanheCampaignPhase.Reward && session.Snapshot.AvailableNodes.Count == 0 &&
                session.Snapshot.Bonuses.Draw + session.Snapshot.Bonuses.Slash + session.Snapshot.Bonuses.InitialHand == chapter &&
                session.Snapshot.ExtraSkillIds.Count == 0 && session.Snapshot.Gold == gold + 500,
                "Claiming both groups still waits on the summary confirmation; a tactic does not consume the new skill slot or repeat fixed credits.");
            if (chapter == 3) Cold(session, definition);
            Accept(session, new ConfirmShanheBossRewardCommand(session.Revision));
            Require(session.Snapshot.BossRewardSummary is null && session.Snapshot.CompletedNodeIds.Count == chapter &&
                (chapter < 3 ? session.Snapshot.Phase == ShanheCampaignPhase.Map && session.Snapshot.AvailableNodes.Single().Id == "boss-" + (chapter + 1)
                    : session.Snapshot.Phase == ShanheCampaignPhase.Won && session.Snapshot.EndingText == "第三章已定"),
                "Only the final summary confirmation opens the next chapter, and the third chapter becomes Won only after that confirmation.");
            before = Snapshot(session);
            Require(!session.Submit(new ConfirmShanheBossRewardCommand(session.Revision)).Accepted && Snapshot(session) == before,
                "Chapter confirmation cannot be repeated after it has advanced the route.");
        }
        Require(session.Snapshot.Gold == 1510 && session.Snapshot.ExtraSkillSlots == 4 &&
            session.AcceptedCommands.OfType<ChooseShanheBossRewardCommand>().Count() == 6 &&
            session.AcceptedCommands.OfType<ConfirmShanheBossRewardCommand>().Count() == 3,
            "All three bundles retain exactly six group claims, three confirmations and their one-time fixed credits.");
        Cold(session, definition);
    }

    public static void ChapterMetadataNestedPoolsLegacyHashesAndLargeShopsKeepTheirBoundaries()
    {
        var source = FixtureDefinition();
        var candidateIds = source.Nodes[0].BossRewards!.Groups[0].RewardIds.ToList();
        var groups = source.Nodes[0].BossRewards!.Groups.ToList();
        groups[0] = groups[0] with { RewardIds = candidateIds };
        source = source with { Nodes = source.Nodes.Select((node, index) => index == 0 ? node with { BossRewards = node.BossRewards! with { Groups = groups } } : node).ToArray() };
        var session = Create(source);
        var hash = session.ContentHash;
        candidateIds.Clear(); groups.Clear();
        Require(session.Definition.Nodes[0].BossRewards!.Groups[0].RewardIds.Count == 3 && session.ContentHash == hash,
            "Published chapter definitions detach mutable group and candidate lists.");
        RejectMutation(session.Definition.Nodes[0].BossRewards!.Groups);
        RejectMutation(session.Definition.Nodes[0].BossRewards!.Groups[0].RewardIds);
        var frozen = session.Definition;
        MustThrow(() => ShanheCampaignContent.FreezeAndValidate(frozen with
        { Nodes = frozen.Nodes.Select((node, index) => index == 0 ? node with { ChapterNumber = 0 } : node).ToArray() }));
        MustThrow(() => ShanheCampaignContent.FreezeAndValidate(frozen with
        { Nodes = frozen.Nodes.Select((node, index) => index == 0 ? node with { BossRewards = node.BossRewards! with { FixedGold = -1 } } : node).ToArray() }));
        MustThrow(() => ShanheCampaignContent.FreezeAndValidate(frozen with
        { Nodes = frozen.Nodes.Select((node, index) => index == 0 ? node with { BossRewards = node.BossRewards! with { Groups = [new("bad", "缺失奖励", ["missing"])] } } : node).ToArray() }));

        foreach (var edition in StandardShanheCampaignCatalog.Editions)
        foreach (var codex in new[] { false, true })
        {
            var current = codex ? StandardShanheCampaignCatalog.CreateCodexDefinition(1, edition.Id)
                : StandardShanheCampaignCatalog.CreateDefinition(1, edition.Id);
            var bosses = current.Nodes.Where(node => node.BossRewards is not null).ToArray();
            Require(current.Nodes.All(node => node.ChapterNumber is >= 1 and <= 3 && !string.IsNullOrWhiteSpace(node.ChapterTitle)) &&
                bosses.Select(node => node.Id).Order(StringComparer.Ordinal).SequenceEqual(new[] { "huaxiong", "lijue", "guosi", "luoyang", "suanzao", "henei" }.Order(StringComparer.Ordinal)) &&
                bosses.All(node => node.BossRewards is { FixedGold: 500, SkillSlots: 1 } && node.BossRewards.Groups.Count == 2 &&
                    node.BossRewards.Groups.All(group => group.ChoiceCount == 3 && group.RewardIds.Count >= 3)) &&
                bosses.Where(node => node.ChapterNumber == 3).Select(node => node.ChapterTitle).Distinct().Count() == 3,
                "Both editions and their codex pools publish three real chapters, the six route-specific completion bosses and both three-choice groups.");
            foreach (var boss in bosses)
                Require(boss.BossRewards!.Groups.Single(group => group.Id == "cards").RewardIds.All(id => current.Rewards.Single(reward => reward.Id == id).StartingCards?.Count > 0),
                    "Chapter card rewards must contain actual opening cards rather than equipment-only substitutes.");
            var legacy = StripChapters(current);
            var legacyJson = JsonSerializer.Serialize(legacy);
            Require(!legacyJson.Contains("\"ChapterNumber\"", StringComparison.Ordinal) && !legacyJson.Contains("\"ChapterTitle\"", StringComparison.Ordinal) &&
                !legacyJson.Contains("\"BossRewards\"", StringComparison.Ordinal), "Default chapter metadata must be absent from historical definition JSON.");
            foreach (var compensation in new[] { true, false })
            {
                var oldDefinition = compensation ? legacy : legacy with { Nodes = legacy.Nodes.Select(node => node with { RewardSkipGold = 0 }).ToArray() };
                var saved = ShanheCampaignSession.Create(oldDefinition, new(71, oldDefinition.PlayerGeneralIds[0]) { UseCodexRewardPool = codex },
                    StandardShanheCampaignCatalog.CreateBattleRegistry).CreateCheckpoint();
                var reconstructed = StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(saved, 1);
                Require(reconstructed.Nodes.All(node => node.BossRewards is null && node.ChapterNumber == 0) &&
                    ShanheCampaignContent.GetContentHash(reconstructed) == saved.CampaignHash,
                    "Only an exact pre-chapter fingerprint selects the matching old definition, including reward compensation metadata.");
                _ = ShanheCampaignSession.Restore(saved, reconstructed, StandardShanheCampaignCatalog.CreateBattleRegistry);
                MustThrow(() => StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(saved with { CampaignHash = "different" }, 1));
                MustThrow(() => StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(saved with
                { Commands = [new ConfirmShanheBossRewardCommand(0)], Revision = 1 }, 1));
            }
        }
        var choices = Enumerable.Range(0, 65).Select(index => new ShanheNodeChoiceDefinition("stock-" + index, "军资", RewardId: "draw", LeavesNode: false)).ToArray();
        var shop = new ShanheNodeDefinition("large-shop", "行商目录", ShanheNodeKind.Shop, ["boss-1"], Choices: choices);
        var shopDefinition = frozen with { StartingNodeIds = [shop.Id], Nodes = new[] { shop }.Concat(frozen.Nodes).ToArray() };
        Require(ShanheCampaignContent.FreezeAndValidate(shopDefinition).Nodes[0].Choices!.Count == 65,
            "A real catalogue shop may exceed the former sixty-four product limit while remaining bounded.");
        MustThrow(() => ShanheCampaignContent.FreezeAndValidate(shopDefinition with
        { Nodes = shopDefinition.Nodes.Select(node => node.Id == shop.Id ? node with { Kind = ShanheNodeKind.Event } : node).ToArray() }));
        MustThrow(() => ShanheCampaignContent.FreezeAndValidate(shopDefinition with
        { Nodes = shopDefinition.Nodes.Select(node => node.Id == shop.Id ? node with { Choices = Enumerable.Range(0, 513).Select(index => choices[0] with { Id = "stock-" + index }).ToArray() } : node).ToArray() }));
    }

    private static ShanheCampaignDefinition StripChapters(ShanheCampaignDefinition definition) => definition with
    {
        Rewards = definition.Rewards.Where(reward => reward.Id is not "chapter-card-slash" and not "chapter-card-dodge").ToArray(),
        Nodes = definition.Nodes.Select(node => node with { ChapterNumber = 0, ChapterTitle = null, BossRewards = null }).ToArray()
    };
    private static ShanheCampaignDefinition FixtureDefinition()
    {
        string[] tactics = ["opening", "draw", "slash"];
        string[] cards = ["peach", "attack", "dodge"];
        return new("fixture:chapter-rewards", "三章征程", [ShanheCampaignChecks.Player], ["boss-1"],
            Enumerable.Range(1, 3).Select(chapter => new ShanheNodeDefinition("boss-" + chapter, "第" + chapter + "章首领",
                ShanheNodeKind.BossBattle, chapter < 3 ? ["boss-" + (chapter + 1)] : [],
                [new("guard-" + chapter, "守军", [new(ShanheCampaignChecks.Enemy)], VictoryGold: 7)], tactics)
            {
                ChapterNumber = chapter, ChapterTitle = "第" + chapter + "章", RewardSkipGold = 100,
                AddSkillSlotsOnVictory = chapter < 3 ? 1 : 0, VictoryText = chapter == 3 ? "第三章已定" : "",
                BossRewards = new(500, 1, [new("tactics", "战法三选一", tactics), new("cards", "卡牌三选一", cards)])
            }).ToArray(),
            [new("opening", "筹备", Bonuses: new(InitialHand: 1)), new("draw", "粮道", Bonuses: new(Draw: 1)),
                new("slash", "连击", Bonuses: new(Slash: 1)), new("peach", "桃", StartingCards: [new(CardKind.Peach)]),
                new("attack", "杀", StartingCards: [new(CardKind.Slash)]), new("dodge", "闪", StartingCards: [new(CardKind.Dodge)])],
            StartingGold: 10, ExtraSkillSlots: 1);
    }
    private static ShanheCampaignSession Create(ShanheCampaignDefinition definition) => ShanheCampaignSession.Create(definition,
        new(71, ShanheCampaignChecks.Player), ShanheCampaignChecks.Registry);
    private static string Snapshot(ShanheCampaignSession session) => JsonSerializer.Serialize(session.Snapshot);
    private static void Cold(ShanheCampaignSession session, ShanheCampaignDefinition definition)
    {
        var saved = ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(session.CreateCheckpoint()));
        var restored = ShanheCampaignSession.Restore(saved, definition, ShanheCampaignChecks.Registry);
        Require(Snapshot(restored) == Snapshot(session), "Cold replay must retain fixed credits, exact group offers, partial claims and chapter phase.");
    }
    private static void Accept(ShanheCampaignSession session, ShanheCampaignCommand command)
    {
        var result = session.Submit(command); Require(result.Accepted, result.Error ?? "Chapter fixture command rejected.");
    }
    private static void RejectMutation<T>(IReadOnlyList<T> values)
    {
        Require(values is IList<T> { IsReadOnly: true }, "Published chapter collections must be read-only.");
        try { ((IList<T>)values)[0] = values[0]; } catch (NotSupportedException) { return; }
        throw new InvalidOperationException("A published chapter collection accepted mutation.");
    }
    private static void MustThrow(Action action)
    {
        try { action(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return; }
        throw new InvalidOperationException("Invalid chapter content or checkpoint was accepted.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
