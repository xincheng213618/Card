using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum GuanduCardEffectStage { Drawing, DrawingChildren, RecoveryChildren, ChainChildren }
public sealed record GuanduCardEffectReceipt(long ActionId, long NullificationFrameId, CardKind Kind,
    int TargetSeat, int TargetIndex, int Occurrence, GuanduCardEffectStage Stage)
{
    public int RequestedCount { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    public long? AwaitingProgramFrameId { get; init; }
    public bool AlliedBranch { get; init; }
    public int RecoveryAmount { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GuanduCardEffectReceipt? GuanduCardEffect { get; init; }
}
public sealed partial record CardsMovedTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeGuanduCardEffectFrameId { get; init; }
}
public sealed record GuanduCardEffectStartedEvent(long UseFrameId, long ActionId, long NullificationFrameId,
    CardKind Kind, int TargetSeat, int TargetIndex, int Occurrence, bool AlliedBranch, int RequestedCount) : IGameEvent;
public sealed record GuanduCardDrawAppliedEvent(long UseFrameId, long NullificationFrameId, int Occurrence,
    int RecipientSeat, int RequestedCount, int ActualDrawCount, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record GuanduCardRecoveryIssuedEvent(long UseFrameId, long NullificationFrameId, int Occurrence,
    int SourceSeat, int TargetSeat, int Amount) : IGameEvent;
public sealed record GuanduIntoxicationCommittedEvent(long UseFrameId, long ActionId, int ActualTurnNumber,
    int SourceSeat, int TargetSeat) : IGameEvent;
public sealed record GuanduIntoxicationAppliedEvent(long UseFrameId, long ActionId, int ActualTurnNumber,
    int SourceSeat, int TargetSeat, int Occurrence) : IGameEvent;
