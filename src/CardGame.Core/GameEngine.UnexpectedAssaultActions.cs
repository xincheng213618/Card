namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void AddUnexpectedAssaultActions(CharacterState actor, IReadOnlyList<Card> playableCards, List<LegalAction> actions)
    {
        foreach (var card in playableCards)
        {
            if (card.Kind == CardKind.UnexpectedAssault) Add(card, null);
            foreach (var conversion in GetProgramViewAsConversions(actor, card, CardKind.UnexpectedAssault, forResponse: false))
                Add(card, conversion);
        }

        void Add(Card card, CardConversionSource? conversion)
        {
            foreach (var target in _players.Where(p => p.IsAlive && p.Seat != actor.Seat && GetHand(p).Count > 0))
                actions.Add(new LegalAction(LegalActionKind.UnexpectedAssault, card.Id, target.Seat,
                    conversion is null ? $"对 {target.Name} 使用【出其不意】" :
                    DescribeConversion(conversion, $"将【{card.DisplayName}】当作【出其不意】对 {target.Name} 使用"),
                    PlayedCardKind: conversion is null ? null : CardKind.UnexpectedAssault)
                    { ConversionSource = conversion });
        }
    }
}
