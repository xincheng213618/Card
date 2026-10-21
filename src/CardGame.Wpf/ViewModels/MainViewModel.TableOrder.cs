namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private int TableSeatOffset(int origin, int target)
    {
        var order = _snapshot.TableOrder ?? _snapshot.Players.Select(player => player.Seat).Order().ToArray();
        var originIndex = -1;
        var targetIndex = -1;
        for (var index = 0; index < order.Count; index++)
        {
            if (order[index] == origin) originIndex = index;
            if (order[index] == target) targetIndex = index;
        }
        if (originIndex < 0 || targetIndex < 0) throw new InvalidOperationException("A displayed participant is absent from table order.");
        return (targetIndex - originIndex + order.Count) % order.Count;
    }
}
