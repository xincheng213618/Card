namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool QiejianBlocked(int owner,int target)=>ProgramEventHistory<QiejianRoundBlockedEvent>().Any(e=>e.OwnerSeat==owner&&e.TargetSeat==target&&e.RoundNumber==_roundNumber);
}
