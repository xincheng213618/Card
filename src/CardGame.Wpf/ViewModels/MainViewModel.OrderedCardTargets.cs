using System.Collections;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    // Ordinary cards may resolve in Core's canonical order. Cards with distinct
    // first/second effects must also retain the user's reversible click order.
    private sealed class OrderedCardTargets : IEnumerable<int>
    {
        private readonly HashSet<int> _membership = [];
        private readonly List<int> _order = [];
        public int Count => _order.Count;
        public bool Contains(int seat) => _membership.Contains(seat);
        public bool Add(int seat)
        {
            if (!_membership.Add(seat)) return false;
            _order.Add(seat);
            return true;
        }
        public bool Remove(int seat)
        {
            if (!_membership.Remove(seat)) return false;
            _order.Remove(seat);
            return true;
        }
        public void Clear() { _membership.Clear(); _order.Clear(); }
        public bool SetEquals(IEnumerable<int> seats) => _membership.SetEquals(seats);
        public void UnionWith(IEnumerable<int> seats) { foreach (var seat in seats) Add(seat); }
        public void IntersectWith(IEnumerable<int> seats)
        {
            _membership.IntersectWith(seats);
            _order.RemoveAll(seat => !_membership.Contains(seat));
        }
        public IEnumerator<int> GetEnumerator() => _order.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private bool IsOrderedFloodSelected => MultiTargetCardActions is { Length: > 0 } actions &&
        actions.All(action => action.Kind == CardGame.Core.LegalActionKind.WumiaoFlood);
}
