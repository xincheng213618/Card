using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<Card> LeiluanTrickCandidates(ProgramSkillFrame f)
    {
        var history = CompleteProgramEventHistory().ToArray();
        var started = history.OfType<TurnStartedEvent>().LastOrDefault(e => e.TurnNumber == f.KongShu!.ActualTurn);
        if (started is null) return [];
        // Movement records carry the actual turn, including response uses and the old turn's Ending tail.
        return _cardZones.CardsAt(CardLocation.DiscardPile).Where(c => MatchesProgramCardCategory(c.Kind, [SkillProgramCardCategory.InstantTrick]) &&
            _cardMovements.Any(m => m.CardId == c.Id && m.To == CardLocation.DiscardPile && m.TurnNumber == f.KongShu!.ActualTurn)).OrderBy(c => c.Id).ToArray();
    }
}
