namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_DeferredTurnEndPreludes
{
    internal static bool SameDeferredTurnEndBatch(CardMovementBatchContext left, CardMovementBatchContext right) =>
        left.Id == right.Id && left.ParentFrameId == right.ParentFrameId && left.ParentBatchId == right.ParentBatchId &&
        left.TurnNumber == right.TurnNumber && left.AwaitingProgramFrameId == right.AwaitingProgramFrameId &&
        left.OriginSkillId == right.OriginSkillId && left.OriginSkillInstanceId == right.OriginSkillInstanceId && left.OriginOwnerSeat == right.OriginOwnerSeat &&
        left.Movements.SequenceEqual(right.Movements) && left.SourceCounts.SequenceEqual(right.SourceCounts) &&
        (left.DestinationCounts is null ? right.DestinationCounts is null : right.DestinationCounts is not null && left.DestinationCounts.SequenceEqual(right.DestinationCounts));
}
