using CardGame.Core;
using CardGame.Content.Standard;
using System.Reflection;

internal static class PhysicalDeckRecipeChecks
{
    public static void ClassicPhysicalDecksMatchOfficialTables()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 37, 0));
        var deck = registry.Decks["classic:standard-deck"];
        var cards = deck.PhysicalCards ?? throw new InvalidOperationException(
            "The current classic deck must use a physical-card recipe.");

        Require(deck.Cards.Count == 0 && cards.Count == 108,
            "The standard recipe must contain exactly 108 physical cards and no count recipe.");
        foreach (var suit in Enum.GetValues<Suit>())
        {
            var suited = cards.Where(card => card.Suit == suit).ToArray();
            Require(suited.Length == 27, $"{suit} must contain exactly 27 cards.");
            for (var rank = 1; rank <= 13; rank++)
            {
                var expected = IsExRank(suit, rank) ? 3 : 2;
                Require(suited.Count(card => card.Rank == rank) == expected,
                    $"{suit} {rank} must contain {expected} physical cards.");
            }
        }

        var counts = cards.GroupBy(card => card.CardDefinitionId)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Require(Count("standard:slash") == 30 &&
                Count("standard:dodge") == 15 &&
                Count("standard:peach") == 8,
            "The standard basic-card split must be 30 Slash, 15 Dodge and 8 Peach.");
        Require(cards.Count(card => registry.Cards[card.CardDefinitionId].CategoryName == "基本牌") == 53 &&
                cards.Count(card => registry.Cards[card.CardDefinitionId].CategoryName == "锦囊牌") == 36 &&
                cards.Count(card => registry.Cards[card.CardDefinitionId].CategoryName == "装备牌") == 19,
            "The standard category split must be 53 basic, 36 trick and 19 equipment cards.");
        Require(Has("standard:lightning", Suit.Heart, 12) &&
                Has("classic:ice-sword", Suit.Spade, 2) &&
                Has("standard:nullification", Suit.Diamond, 12) &&
                Has("standard:renwang_shield", Suit.Club, 2),
            "The four EX cards must occupy the canonical heart Q, spade 2, diamond Q and club 2 slots.");
        Require(Has("standard:offensive_horse", Suit.Heart, 5) &&
                Has("classic:dawan", Suit.Spade, 13) &&
                Has("classic:zixing", Suit.Diamond, 13) &&
                Has("standard:defensive_horse", Suit.Spade, 5) &&
                Has("classic:dilu", Suit.Club, 5) &&
                Has("classic:zhaohuangfeidian", Suit.Heart, 13),
            "All six standard mounts must retain their canonical names, suits and ranks.");

        var createDeck = typeof(GameEngine).GetMethod(
            "CreateDeckFromRegistry", BindingFlags.Static | BindingFlags.NonPublic)!;
        var runtimeCards = (IReadOnlyList<Card>)createDeck.Invoke(null, [registry, deck])!;
        Require(RuntimeKind(Suit.Heart, 5, EquipmentSlot.OffensiveHorse) == CardKind.OffensiveHorse &&
                RuntimeKind(Suit.Spade, 13, EquipmentSlot.OffensiveHorse) == CardKind.Dawan &&
                RuntimeKind(Suit.Diamond, 13, EquipmentSlot.OffensiveHorse) == CardKind.Zixing &&
                RuntimeKind(Suit.Spade, 5, EquipmentSlot.DefensiveHorse) == CardKind.DefensiveHorse &&
                RuntimeKind(Suit.Club, 5, EquipmentSlot.DefensiveHorse) == CardKind.Dilu &&
                RuntimeKind(Suit.Heart, 13, EquipmentSlot.DefensiveHorse) == CardKind.Zhaohuangfeidian,
            "The physical recipe must preserve six distinct mount identities at runtime.");

        var legacy = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 35, 0));
        Require(legacy.Decks["classic:standard-deck"].PhysicalCards is null &&
                legacy.Decks["classic:standard-deck"].Cards.Sum(card => card.Count) == 105,
            "The 1.35 hybrid deck must remain available for old checkpoints and scenarios.");
        var genericPhysical = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 36, 0));
        Require(!genericPhysical.Cards.ContainsKey("classic:dawan") &&
                genericPhysical.Decks["classic:standard-deck"].PhysicalCards!.Count(card =>
                    card.CardDefinitionId == "standard:offensive_horse") == 3 &&
                genericPhysical.Decks["classic:standard-deck"].PhysicalCards!.Count(card =>
                    card.CardDefinitionId == "standard:defensive_horse") == 3,
            "The 1.36 physical deck must retain its generic mount identities and fingerprint boundary.");

        var militaryRegistry = StandardContentRegistry.CreateWithClassicGenerals();
        var militaryCards = militaryRegistry.Decks["classic:standard-deck"].PhysicalCards ?? [];
        var expansion = militaryCards.Skip(108).ToArray();
        Require(militaryCards.Count == 160 && expansion.Length == 52 &&
                Enum.GetValues<Suit>().All(suit => expansion.Count(card => card.Suit == suit) == 13) &&
                Enum.GetValues<Suit>().All(suit => Enumerable.Range(1, 13).All(rank =>
                    expansion.Count(card => card.Suit == suit && card.Rank == rank) == 1)),
            "The current classic deck must append one military card for every suit/rank to the 108-card standard deck.");
        var expansionCounts = expansion.GroupBy(card => card.CardDefinitionId)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Require(ExpansionCount("standard:fire_slash") == 5 &&
                ExpansionCount("standard:thunder_slash") == 9 &&
                ExpansionCount("standard:dodge") == 9 &&
                ExpansionCount("standard:peach") == 4 &&
                ExpansionCount("standard:alcohol") == 5 &&
                ExpansionCount("standard:nullification") == 3 &&
                ExpansionCount("standard:fire_attack") == 3 &&
                ExpansionCount("standard:supply_shortage") == 2 &&
                ExpansionCount("standard:iron_chain") == 6 &&
                expansion.Count(card => militaryRegistry.Cards[card.CardDefinitionId].CategoryName == "装备牌") == 6 &&
                expansion.Single(card => card.Suit == Suit.Diamond && card.Rank == 13).CardDefinitionId == "classic:hualiu",
            "The 52-card military expansion must match its exact card-name distribution and diamond-K Hualiu.");

        int Count(string id) => counts.GetValueOrDefault(id);
        int ExpansionCount(string id) => expansionCounts.GetValueOrDefault(id);
        bool Has(string id, Suit suit, int rank) => cards.Any(card =>
            card.CardDefinitionId == id && card.Suit == suit && card.Rank == rank);
        CardKind RuntimeKind(Suit suit, int rank, EquipmentSlot slot) => runtimeCards.Single(card =>
            card.Suit == suit && card.Rank == rank &&
            EquipmentCatalog.IsEquipment(card.Kind) && EquipmentCatalog.Get(card.Kind).Slot == slot).Kind;
        static bool IsExRank(Suit suit, int rank) =>
            (suit, rank) is (Suit.Heart, 12) or (Suit.Spade, 2) or
                (Suit.Diamond, 12) or (Suit.Club, 2);
    }

    public static void ExactSuitRankValidationAndHashing()
    {
        var physical = new[]
        {
            new ContentDeckPhysicalCard("physical:slash", Suit.Spade, 7),
            new ContentDeckPhysicalCard("physical:dodge", Suit.Heart, 2),
            new ContentDeckPhysicalCard("physical:slash", Suit.Diamond, 13)
        };
        var registry = ContentRegistry.Build(new PhysicalPackage("physical-package-a", physical));
        var deck = registry.Decks[PhysicalPackage.DeckId];
        Require(deck.Cards.Count == 0 && deck.PhysicalCards!.SequenceEqual(physical),
            "The registry must preserve the ordered physical-card recipe.");

        var createDeck = typeof(GameEngine).GetMethod(
            "CreateDeckFromRegistry", BindingFlags.Static | BindingFlags.NonPublic)!;
        var cards = (IReadOnlyList<Card>)createDeck.Invoke(null, [registry, deck])!;
        Require(cards.Select(card => (card.Id, card.Kind, card.Suit, card.Rank)).SequenceEqual(new[]
        {
            (1, CardKind.Slash, Suit.Spade, 7),
            (2, CardKind.Dodge, Suit.Heart, 2),
            (3, CardKind.Slash, Suit.Diamond, 13)
        }), "The engine must construct exact physical cards without cycling suit or rank by id.");

        var changed = ContentRegistry.Build(new PhysicalPackage("physical-package-a",
        [
            physical[0] with { Rank = 8 },
            physical[1],
            physical[2]
        ]));
        Require(changed.ContentHash != registry.ContentHash,
            "Changing one physical card's rank must change the content fingerprint.");

        ExpectInvalid(new PhysicalPackage("physical-package-invalid-rank",
            [new ContentDeckPhysicalCard("physical:slash", Suit.Club, 14)]));
        ExpectInvalid(new PhysicalPackage("physical-package-mixed", physical, includeCountRecipe: true));
    }

    private static void ExpectInvalid(IGameContentPackage package)
    {
        try
        {
            _ = ContentRegistry.Build(package);
            throw new InvalidOperationException("Invalid physical deck recipe was accepted.");
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("Deck", StringComparison.Ordinal))
        {
        }
    }

    private sealed class PhysicalPackage(
        string packageId,
        IReadOnlyList<ContentDeckPhysicalCard> physicalCards,
        bool includeCountRecipe = false) : IGameContentPackage
    {
        public const string DeckId = "physical:deck";
        public PackageManifest Manifest { get; } = new(packageId, new Version(1, 0, 0), []);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddCard(new ContentCardDefinition(
                "physical:slash", "杀", "基本牌", "测试杀。", CardKind.Slash));
            builder.AddCard(new ContentCardDefinition(
                "physical:dodge", "闪", "基本牌", "测试闪。", CardKind.Dodge));
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "逐张实体牌测试牌堆",
                0,
                0,
                includeCountRecipe ? [new ContentDeckCardCount("physical:slash", 1)] : [])
            {
                PhysicalCards = physicalCards
            });
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
