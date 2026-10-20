using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private int MouZyMobileHandLimit(int owner) => HasMouZhouYuMobileCapability && MouZyMobileTurn() is { } turn
        ? ProgramEventHistory<MouZhouYuMobileYingziIssuedEvent>().Where(e => e.Source.OwnerSeat == owner && e.Turn == turn).Sum(e => e.Count) : 0;
}
