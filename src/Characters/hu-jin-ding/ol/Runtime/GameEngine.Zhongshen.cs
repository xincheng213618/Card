namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasZhongshenCapability => _contentRegistry.ProgramDependencies.UsesRoundGainedRedHandOnly;
    private bool MatchesZhongshenRoundGain(CharacterState owner, Card card, SkillProgramViewAs rule)
    {
        if (rule.RoundGainedRedHandOnly != true) return true;
        if (!owner.IsAlive || card.IsGeneralWeapon || _cardZones.GetLocation(card.Id) != CardLocation.Hand(owner.Seat) || SuitColor(EffectiveSuit(owner, card)) != true ||
            ProgramEventHistory<ZhongshenRoundBoundaryEvent>().SingleOrDefault(e => e.RoundNumber == _roundNumber) is not { } round) return false;
        return _cardMovements.Any(m => m.CardId == card.Id && m.Sequence > round.MovementSequence && m.To == CardLocation.Hand(owner.Seat) && ActualHandGainOriginalSource(m) != CardLocation.Hand(owner.Seat));
    }
    private bool HasZhongshenCardMark(int cardId)
    {
        if (_cardZones.GetLocation(cardId) is not { Zone: CardZoneKind.Hand, OwnerSeat: { } owner } || !IsValidPlayerSeat(owner)) return false;
        var card = GetAdvancedCard(cardId);
        return EnabledViewAsPrograms(_players[owner]).SelectMany(p => p.ViewAs).Any(r => r.RoundGainedRedHandOnly == true && MatchesZhongshenRoundGain(_players[owner], card, r));
    }
}
