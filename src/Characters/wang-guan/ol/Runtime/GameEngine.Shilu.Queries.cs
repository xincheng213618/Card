namespace CardGame.Core;
public sealed partial class GameEngine
{
    private IReadOnlyList<int> ShiluTargets(int owner)=>Array.AsReadOnly(_players.Where(p=>p.IsAlive&&p.Seat!=owner&&GetHand(p).Count>0&&IsWithinAttackRange(owner,p.Seat)).Select(p=>p.Seat).ToArray());
}
