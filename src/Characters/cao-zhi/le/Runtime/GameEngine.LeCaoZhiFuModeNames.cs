namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Reconstruct definitions, never live zones. The helpers below only map
    // card faces and do not draw, publish facts or consume random numbers.
    private CardKind[] LeCaoZhiModeNamePool()
    {
        var cards = CreateDeckFromRegistry(_contentRegistry,
            ResolveDeckDefinition(_contentRegistry, _options.DeckId ?? _modeDefinition.DeckId));
        if (_modeDefinition.HulaoActivity is not null)
        {
            // PrepareHulaoActivityDeck also rolls optional equipment. Only its
            // deterministic non-equipment replacement belongs in this pool.
            cards = Array.AsReadOnly(cards.Select(card => card.Kind is CardKind.FiveGrains or CardKind.PeachGarden
                ? card with { Kind = CardKind.AllianceFeast } : card).ToArray());
        }
        cards = PrepareShenwuActivityDeck(cards);
        cards = PrepareHellActivityDeck(cards);
        cards = PrepareWenheActivityDeck(cards);
        cards = PrepareDragonBoatDeck(cards);
        cards = PrepareGuanduDeck(cards);
        return cards.Select(card => card.Kind).Where(kind => !EquipmentCatalog.IsEquipment(kind))
            .Distinct().Order().ToArray();
    }
}
