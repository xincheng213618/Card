using CardGame.Core;
using CardGame.Content.Standard;
using System.Reflection;

internal static class PhysicalDeckRecipeChecks
{
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
