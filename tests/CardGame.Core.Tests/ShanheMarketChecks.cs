using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheMarketChecks
{
    private const string Player = "fixture:market-player";
    private static readonly Lazy<ContentRegistry> Content = new(() => ContentRegistry.Build(new StandardContentPackage(), new FixturePackage()));

    public static void RandomStockCategoryPricesRefreshAndColdReplay()
    {
        var definition = Definition();
        var options = Options();
        var session = Create(definition, options);
        Accept(session, new MoveShanheNodeCommand("shop", 0));
        var published = session.Snapshot;
        Require(published.ShopRefreshRemaining == 1 && published.ShopRefreshCost == 100,
            "A new codex shop must publish one refresh costing exactly 100 copper coins.");
        foreach (var category in new[] { "t", "s", "e", "c" })
            Require(Items(session, category).Length is 3 or 4, "A sufficiently populated category must publish three or four real items.");
        Require(published.ShopItems.All(item => item.Reward?.Id is not ("t0" or "s-locked" or "e-locked" or "c-unpriced" or "c-bundle")) &&
            published.ShopItems.Select(item => item.ChoiceId).Distinct(StringComparer.Ordinal).Count() == published.ShopItems.Count,
            "Owned tactics, locked skills and locked equipment must stay outside unique current stock.");
        Require(Serialize(CreateAtShop(definition, options).Snapshot) == Serialize(published), "The same seed and node must produce exact stock and prices.");
        for (var query = 0; query < 4; query++) _ = session.Snapshot.ShopItems;
        Require(ReferenceEquals(session.Snapshot, published) && session.AcceptedCommands.Count == 1,
            "Reading market stock must preserve the published object and command journal.");
        Frozen(published.ShopItems); Frozen(published.NodeChoices);
        RejectUnchanged(session, new ChooseShanheNodeOptionCommand("buy-t0", null, session.Revision));
        RejectUnchanged(session, new ChooseShanheNodeOptionCommand("$shop:0:s-locked", null, session.Revision));

        BuyCategory(session, "t", 200);
        Require(Items(session, "t").All(item => item.GoldCost == 300) && Items(session, "e").All(item => item.GoldCost == 100) &&
            Items(session, "c").All(item => item.GoldCost == 150) && Items(session, "s").All(item => item.GoldCost == 80),
            "A tactic purchase must increase only that category by 100.");
        BuyCategory(session, "e", 100);
        BuyCategory(session, "c", 150);
        BuyCategory(session, "s", 80);
        BuyCategory(session, "t", 300);
        Accept(session, new ChooseShanheNodeOptionCommand("buy-medicine", null, session.Revision));
        Require(Items(session, "e").All(item => item.GoldCost == 200) && Items(session, "c").All(item => item.GoldCost == 250) &&
            Items(session, "s").All(item => item.GoldCost == 80), "Equipment and cards accumulate independently while skill prices stay fixed.");
        foreach (var item in session.Snapshot.ShopItems.Where(item => !item.IsPurchased))
            Require(session.Snapshot.NodeChoices.Single(choice => choice.Id == item.ChoiceId).GoldCost == item.GoldCost,
                "Node commands and product views must agree on the exact current debit.");
        Cold(session, definition);

        var oldStock = session.Snapshot.ShopItems.Where(item => item.Reward?.Id != "medicine").Select(item => item.ChoiceId).ToArray();
        var beforeRefresh = session.Snapshot.Gold;
        RejectUnchanged(session, new RefreshShanheShopCommand(session.Revision - 1));
        Accept(session, new RefreshShanheShopCommand(session.Revision));
        Require(session.Snapshot.Gold == beforeRefresh - 100 && session.Snapshot.ShopRefreshRemaining == 0 &&
            session.Snapshot.ShopItems.Where(item => item.Reward?.Id != "medicine").All(item => !oldStock.Contains(item.ChoiceId)),
            "Refresh must pay once, replace stock identities and consume its allowance.");
        foreach (var id in oldStock) RejectUnchanged(session, new ChooseShanheNodeOptionCommand(id, null, session.Revision));
        RejectUnchanged(session, new RefreshShanheShopCommand(session.Revision));
        Require(Items(session, "t").All(item => item.GoldCost == 400) && Items(session, "e").All(item => item.GoldCost == 200) &&
            Items(session, "c").All(item => item.GoldCost == 250), "Refresh must retain all paid category surcharges.");
        Require(session.Snapshot.ShopItems.Single(item => item.ChoiceId == "buy-medicine") is { IsPurchased: true, GoldCost: 35 } &&
            Items(session, "t").All(item => !session.Snapshot.TacticIds.Contains(item.Reward!.Id)),
            "Refresh must retain supply purchase state and exclude every tactic already owned.");
        Cold(session, definition);
        Accept(session, new ChooseShanheNodeOptionCommand("$leave", null, session.Revision));
        RejectUnchanged(session, new RefreshShanheShopCommand(session.Revision));
        Accept(session, new MoveShanheNodeCommand("shop2", session.Revision));
        Require(session.Snapshot.ShopRefreshRemaining == 1 && Items(session, "t").All(item => item.GoldCost == 200) &&
            Items(session, "e").All(item => item.GoldCost == 100) && Items(session, "c").All(item => item.GoldCost == 150),
            "A separate shop starts with its own refresh and category counters.");
        Cold(session, definition);
    }

    public static void InsufficientFundsSmallPoolsAndLegacyFixedStock()
    {
        var definition = Definition();
        var poor = CreateAtShop(definition with { StartingGold = 99 }, Options());
        RejectUnchanged(poor, new RefreshShanheShopCommand(poor.Revision));
        var tactic = Items(poor, "t").First();
        Require(tactic is { CanPurchase: false, State: ShanheShopItemState.InsufficientResources }, "Unaffordable stock must expose the real debit state.");
        RejectUnchanged(poor, new ChooseShanheNodeOptionCommand(tactic.ChoiceId, null, poor.Revision));
        var sparse = definition with
        {
            Rewards = definition.Rewards.Where(reward => reward.Id is "medicine" or "t0" or "t1" or "s0" or "s-locked" or "e0" or "e-locked" or "c0").ToArray(),
            Nodes = definition.Nodes.Select(node => node.Kind == ShanheNodeKind.Shop ? node with
            { Choices = node.Choices!.Where(choice => choice.RewardId is "medicine" or "t0" or "t1" or "s0" or "s-locked" or "e0" or "e-locked" or "c0").ToArray() } : node).ToArray()
        };
        var small = CreateAtShop(sparse, Options());
        Require(Items(small, "t").Length == 1 && Items(small, "s").Length == 1 && Items(small, "e").Length == 1 && Items(small, "c").Length == 1,
            "Small real pools must remain small without inventing filler products.");
        Accept(small, new RefreshShanheShopCommand(small.Revision));
        Cold(small, sparse);

        var legacy = CreateAtShop(definition, new(71, Player));
        Require(legacy.Snapshot.ShopItems.Count == definition.Nodes[0].Choices!.Count && legacy.Snapshot.ShopRefreshRemaining == 0,
            "Legacy options must retain authored fixed stock and no refresh command.");
        Accept(legacy, new ChooseShanheNodeOptionCommand("buy-t0", null, legacy.Revision));
        Require(legacy.Snapshot.ShopItems.Single(item => item.ChoiceId == "buy-t1").GoldCost == 200,
            "A legacy purchase must not apply new category surcharges.");
        RejectUnchanged(legacy, new RefreshShanheShopCommand(legacy.Revision));
        var json = JsonNode.Parse(ShanheCampaignJson.Serialize(legacy.CreateCheckpoint()))!;
        json["Options"]!.AsObject().Remove(nameof(ShanheCampaignOptions.UseCodexRewardPool));
        var checkpoint = ShanheCampaignJson.Deserialize(json.ToJsonString());
        Require(!checkpoint.Options.UseCodexRewardPool && Serialize(ShanheCampaignSession.Restore(checkpoint, definition, _ => Content.Value).Snapshot) == Serialize(legacy.Snapshot),
            "Old JSON without the new option must replay its fixed inventory and exact original prices.");
    }

    private static ShanheCampaignOptions Options() => new(71, Player)
    {
        UseCodexRewardPool = true, EquippedTacticIds = ["t0"],
        UnlockedRewardIds = ["t0", .. Enumerable.Range(0, 7).Select(i => "s" + i), .. Enumerable.Range(0, 7).Select(i => "e" + i)]
    };
    private static ShanheCampaignDefinition Definition()
    {
        var skills = Enumerable.Range(0, 7).Select(i => "fixture:market-skill-" + i).ToArray();
        var rewards = Enumerable.Range(0, 7).Select(i => new ShanheRewardDefinition("t" + i, "战法" + i) { TacticSkillId = "fixture:market-tactic-" + i })
            .Concat(skills.Select((id, i) => new ShanheRewardDefinition("s" + i, "技能" + i, SkillId: id)))
            .Concat(Enumerable.Range(0, 7).Select(i => new ShanheRewardDefinition("e" + i, "装备" + i, StartingEquipment: [new(CardKind.Crossbow)])))
            .Concat(Enumerable.Range(0, 7).Select(i => new ShanheRewardDefinition("c" + i, "卡牌" + i, StartingCards: [new(CardKind.Slash)])))
            .Concat([new("s-locked", "锁定技能", SkillId: "fixture:market-skill-locked"), new("e-locked", "锁定装备", StartingEquipment: [new(CardKind.BaguaFormation)]),
                new("c-unpriced", "仅作奖励的卡牌", StartingCards: [new(CardKind.Peach)]), new("c-bundle", "不可直接购买的套装", StartingCards: [new(CardKind.Dodge)]),
                new("medicine", "补给", Heal: 1)]).ToArray();
        var choices = rewards.Where(reward => reward.Id != "c-unpriced").Select(reward => new ShanheNodeChoiceDefinition("buy-" + reward.Id, reward.Name,
            reward.Id == "c-bundle" ? 9999 : reward.Id[0] switch { 't' => 200, 's' => 80, 'e' => 100, 'c' => 150, _ => 35 }, RewardId: reward.Id, LeavesNode: false)).ToArray();
        return new("fixture:market", "集市", [Player], ["shop"],
            [new("shop", "一号集市", ShanheNodeKind.Shop, ["shop2"], Choices: choices),
             new("shop2", "二号集市", ShanheNodeKind.Shop, ["battle"], Choices: choices),
             new("battle", "守关", ShanheNodeKind.Battle, [], [new("gate", "关隘", [new(Player)])])], rewards, StartingGold: 10_000, ExtraSkillSlots: 3);
    }
    private static ShanheShopItemSnapshot[] Items(ShanheCampaignSession session, string prefix) =>
        session.Snapshot.ShopItems.Where(item => item.Reward?.Id.StartsWith(prefix, StringComparison.Ordinal) == true && !item.Reward.Id.Contains('-')).ToArray();
    private static void BuyCategory(ShanheCampaignSession session, string category, int price)
    {
        var item = Items(session, category).First(item => item.CanPurchase);
        Require(item.GoldCost == price, "The published initial price must equal the authored category price.");
        var gold = session.Snapshot.Gold;
        var staleRevision = session.Revision;
        Accept(session, new ChooseShanheNodeOptionCommand(item.ChoiceId, null, staleRevision));
        Require(session.Snapshot.Gold == gold - price, "A successful purchase must debit exactly the displayed current price.");
        RejectUnchanged(session, new ChooseShanheNodeOptionCommand(item.ChoiceId, null, staleRevision));
        RejectUnchanged(session, new ChooseShanheNodeOptionCommand(item.ChoiceId, null, session.Revision));
    }
    private static ShanheCampaignSession Create(ShanheCampaignDefinition definition, ShanheCampaignOptions options) => ShanheCampaignSession.Create(definition, options, _ => Content.Value);
    private static ShanheCampaignSession CreateAtShop(ShanheCampaignDefinition definition, ShanheCampaignOptions options)
    {
        var session = Create(definition, options); Accept(session, new MoveShanheNodeCommand("shop", 0)); return session;
    }
    private static string Serialize(ShanheCampaignSnapshot snapshot) => JsonSerializer.Serialize(snapshot);
    private static void Cold(ShanheCampaignSession session, ShanheCampaignDefinition definition)
    {
        var restored = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(session.CreateCheckpoint())), definition, _ => Content.Value);
        Require(Serialize(restored.Snapshot) == Serialize(session.Snapshot), "Cold journal replay must retain exact random inventory, prices, resources and refresh state.");
    }
    private static void Accept(ShanheCampaignSession session, ShanheCampaignCommand command)
    { var result = session.Submit(command); Require(result.Accepted, result.Error ?? "Campaign command rejected."); }
    private static void RejectUnchanged(ShanheCampaignSession session, ShanheCampaignCommand command)
    { var before = Serialize(session.Snapshot); Require(!session.Submit(command).Accepted && Serialize(session.Snapshot) == before, "A rejected command must preserve all market state and revision."); }
    private static void Frozen<T>(IReadOnlyList<T> values)
    {
        Require(values is IList<T> { IsReadOnly: true }, "Published stock must expose a frozen collection.");
        if (values.Count == 0) return;
        try { ((IList<T>)values)[0] = values[0]; } catch (NotSupportedException) { return; }
        throw new InvalidOperationException("Published stock accepted mutation.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class FixturePackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-market", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var ids = Enumerable.Range(0, 7).Select(i => "fixture:market-tactic-" + i)
                .Concat(Enumerable.Range(0, 7).Select(i => "fixture:market-skill-" + i))
                .Concat(["fixture:market-skill-locked", "fixture:market-printed"]);
            var programs = ids.Select(id => new { id, revision = 1,
                modifiers = new[] { new { id = "range", query = "attackRange", operation = "add", value = 1, priority = 0 } } }).ToArray();
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = programs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = programs.ToDictionary(item => item.id, item => new { name = item.id, description = "Market tactic fixture" }) }));
            foreach (var entry in catalog.Programs) builder.AddSkill(new(entry.Key, entry.Key, "fixture") { Program = entry.Value });
            builder.AddGeneral(new(Player, "行者", "", "fixture:market-printed", "shu"));
        }
    }
}
