namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Capture the public printed face at the fact's output boundary. Later
    // movement, a nested declaration or a new hand must not change this face.
    private IGameEvent CapturePublicPlayedCard(IGameEvent payload)
    {
        var id = payload switch
        {
            CardUseDeclaredEvent use => use.CardId,
            CardRespondedEvent response => response.CardId,
            CardRecastEvent recast => recast.CardId,
            _ => 0
        };
        if (id <= 0) return payload;
        // Jinzhi discards these entities to create a separate virtual basic
        // card. Its public use must not borrow a cost card's printed suit/rank.
        if (payload is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId)?.Action is { } action &&
            action.ConversionChain.Any(IsWushuangJinzhiSource)) return payload;
        foreach (var frame in _resolutionStack.Reverse())
        {
            var payment = frame is CardDeclarationFrame declaration ? declaration.Payment : frame.AcceptedDeclarationPayment;
            // A hidden counterspell can finish its physical discard before the
            // public response fact. Location alone does not reveal that cost.
            if (payment is { IsRevealed: false } && payment.Cost.CardId == id) return payload;
        }
        var card = _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(card => card.Id == id);
        var face = ToSnapshot(card);
        return payload switch
        {
            CardUseDeclaredEvent use => use with { PublicCard = face },
            CardRespondedEvent response => response with { PublicCard = face },
            CardRecastEvent recast => recast with { PublicCard = face },
            _ => payload
        };
    }
}
