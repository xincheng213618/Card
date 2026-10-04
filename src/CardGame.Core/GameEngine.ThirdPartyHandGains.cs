namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool IsThirdPartyHandGainTrigger(SkillProgramTrigger trigger) =>
        trigger.Window == SkillProgramTriggerWindow.CardsGained &&
        trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerThirdPartyHandGain;

    // Offers one candidate per observed movement: a living character's hand card
    // passing to a different character's zone. The observing owner is decoupled
    // from both seats, so the skill holder sees transfers they took no part in.
    private IEnumerable<ProgramTriggerCandidate> CollectThirdPartyHandGainCandidates(CardMovementBatchContext batch)
    {
        foreach (var owner in _players.Where(player => player.IsAlive))
        foreach (var candidate in CollectProgramTriggerCandidates(owner, SkillProgramTriggerWindow.CardsGained))
        {
            var trigger = GetProgramTrigger(candidate);
            if (!IsThirdPartyHandGainTrigger(trigger)) continue;
            var indexes = batch.Movements.Select((movement, index) => (movement, index))
                .Where(item => item.movement.From != item.movement.To &&
                    item.movement.From.Zone == CardZoneKind.Hand &&
                    item.movement.From.OwnerSeat is { } source && source != item.movement.To.OwnerSeat &&
                    IsValidPlayerSeat(source) && _players[source].IsAlive &&
                    item.movement.To.OwnerSeat is { } destination && IsValidPlayerSeat(destination) &&
                    (trigger.MovementReasons.Count == 0 ||
                        trigger.MovementReasons.Contains(item.movement.Reason.Value)) &&
                    !trigger.ExcludedMovementReasons.Contains(item.movement.Reason.Value))
                .Select(item => item.index).ToArray();
            foreach (var index in indexes)
            {
                var movement = batch.Movements[index];
                var destinationCount = ProgramMovementSourceCounts(batch, trigger.Window)
                    .FirstOrDefault(item => item.Count.Location == movement.To).Count is { } matched ? matched
                    : new CardMovementSourceCount(movement.To, 0, 1);
                var facts = CaptureCardsMovedTriggerFacts(owner, 1, destinationCount, trigger.Window, batch.MovementTiming);
                if (trigger.Condition.Evaluate(facts, candidate.SkillId, candidate.SkillInstanceId))
                    yield return candidate with { OccurrenceIndex = index };
            }
        }
    }
}
