namespace CardGame.Core;
public sealed record NativeBuZhiDiscardMaterial(int CardId, CardKind PrintedKind, CardLocation From, CardLocation To, long MovementSequence);
public sealed record NativeBuZhiDiscardInvocationProof
{
    public long InvocationId { get; init; }
    public long ParentFrameId { get; init; }
    public required CardConversionSource Source { get; init; }
    public required string GameplayHash { get; init; }
    public int InstructionIndex { get; init; }
    public int ActualTurnNumber { get; init; }
    public int ActualTurnOwnerSeat { get; init; }
    public required CardMoveReason Reason { get; init; }
    public long SequenceBefore { get; init; }
    public long SequenceAfter { get; init; }
    public long LastBatchId { get; init; }
    private readonly IReadOnlyList<CardZoneKind> _zones = Array.AsReadOnly(Array.Empty<CardZoneKind>());
    public IReadOnlyList<CardZoneKind> Zones { get => _zones; init => _zones = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<NativeBuZhiDiscardMaterial> _materials = Array.AsReadOnly(Array.Empty<NativeBuZhiDiscardMaterial>());
    public IReadOnlyList<NativeBuZhiDiscardMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public int ActualCount => Materials.Count;
}
public sealed record NativeBuZhiDiscardInvocationRecordedEvent(long InvocationId, long ParentFrameId, CardConversionSource Source,
    string GameplayHash, int InstructionIndex, int ActualTurnNumber, int ActualTurnOwnerSeat, CardMoveReason Reason,
    long SequenceBefore, long SequenceAfter, long LastBatchId, int ActualCount, int ZoneMask) : IGameEvent;
