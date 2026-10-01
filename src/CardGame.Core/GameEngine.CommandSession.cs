namespace CardGame.Core;

public sealed partial class GameEngine
{
    // One owner for command metadata. These forwarding members keep the domain
    // migration small; they hold no second copy of the session state.
    private readonly CommandSessionState _commandSession = new();
    private long _revision { get => _commandSession.Revision; set => _commandSession.Revision = value; }
    private List<GameCommand> _acceptedCommands => _commandSession.AcceptedCommands;
    private GameCommand? _commandAwaitingCommit
    {
        get => _commandSession.CommandAwaitingCommit;
        set => _commandSession.CommandAwaitingCommit = value;
    }
    private Exception? _fatalRuleFailure
    {
        get => _commandSession.FatalRuleFailure;
        set => _commandSession.FatalRuleFailure = value;
    }
    private GameCheckpoint? _lastTrustedCheckpoint
    {
        get => _commandSession.LastTrustedCheckpoint;
        set => _commandSession.LastTrustedCheckpoint = value;
    }

    private sealed class CommandSessionState
    {
        public long Revision;
        public readonly List<GameCommand> AcceptedCommands = [];
        public GameCommand? CommandAwaitingCommit;
        public Exception? FatalRuleFailure;
        public GameCheckpoint? LastTrustedCheckpoint;
    }

    private EngineRunResult ExecuteExclusive(Func<EngineRunResult> operation) =>
        ExecuteCommandOperation(operation, () => State, FlushNotifications);

    // Projection and delivery are explicit dependencies of the command pipeline,
    // not callbacks stored on the match or its resolution frames.
    private EngineRunResult ExecuteCommandOperation(Func<EngineRunResult> operation,
        Func<GameSnapshot> project, Action deliver)
    {
        EnsureSessionHealthy();
        if (_isExecutingPublicOperation)
            throw new InvalidOperationException(
                "GameEngine cannot be advanced reentrantly from a synchronous event handler.");

        _isExecutingPublicOperation = true;
        var previousRevision = _revision;
        var previousCommandCount = _acceptedCommands.Count;
        var hadTrustedPrefix = previousRevision == previousCommandCount;
        var committed = false;
        try
        {
            operation();
            AssertCoreInvariants();

            // Nothing is visible to callbacks yet. Project with the new decision
            // revision, so both the returned result and StateChanged agree.
            var nextRevision = checked(previousRevision + 1);
            _revision = nextRevision;
            RefreshPendingDecisionRevision();
            var snapshot = project();
            var result = new EngineRunResult(_status, _winner, snapshot,
                snapshot.PendingDecision, nextRevision)
            {
                WinnerTeamId = IsTeamMode ? _winnerTeamId : null,
                WinnerFactionId = IsNationalWarMode ? _winnerFactionId : null
            };
            var preparedEvents = PrepareCommandEvents(nextRevision);

            // Reserve every append before the commit point. Normal commands do
            // not copy the journal or rebuild a replay/checkpoint.
            if (_commandAwaitingCommit is not null)
                _acceptedCommands.EnsureCapacity(checked(previousCommandCount + 1));
            _events.EnsureCapacity(checked(_events.Count + preparedEvents.Length));
            _pendingNotifications.EnsureCapacity(checked(
                _pendingNotifications.Count + preparedEvents.Length));

            // Commit: no rules, projection, user code, or allocations follow
            // between these appends and the committed marker.
            if (_commandAwaitingCommit is { } command) _acceptedCommands.Add(command);
            foreach (var item in preparedEvents)
            {
                _events.Add(item.Event);
                _pendingNotifications.Enqueue(item);
            }
            if (preparedEvents.Length > 0)
                _eventSequence = preparedEvents[^1].Event.Sequence;
            _pendingEvents.Clear();
            if (_pendingStateSnapshot is not null) _pendingStateSnapshot = snapshot;
            committed = true;

            deliver();
            // Reuse the prepared projection. Delivery cannot trigger a second
            // BuildResult and invalidate an already observable commit.
            return result;
        }
        catch (Exception failure)
        {
            if (!committed)
            {
                _revision = previousRevision;
                if (_acceptedCommands.Count > previousCommandCount)
                    _acceptedCommands.RemoveRange(previousCommandCount,
                        _acceptedCommands.Count - previousCommandCount);
                _lastTrustedCheckpoint = null;
                if (hadTrustedPrefix)
                {
                    try { _lastTrustedCheckpoint = BuildTrustedCheckpoint(previousRevision); }
                    catch { /* Preserve the original execution/preparation failure. */ }
                }
                MarkSessionFaulted(failure);
            }
            // Delivery failures preserve the committed revision, command and
            // events. Ordinary observer failures are already contained by
            // InvokeObservers; this catches only pipeline/infrastructure failure.
            _pendingNotifications.Clear();
            _pendingEvents.Clear();
            _pendingStateSnapshot = null;
            throw;
        }
        finally { _isExecutingPublicOperation = false; }
    }

    private EventNotification[] PrepareCommandEvents(long revision)
    {
        var prepared = new EventNotification[_pendingEvents.Count];
        for (var index = 0; index < prepared.Length; index++)
        {
            var sequence = checked(_eventSequence + index + 1L);
            prepared[index] = new EventNotification(new EventEnvelope(
                new EventId(sequence), ParentId: null, sequence, revision,
                $"revision-{revision}", CommittedEventProjection.Freeze(_pendingEvents[index])));
        }
        return prepared;
    }
}
