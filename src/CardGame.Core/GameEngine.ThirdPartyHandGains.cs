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
            if (trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.KanggeGainDraw)) continue;
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

    private static bool IsOwnerSourceHandGainTrigger(SkillProgramTrigger trigger) =>
        trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerOwnerSourceHandGain;

    // Offers one candidate per observed movement of the observing owner's own hand
    // card into another living character's hand; the owner is the movement source.
    private IEnumerable<ProgramTriggerCandidate> CollectOwnerSourceHandGainCandidates(CardMovementBatchContext batch)
    {
        foreach (var owner in _players.Where(player => player.IsAlive))
        foreach (var candidate in CollectProgramTriggerCandidates(owner, SkillProgramTriggerWindow.CardsMoved))
        {
            var trigger = GetProgramTrigger(candidate);
            if (!IsOwnerSourceHandGainTrigger(trigger)) continue;
            var indexes = batch.Movements.Select((movement, index) => (movement, index))
                .Where(item => item.movement.From != item.movement.To &&
                    item.movement.From.Zone == CardZoneKind.Hand &&
                    item.movement.From.OwnerSeat == candidate.OwnerSeat &&
                    item.movement.To.OwnerSeat is { } destination && IsValidPlayerSeat(destination) &&
                    destination != candidate.OwnerSeat &&
                    (trigger.MovementReasons.Count == 0 ||
                        trigger.MovementReasons.Contains(item.movement.Reason.Value)) &&
                    !trigger.ExcludedMovementReasons.Contains(item.movement.Reason.Value))
                .Select(item => item.index).ToArray();
            foreach (var index in indexes)
            {
                var movement = batch.Movements[index];
                var sourceCount = ProgramMovementSourceCounts(batch, trigger.Window)
                    .FirstOrDefault(item => item.Count.Location == movement.From).Count is { } matched ? matched
                    : new CardMovementSourceCount(movement.From, 0, 1);
                var facts = CaptureCardsMovedTriggerFacts(owner, 1, sourceCount, trigger.Window, batch.MovementTiming);
                if (trigger.Condition.Evaluate(facts, candidate.SkillId, candidate.SkillInstanceId))
                    yield return candidate with { OccurrenceIndex = index };
            }
        }
    }

    // 抗歌 includes draws and processing settlements as well as hand transfers.
    // Its own collector keeps the wider boundary from being counted twice.
    private IEnumerable<ProgramTriggerCandidate> CollectKanggeHandGainCandidates(CardMovementBatchContext batch)
    {
        foreach (var owner in _players.Where(player => player.IsAlive))
        foreach (var candidate in CollectProgramTriggerCandidates(owner, SkillProgramTriggerWindow.CardsGained))
        {
            var trigger = GetProgramTrigger(candidate);
            if (!trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.KanggeGainDraw)) continue;
            var indexes = batch.Movements.Select((movement, index) => (movement, index))
                .Where(item => item.movement.From != item.movement.To &&
                    item.movement.To.Zone == CardZoneKind.Hand &&
                    item.movement.To.OwnerSeat is { } gainer && IsValidPlayerSeat(gainer) &&
                    _players[gainer].IsAlive && gainer != owner.Seat &&
                    IsKanggeMarkedSeat(gainer, owner.Seat) && gainer != _currentSeat &&
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
