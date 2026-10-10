namespace CardGame.Core;

public sealed partial class GameEngine
{
    private readonly Dictionary<Type, object> _typedProgramEventHistory = [];

    // This is an index of journal facts, not another active-policy store. Pending
    // facts participate immediately; committed payloads retain their frozen form.
    private IReadOnlyList<T> ProgramEventHistory<T>() where T : IGameEvent
    {
        if (!_typedProgramEventHistory.TryGetValue(typeof(T), out var value))
            _typedProgramEventHistory[typeof(T)] = value = new TypedProgramEventHistory<T>();
        return ((TypedProgramEventHistory<T>)value).Read(_events, _pendingEvents);
    }

    private sealed class TypedProgramEventHistory<T> where T : IGameEvent
    {
        private readonly List<T> _committed = [];
        private int _committedCount;
        private int _pendingCount = -1;
        private int _snapshotCommittedCount;
        private IReadOnlyList<T> _snapshot = Array.Empty<T>();

        internal IReadOnlyList<T> Read(IReadOnlyList<EventEnvelope> events, IReadOnlyList<IGameEvent> pending)
        {
            if (_committedCount == events.Count && _pendingCount == 0 && pending.Count == 0) return _snapshot;
            var changed = false;
            for (; _committedCount < events.Count; _committedCount++)
                if (events[_committedCount].Payload is T fact)
                {
                    _committed.Add(fact);
                    changed = true;
                }
            _pendingCount = pending.Count;
            var previousPendingCount = _snapshot.Count - _snapshotCommittedCount;
            var pendingTypedCount = 0;
            // Counts alone cannot prove a pending tail: cancellation, a same-count
            // replacement or reordering must retain the actual facts by identity.
            for (var index = 0; index < pending.Count; index++)
                if (pending[index] is T typed)
                {
                    if (pendingTypedCount >= previousPendingCount ||
                        !ReferenceEquals(_snapshot[_snapshotCommittedCount + pendingTypedCount], typed))
                        changed = true;
                    pendingTypedCount++;
                }
            if (pendingTypedCount != previousPendingCount) changed = true;
            if (!changed) return _snapshot;

            var length = _committed.Count + pendingTypedCount;
            if (length == 0) _snapshot = Array.Empty<T>();
            else
            {
                var selected = new T[length];
                _committed.CopyTo(selected);
                var next = _committed.Count;
                for (var index = 0; index < pending.Count; index++)
                    if (pending[index] is T typed) selected[next++] = typed;
                _snapshot = Array.AsReadOnly(selected);
            }
            // Commit preparation may replace a pending payload with a frozen
            // copy. Rebuilt snapshots always take that new committed identity.
            _snapshotCommittedCount = _committed.Count;
            return _snapshot;
        }
    }
}
