namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void ValidateAlcoholConversion(CharacterState owner, Card card,
        CardConversionSource? source, bool forResponse)
    {
        if (source is null ? card.Kind != CardKind.Alcohol :
            !GetProgramViewAsConversions(owner, card, CardKind.Alcohol, forResponse,
                dyingUse: forResponse && _pendingDying?.VictimSeat == owner.Seat).Contains(source))
            throw new InvalidOperationException("The Alcohol conversion has no matching enabled program source.");
    }
}
