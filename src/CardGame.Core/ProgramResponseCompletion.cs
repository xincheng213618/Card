using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>A finished response returns to the same native response cursor, without paying its entities again.</summary>
public sealed record ProgramResponseCompletionReturn(long ActionId, long ParentFrameId,
    long? AttackOwnerFrameId, ProgramCardContinuation OriginalContinuation, int ActualTurnNumber, int ActualTurnOwnerSeat)
{
    private IReadOnlyList<CardMovementRecord> _costs = Array.AsReadOnly(Array.Empty<CardMovementRecord>());
    private IReadOnlyList<CardMovementBatchContext> _batches = Array.AsReadOnly(Array.Empty<CardMovementBatchContext>());
    private IReadOnlyList<RecoveryAttempt> _recoveries = Array.AsReadOnly(Array.Empty<RecoveryAttempt>());
    private IReadOnlyList<HpChangeContext> _health = Array.AsReadOnly(Array.Empty<HpChangeContext>());
    public int CompletionActorSeat { get; init; }
    public IReadOnlyList<CardMovementRecord> NativeCosts { get => _costs; init => _costs = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardMovementBatchContext> CostBatches
    {
        get => _batches;
        init => _batches = Array.AsReadOnly(value.Select(b => b with {
            Movements = Array.AsReadOnly(b.Movements.ToArray()), SourceCounts = Array.AsReadOnly(b.SourceCounts.ToArray()),
            DestinationCounts = b.DestinationCounts is null ? null : Array.AsReadOnly(b.DestinationCounts.ToArray()) }).ToArray());
    }
    public int CostBatchCursor { get; init; }
    public IReadOnlyList<RecoveryAttempt> CostRecoveries { get => _recoveries; init => _recoveries = Array.AsReadOnly(value.Select(a => a with {
        Candidates = Array.AsReadOnly(a.Candidates.ToArray()), Completion = a.Completion with {
            Policies = a.Completion.Policies is null ? null : Array.AsReadOnly(a.Completion.Policies.ToArray()) } }).ToArray()); }
    public IReadOnlyList<HpChangeContext> CostHealthChanges { get => _health; init => _health = Array.AsReadOnly(value.ToArray()); }
    public int CostRecoveryCursor { get; init; }
    public int CostHealthCursor { get; init; }
    public long? ActiveHealthChildFrameId { get; init; }
    public long? ActiveCostChildFrameId { get; init; }
    public bool CostsDrained { get; init; }
}

public sealed partial record ProgramCardTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramResponseCompletionReturn? ResponseCompletion { get; init; }
}

// Only scalar public appearance is reported. Conversion/material provenance remains
// on the trusted CardActionContext, as it does for the existing accepted boundary.
public sealed record CardResponseCompletedEvent(long ActionId, long ParentFrameId,
    long? AttackOwnerFrameId, int ActorSeat, int ProviderSeat, CardKind EffectiveKind,
    ProgramCardContinuation OriginalContinuation, int ActualTurnNumber, int ActualTurnOwnerSeat,
    int NativeActorSeat) : IGameEvent;

public sealed record CardResponseCompletionStartedEvent(long FrameId, long ActionId, long ParentFrameId,
    int ActorSeat, int ProviderSeat, CardKind EffectiveKind, ProgramCardContinuation OriginalContinuation,
    int ActualTurnNumber, int ActualTurnOwnerSeat, int NativeActorSeat) : IGameEvent;

public sealed partial record CardsMovedTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeResponseCompletionFrameId { get; init; }
}
