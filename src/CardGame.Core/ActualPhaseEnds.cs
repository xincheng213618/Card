using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ActualPhaseEndItem(ProgramTriggerCandidate Candidate, long EntitlementId);
public sealed record ActualPhaseEndContext(ActualDiscardRecoveryPhaseKey Phase, long EntitlementId, long BoundaryFrameId);
public enum ActualPhaseEndContinuation
{
    Preparation, Judgment, Draw, Play, Discard, Ending, OwnedDraw
}
public sealed record ActualPhaseEndFrame(long Id, ActualDiscardRecoveryPhaseKey Phase,
    ActualPhaseEndContinuation Continuation, IReadOnlyList<ActualPhaseEndItem> Items,
    IReadOnlyList<long> InheritedMovementBatchIds, int ItemIndex = 0, bool ItemsCaptured = false,
    int DelayedEffects = 0, bool AfterNormalDrawProgramsCompleted = false,
    bool DrawPhaseEndedProgramsCompleted = false, bool ActualDrawCompletionCompleted = false,
    long? ParentFrameId = null, long? ActiveWindowFrameId = null)
    : ResolutionFrame(Id, ResolutionFrameKind.ActualPhaseEnd, ResolutionFrameStep.ResolvingEffect);

public sealed record ActualPhaseEndingEvent(long BoundaryFrameId, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record ActualPhaseEndedEvent(long BoundaryFrameId, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record ActualTurnCursorReturnedEvent(int ActualTurn, int OwnerSeat) : IGameEvent;
public sealed record ActualPhaseEndItemResolvedEvent(long BoundaryFrameId, ActualDiscardRecoveryPhaseKey Phase,
    long EntitlementId, int OwnerSeat) : IGameEvent;
public sealed record ActualDiscardPhaseExecutionConfirmedEvent(ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;

public sealed partial record ProgramSkillWindowContext
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ActualPhaseEndContext? ActualPhaseEnd { get; init; }
}
public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ActualPhaseEndContext? ActualPhaseEnd { get; init; }
}
public sealed partial record CardsMovedTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeActualPhaseEndFrameId { get; init; }
}
