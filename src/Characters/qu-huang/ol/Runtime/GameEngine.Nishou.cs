using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private int[] NishouMinimumTargets(int owner)
    {var alive=_players.Where(p=>p.IsAlive).ToArray();if(alive.Length==0)return [];var least=alive.Min(p=>GetHand(p).Count);return alive.Where(p=>p.Seat!=owner&&GetHand(p).Count==least).Select(p=>p.Seat).ToArray();}
}
