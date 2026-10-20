namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasDiaolingAwakened(int seat) => ProgramEventHistory<DiaolingAwakenedEvent>().Any(e => e.OwnerSeat == seat);
}
