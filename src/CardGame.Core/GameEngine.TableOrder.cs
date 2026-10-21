namespace CardGame.Core;

/// <summary>Seat identifies a participant; this public fact changes only their position on the table.</summary>
public sealed record TableSeatMovedEvent(int Seat, int PreviousSeat, int NextSeat, long ParentFrameId) : IGameEvent;

public sealed partial class GameEngine
{
    private TableSeatMovedEvent? _cachedTableMovement;
    private IReadOnlyList<int>? _cachedTableOrder;

    private IReadOnlyList<int> CurrentTableOrder()
    {
        var movements = ProgramEventHistory<TableSeatMovedEvent>();
        var last = movements.LastOrDefault();
        if (_cachedTableOrder is not null && ReferenceEquals(last, _cachedTableMovement)) return _cachedTableOrder;
        var order = Enumerable.Range(0, _playerCount).ToList();
        foreach (var movement in movements)
        {
            order.Remove(movement.Seat);
            order.Insert(order.IndexOf(movement.PreviousSeat) + 1, movement.Seat);
        }
        _cachedTableMovement = last;
        return _cachedTableOrder = Array.AsReadOnly(order.ToArray());
    }

    private int TableStepSeat(int from, int offset)
    {
        ValidatePlayerSeat(from, nameof(from));
        var order = CurrentTableOrder();
        var index = IndexOfTableSeat(order, from);
        return order[((index + offset) % order.Count + order.Count) % order.Count];
    }

    private static int IndexOfTableSeat(IReadOnlyList<int> order, int seat)
    {
        for (var index = 0; index < order.Count; index++) if (order[index] == seat) return index;
        throw new InvalidOperationException("The public table order lost a participant.");
    }

    private int RelativeTableOrder(int from, int target)
    {
        ValidatePlayerSeat(from, nameof(from));
        ValidatePlayerSeat(target, nameof(target));
        var order = CurrentTableOrder();
        return (IndexOfTableSeat(order, target) - IndexOfTableSeat(order, from) + order.Count) % order.Count;
    }

    private int GetAdjacentLivingSeat(int from, int step)
    {
        if (step is not (1 or -1)) throw new ArgumentOutOfRangeException(nameof(step));
        for (var offset = 1; offset <= _playerCount; offset++)
        {
            var seat = TableStepSeat(from, step * offset);
            if (_players[seat].IsAlive) return seat;
        }
        return from;
    }

    private void MoveTableSeatBetween(int owner, int previous, int next, long parentFrameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != parentFrameId || frame.OwnerSeat != owner ||
            !IsValidPlayerSeat(previous) || !IsValidPlayerSeat(next) || previous == next || previous == owner || next == owner ||
            !_players[owner].IsAlive || !_players[previous].IsAlive || !_players[next].IsAlive)
            throw new InvalidOperationException("A table move requires its owning live program frame and two other living neighbors.");
        var remaining = CurrentTableOrder().Where(seat => seat != owner && _players[seat].IsAlive).ToArray();
        var previousIndex = Array.IndexOf(remaining, previous);
        if (remaining[(previousIndex + 1) % remaining.Length] != next)
            throw new InvalidOperationException("The selected table gap is no longer adjacent.");
        AdvanceEventRulesAndQueueFact(new TableSeatMovedEvent(owner, previous, next, parentFrameId));
    }
}
