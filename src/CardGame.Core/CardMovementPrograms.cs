namespace CardGame.Core;

public sealed record CardMovementTiming(int ActualTurnOwnerSeat, TurnPhase Phase, int PhaseActorSeat);

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
    long? AwaitingProgramFrameId = null,
    IReadOnlyList<CardMovementSourceCount>? DestinationCounts = null,
    string? OriginSkillId = null,
    string? OriginSkillInstanceId = null,
    int? OriginOwnerSeat = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CardMovementTiming? MovementTiming { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ActualDiscardRecoveryPhaseKey? DiscardRecoveryPhase { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public NativeDrawInvocationProof? NativeDrawInvocation { get; init; }
}

public sealed record CardMovementSourceCount(
    CardLocation Location,
    int CountBefore,
    int CountAfter);

/// <summary>
/// Serializable ordered cursor for configured post-movement triggers.
/// The physical batch is already committed; this frame only coordinates the
/// resulting optional program bindings at the next safe rules boundary.
/// </summary>
public sealed partial record CardsMovedTriggerWindowFrame(
    long Id,
    CardMovementBatchContext Batch,
    IReadOnlyList<ProgramTriggerCandidate> Candidates,
    int CandidateIndex = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect,
    IReadOnlyList<ProgramSkillWindowContext>? Contexts = null,
    long? ResumeProgramFrameId = null)
    : ResolutionFrame(Id, ResolutionFrameKind.CardsMovedTriggerWindow, Step)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeDeclarationFrameId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeDrawFundedDistinctBasicFrameId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeRecoveryReplacementFrameId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeEquipmentRecastFrameId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeColorFireAttackFrameId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeCounterspellPaymentFrameId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public long? ResumeHistoricalEndingUseFrameId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public long? ResumeRoundPileAlcoholUseFrameId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeDrawPhaseObligationFrameId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeFactionRequestCostFrameId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DeferredTurnEndPreludeReturn? DeferredTurnEndReturn { get; init; }
}
