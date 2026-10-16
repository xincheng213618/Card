namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksWholeActualTurnUseOrdinal =>
        _contentRegistry.ProgramDependencies.TracksWholeActualTurnUseOrdinal;

    // A raw TurnEnded can still be followed by an inserted Play phase. This
    // reader remains active until the native turn cursor actually returns.
    private bool HasOngoingWholeActualUseTurn => _turnNumber > 0 &&
        _turnProgression.TurnNumber == _turnNumber && _turnProgression.OwnerSeat == _currentSeat &&
        !CompleteProgramEventHistory().OfType<ActualTurnCursorReturnedEvent>()
            .Any(e => e.ActualTurn == _turnNumber && e.OwnerSeat == _currentSeat);

    private CurrentTurnCardUseKindsRecordedEvent[] WholeActualTurnUseHistory() =>
        EventsSinceLastBoundary(e => e is TurnStartedEvent or ActualTurnCursorReturnedEvent)
            .OfType<CurrentTurnCardUseKindsRecordedEvent>()
            .Where(e => e.TurnNumber == _turnNumber && e.TurnOwnerSeat == _currentSeat).ToArray();

    private int? CapturedWholeActualTurnActorUseOrdinal(CardActionContext action)
    {
        if (!TracksWholeActualTurnUseOrdinal || !HasOngoingWholeActualUseTurn || action.ActionId <= 0 ||
            action.Type == CardActionType.Response &&
            (action.EffectiveKind is not (CardKind.Nullification or CardKind.Dodge) ||
             action.ActorSeat != action.ProviderSeat || action.ResponderSeat != action.ActorSeat ||
             action.RequesterSeat is not null)) return null;
        var facts = WholeActualTurnUseHistory();
        var matching = facts.Where(e => e.CardActionId == action.ActionId).ToArray();
        if (matching.Length == 0) return null;
        if (matching is not [var current] || current.WholeActualTurnUseSequence is not > 0 ||
            current.RoundActionType != action.Type || current.RoundProviderSeat != action.ProviderSeat)
            throw new InvalidOperationException("An actual-turn use requires its exact frozen declaration sequence and provider.");
        var replacements = EventsSinceLastBoundary(e => e is TurnStartedEvent or ActualTurnCursorReturnedEvent)
            .OfType<ProgramCardUseActorReplacedEvent>().GroupBy(e => e.CardUseFrameId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var seen = new HashSet<(long? ActionId, long? LegacyUseId)>();
        var count = 0;
        for (var index = 0; index < current.WholeActualTurnUseSequence; index++)
        {
            if (index >= facts.Length || facts[index].WholeActualTurnUseSequence != index + 1)
                throw new InvalidOperationException("An actual-turn declaration sequence cannot skip or reorder an issued use.");
            var fact = facts[index];
            (long? ActionId, long? LegacyUseId) key = fact.CardActionId is not null
                ? (fact.CardActionId, null) : (null, fact.OriginFrameId);
            if (!seen.Add(key) || fact.RoundActionType is not (CardActionType.Use or CardActionType.Response) ||
                fact.RoundProviderSeat is not { } provider || !IsValidPlayerSeat(provider))
                throw new InvalidOperationException("An actual-turn declaration requires one native use and frozen provider.");
            var actor = fact.ActorSeat;
            // A defending Dodge shares the attack's origin frame, but is its own
            // Response. Only the attacking Use follows that replacement chain.
            if (fact.RoundActionType == CardActionType.Use && replacements.TryGetValue(fact.OriginFrameId, out var changes))
                foreach (var change in changes)
                {
                    if (change.PreviousActorSeat != actor || change.OwnerSeat != actor ||
                        change.ProviderSeat != provider || !IsValidPlayerSeat(change.ActorSeat))
                        throw new InvalidOperationException("An actual-turn use lost its native actor-replacement chain.");
                    actor = change.ActorSeat;
                }
            if (index == current.WholeActualTurnUseSequence - 1 &&
                (fact != current || actor != action.ActorSeat)) return null;
            if (actor == action.ActorSeat) count = checked(count + 1);
        }
        return count;
    }
}
