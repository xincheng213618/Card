using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int WenyiRemaining(int owner) => Math.Max(0, 1 + ProgramEventHistory<MiaoxinWenyiRefreshedEvent>().Count(e => e.OwnerSeat == owner) - ProgramEventHistory<WenyiPaidEvent>().Count(e => e.OwnerSeat == owner));
}
