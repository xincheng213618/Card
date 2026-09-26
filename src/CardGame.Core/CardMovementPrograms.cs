namespace CardGame.Core;

/// <summary>
/// Frozen facts for one atomic card-zone operation. Nested operations retain
/// both their rules-frame parent and their immediate movement-batch parent.
/// </summary>
public sealed record CardMovementBatchContext(
    long Id,
    long? ParentFrameId,
    long? ParentBatchId,
    int TurnNumber,
    IReadOnlyList<CardMovementRecord> Movements,
    IReadOnlyList<CardMovementSourceCount> SourceCounts,
    long? AwaitingProgramFrameId = null);

public sealed record CardMovementSourceCount(
    CardLocation Location,
    int CountBefore,
    int CountAfter);

/// <summary>
/// Serializable ordered cursor for configured post-movement triggers.
/// The physical batch is already committed; this frame only coordinates the
/// resulting optional program bindings at the next safe rules boundary.
/// </summary>
public sealed record CardsMovedTriggerWindowFrame(
    long Id,
    CardMovementBatchContext Batch,
    IReadOnlyList<ProgramTriggerCandidate> Candidates,
    int CandidateIndex = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.CardsMovedTriggerWindow, Step);
