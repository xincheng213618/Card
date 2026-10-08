using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>The actual supplied play is distinct from the requester's accepted Use.</summary>
public sealed record ProgramCardSupplyCompletionReturn
{
    private IReadOnlyList<CardMovementRecord> _costs = Array.AsReadOnly(Array.Empty<CardMovementRecord>());
    private IReadOnlyList<CardMovementBatchContext> _batches = Array.AsReadOnly(Array.Empty<CardMovementBatchContext>());
    private IReadOnlyList<RecoveryAttempt> _recoveries = Array.AsReadOnly(Array.Empty<RecoveryAttempt>());
    private IReadOnlyList<HpChangeContext> _health = Array.AsReadOnly(Array.Empty<HpChangeContext>());
    public long ActionId { get; init; }
    public long ParentFrameId { get; init; }
    public long RequestOwnerFrameId { get; init; }
    public FactionCardRequestPurpose Purpose { get; init; }
    public int RequesterSeat { get; init; }
    public int ProviderSeat { get; init; }
    public CardKind EffectiveKind { get; init; }
    public int ActualTurnNumber { get; init; }
    public int ActualTurnOwnerSeat { get; init; }
    public IReadOnlyList<CardMovementRecord> NativeCosts { get => _costs; init => _costs = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardMovementBatchContext> CostBatches
    {
        get => _batches;
        init => _batches = Array.AsReadOnly(value.Select(b => b with {
            Movements = Array.AsReadOnly(b.Movements.ToArray()), SourceCounts = Array.AsReadOnly(b.SourceCounts.ToArray()),
            DestinationCounts = b.DestinationCounts is null ? null : Array.AsReadOnly(b.DestinationCounts.ToArray()) }).ToArray());
    }
    public IReadOnlyList<RecoveryAttempt> CostRecoveries { get => _recoveries; init => _recoveries = Array.AsReadOnly(value.Select(a => a with {
        Candidates = Array.AsReadOnly(a.Candidates.ToArray()), Completion = a.Completion with {
            Policies = a.Completion.Policies is null ? null : Array.AsReadOnly(a.Completion.Policies.ToArray()) } }).ToArray()); }
    public IReadOnlyList<HpChangeContext> CostHealthChanges { get => _health; init => _health = Array.AsReadOnly(value.ToArray()); }
    public int CostBatchCursor { get; init; }
    public int CostRecoveryCursor { get; init; }
    public int CostHealthCursor { get; init; }
    public long? ActiveCostChildFrameId { get; init; }
    public long? ActiveHealthChildFrameId { get; init; }
    public bool CostsDrained { get; init; }
}

public sealed partial record ProgramCardTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramCardSupplyCompletionReturn? CardSupplyCompletion { get; init; }
}
public sealed partial record CardsMovedTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeCardSupplyCompletionFrameId { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CompletedCardSupplyFrameId { get; init; }
}

// No uncommitted/private material IDs are added to public completion facts.
public sealed record CardSupplyCompletionStartedEvent(long FrameId, long ActionId, long ParentFrameId,
    long RequestOwnerFrameId, FactionCardRequestPurpose Purpose, int RequesterSeat, int ProviderSeat,
    CardKind EffectiveKind, int ActualTurnNumber, int ActualTurnOwnerSeat) : IGameEvent;
public sealed record CardSupplyCompletedEvent(long FrameId, long ActionId, long ParentFrameId,
    int RequesterSeat, int ProviderSeat, CardKind EffectiveKind, int ActualTurnNumber, int ActualTurnOwnerSeat) : IGameEvent;
