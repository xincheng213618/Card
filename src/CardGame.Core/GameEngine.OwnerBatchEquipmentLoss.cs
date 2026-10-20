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
        IReadOnlyList<int> matchingIndexes) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_OwnerBatchEquipmentLoss.CaptureOwnerBatchEquipmentLoss(facts,batch,candidate,trigger,matchingIndexes);

    private static bool UsesOwnerBatchEquipmentLoss(SkillProgramTriggerCondition condition) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_OwnerBatchEquipmentLoss.UsesOwnerBatchEquipmentLoss(condition);
}
