namespace CardGame.Core;

public enum RoundEndingBoundaryStage { ReturningHolds, Programs, Completing }

/// <summary>The old round retains its actual turn until every issued child returns.</summary>
public sealed record RoundEndingBoundaryFrame(
    long Id, int RoundNumber, int ActualTurnNumber, int OwnerSeat, int NextNormalSeat,
    bool SkippedTurn, ActualTurnKind OriginalActualTurnKind, int? NormalResumeSeat,
    IReadOnlyList<long> InheritedMovementBatchIds,
    RoundEndingBoundaryStage Stage = RoundEndingBoundaryStage.ReturningHolds,
    long? ActiveWindowFrameId = null,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.RoundEndingBoundary, Step);

public sealed record RoundEndingStartedEvent(long BoundaryFrameId, int RoundNumber,
    int ActualTurnNumber, int ActorSeat, int NextNormalSeat) : IGameEvent;
public sealed record RoundEndedEvent(long BoundaryFrameId, int RoundNumber,
    int ActualTurnNumber, int ActorSeat) : IGameEvent;

public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? RoundEndingBoundaryId { get; init; }
}

public sealed partial record CardsMovedTriggerWindowFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeRoundEndingBoundaryId { get; init; }
}
