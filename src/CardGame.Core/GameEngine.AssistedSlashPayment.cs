namespace CardGame.Core;

public sealed partial class GameEngine
{
    private (IReadOnlyList<Card> Cards, CardKind Kind, CardConversionSource? Conversion) ReadAssistedSlashPayment(CharacterState actor, PromptChoice selected)
    {
        var kind = Enum.Parse<CardKind>(selected.Parameters["effective-kind"]);
        IReadOnlyList<Card> cards;
        CardConversionSource? conversion;
        if (selected.Cards.Count > 1)
        {
            if (selected.Parameters.GetValueOrDefault("equipment") == CardKind.ZhangbaSerpentSpear.ToString())
            {
                cards = GetZhangbaSlashPairs(actor).Single(pair => pair.Select(card => card.Id).SequenceEqual(selected.Cards));
                conversion = null;
            }
            else
            {
                if (!TryReadConversionSource(selected.Parameters, out conversion) || conversion is null) throw new InvalidOperationException("A multi-card Slash lost its conversion source.");
                cards = FindProgramMultiCardViewAsSelection(actor, selected.Cards, kind, false, conversion)?.Cards ?? throw new InvalidOperationException("The physical Slash source cards became unavailable.");
            }
        }
        else
        {
            var card = GetPlayableCards(actor).Concat(GetEquipment(actor)).Single(item => item.Id == selected.Cards.Single());
            TryReadConversionSource(selected.Parameters, out _selectedUseConversion);
            _hasSelectedUseConversionChoice = true;
            conversion = GetSelectedUseConversion(actor, actor, card, kind);
            if (conversion is null)
            {
                _selectedUseConversion = null;
                _hasSelectedUseConversionChoice = true;
            }
            cards = [card];
        }
        return (cards, kind, conversion);
    }
}
