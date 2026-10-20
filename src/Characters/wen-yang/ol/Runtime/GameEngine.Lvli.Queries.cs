using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private int WenYangLvliLimit(int owner)=>WenYangHasAwakened(owner,1)&&owner==_currentSeat?2:1;
    private int WenYangLvliUses(int owner)=>ProgramEventHistory<WenYangLvliIssuedEvent>().Count(e=>e.OwnerSeat==owner&&e.ActualTurn==_turnNumber&&e.TurnOwner==_currentSeat);
}
