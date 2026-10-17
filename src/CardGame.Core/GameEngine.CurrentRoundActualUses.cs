namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksCurrentRoundActualUses =>
        _contentRegistry?.ProgramDependencies.TracksCurrentRoundActualUses == true;

    // The round reader reuses native declaration facts. Keeping the old turn
    // reader separate preserves its deliberate TurnEnded/Finished boundary.
    private IEnumerable<IGameEvent> CurrentUseKindDeduplicationHistory() => TracksCurrentRoundActualUses || TracksWholeActualTurnUseOrdinal
        ? CompleteProgramEventHistory()
        : EventsSinceLastBoundary(e => e is TurnStartedEvent or TurnEndedEvent);

    private int CurrentRoundActualUseCount(int actorSeat)
    {
        if (!TracksCurrentRoundActualUses || _roundNumber < 1 || !IsValidPlayerSeat(actorSeat)) return 0;
        var history = EventsSinceLastBoundary(e => e is RoundStartedEvent).ToArray();
        var replacements = history.OfType<ProgramCardUseActorReplacedEvent>()
            .GroupBy(e => e.CardUseFrameId).ToDictionary(group => group.Key, group => group.ToArray());
        var seen = new HashSet<(long? ActionId, long? LegacyUseId, int? InstructionIndex)>();
        var count = 0;
        foreach (var fact in history.OfType<CurrentTurnCardUseKindsRecordedEvent>())
        {
            (long? ActionId, long? LegacyUseId, int? InstructionIndex) key = fact.CardActionId is not null
                ? (fact.CardActionId, null, null) : (null, fact.OriginFrameId, fact.ProgramInstructionIndex);
            if (!seen.Add(key))
                throw new InvalidOperationException("A round use must have one native declaration fact.");
            var actor = fact.ActorSeat;
            if (fact.RoundActionType is not (CardActionType.Use or CardActionType.Response) ||
                fact.RoundProviderSeat is not { } provider || !IsValidPlayerSeat(provider))
                throw new InvalidOperationException("A round use requires its frozen native action type and provider.");
            // Dodge's origin is the attacking use, while its own accepted action
            // is a Response. An attacker's role change must never steal that use.
            // Some real virtual Use producers do not emit ActionAccepted. Its
            // issuance type, rather than an optional accepted-action event,
            // distinguishes that use from a defender's Response.
            if (fact.RoundActionType == CardActionType.Use && replacements.TryGetValue(fact.OriginFrameId, out var changes))
            {
                foreach (var change in changes)
                {
                    if (change.PreviousActorSeat != actor || change.OwnerSeat != actor ||
                        change.ProviderSeat != provider || !IsValidPlayerSeat(change.ActorSeat))
                        throw new InvalidOperationException("A round use lost its native actor-replacement chain.");
                    actor = change.ActorSeat;
                }
            }
            if (actor == actorSeat) count = checked(count + 1);
        }
        return count;
    }
}
