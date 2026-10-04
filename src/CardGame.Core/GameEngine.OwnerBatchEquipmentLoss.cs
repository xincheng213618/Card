namespace CardGame.Core;

public sealed partial class GameEngine
{
    // This scalar is opt-in. Historical owner-batch bindings retain their exact
    // facts and JSON; no live equipment count or adjacent batch is consulted.
    private static SkillProgramTriggerFacts CaptureOwnerBatchEquipmentLoss(
        SkillProgramTriggerFacts facts,
        CardMovementBatchContext batch,
        ProgramTriggerCandidate candidate,
        SkillProgramTrigger trigger,
        IReadOnlyList<int> matchingIndexes)
    {
        if (!UsesOwnerBatchEquipmentLoss(trigger.Condition)) return facts;
        if (trigger.Window != SkillProgramTriggerWindow.CardsMoved ||
            trigger.MovementOccurrence != SkillProgramMovementOccurrence.PerOwnerBatch ||
            trigger.MovementDiscardOnly || !trigger.SourceZones.Contains(CardZoneKind.Equipment))
            throw new InvalidOperationException("Equipment-loss facts require an actual owner movement batch.");
        var equipment = CardLocation.Equipment(candidate.OwnerSeat);
        var count = matchingIndexes.Select(index => batch.Movements[index])
            .Where(move => move.From == equipment && move.To != equipment)
            .Select(move => move.CardId).Distinct().Count();
        return facts with { MovedEquipmentCardCount = count };
    }

    private static bool UsesOwnerBatchEquipmentLoss(SkillProgramTriggerCondition condition) =>
        condition.Left?.Kind == SkillProgramTriggerValueKind.MovedEquipmentCardCount ||
        condition.Right?.Kind == SkillProgramTriggerValueKind.MovedEquipmentCardCount ||
        condition.Children.Any(UsesOwnerBatchEquipmentLoss);
}
