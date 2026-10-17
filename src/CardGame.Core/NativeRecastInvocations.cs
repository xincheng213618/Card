using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>One paid recast instruction for one participant, independent of its material count.</summary>
public sealed record NativeRecastInvocationRecordedEvent(long InvocationId, int TurnNumber,
    int ActualTurnOwnerSeat, int InitiatorSeat, int ExecutorSeat, int PrimaryCount,
    int ActualRecastCount, int ActualDrawCount, long? ParentFrameId, int? InstructionIndex,
    int? ParticipantCursor, int SequenceBefore, int SequenceAfter) : IGameEvent;

public sealed record NativeRecastMaterial(int CardId, CardKind PrintedKind, CardLocation From,
    bool IsGeneralWeapon);

public sealed record NativeRecastInvocationDraft
{
    public long InvocationId { get; init; }
    public int TurnNumber { get; init; }
    public int ActualTurnOwnerSeat { get; init; }
    public int InitiatorSeat { get; init; }
    public int ExecutorSeat { get; init; }
    public int SequenceBefore { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ParentFrameId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? InstructionIndex { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ParticipantCursor { get; init; }
    public CardMoveReason Reason { get; init; }
    private IReadOnlyList<NativeRecastMaterial> _materials = Array.AsReadOnly(Array.Empty<NativeRecastMaterial>());
    public IReadOnlyList<NativeRecastMaterial> Materials
    { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeRecastInvocationDraft? NativeRecastInvocation { get; init; }
}

public sealed partial record EquipmentRecastFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeRecastInvocationDraft? NativeRecastInvocation { get; init; }
}
