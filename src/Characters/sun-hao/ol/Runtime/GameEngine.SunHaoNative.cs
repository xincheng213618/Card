namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool SunHaoActualTurnOpen => _turnNumber > 0 && _turnProgression.TurnNumber == _turnNumber && _turnProgression.OwnerSeat == _currentSeat &&
        !ProgramEventHistory<ActualTurnCursorReturnedEvent>().Any(e => e.ActualTurn == _turnNumber && e.OwnerSeat == _currentSeat);
}
