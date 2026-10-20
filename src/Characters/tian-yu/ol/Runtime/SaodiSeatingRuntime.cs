namespace CardGame.Core;

[ProgramRuntimeModule]
internal sealed class SaodiSeatingRuntime(IDistanceRuleRuntimeQueries queries, ISelectionParticipantQueries participants)
{
    internal int[] BetweenSeats(int ownerSeat, int targetSeat)
    {
        var clockwise = CollectAliveSeatsOnArc(ownerSeat, targetSeat, step: 1);
        var counterClockwise = CollectAliveSeatsOnArc(ownerSeat, targetSeat, step: -1);
        int ArcLength(List<int> arc) => arc.Count + 1;
        if (ArcLength(clockwise) < ArcLength(counterClockwise)) return clockwise.Order().ToArray();
        if (ArcLength(counterClockwise) < ArcLength(clockwise)) return counterClockwise.Order().ToArray();
        return clockwise.Concat(counterClockwise).Distinct().Order().ToArray();
    }

    private List<int> CollectAliveSeatsOnArc(int ownerSeat, int targetSeat, int step)
    {
        var seats = new List<int>();
        var seat = ownerSeat;
        do
        {
            seat = (seat + step + queries.PlayerCount) % queries.PlayerCount;
            if (seat == targetSeat) break;
            if (participants.IsAlive(seat)) seats.Add(seat);
        }
        while (seat != ownerSeat);
        return seats;
    }
}
