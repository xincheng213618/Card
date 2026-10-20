namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool GuanSparedThisTurn(int owner, int target) => CompleteProgramEventHistory().OfType<GuanLethalSparedEvent>()
        .Any(e => e.OwnerSeat == owner && e.TargetSeat == target && e.TurnNumber == _turnNumber);

}
