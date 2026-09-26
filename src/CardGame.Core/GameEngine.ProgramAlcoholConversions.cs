namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void ValidateAlcoholConversion(CharacterState owner, Card card,
        CardConversionSource? source, bool forResponse)
    {
        if (card.Kind == CardKind.Alcohol ? source is not null : source is null ||
            !GetProgramViewAsConversions(owner, card, CardKind.Alcohol, forResponse).Contains(source))
            throw new InvalidOperationException("The Alcohol conversion has no matching enabled program source.");
    }
}
