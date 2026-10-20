using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record MouXiahouShiTurnKey(int TurnNumber, int OwnerSeat, int Ordinal);
public sealed record MouXiahouShiDiscardPayment(int CardId, CardKind PrintedKind, CardLocation From, CardLocation To, long Sequence);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouXiahouShiYanyuReceipt? MouXiahouShiYanyu { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouXiahouShiQiaoshiReceipt? MouXiahouShiQiaoshi { get; init; }
}
public sealed record MouXiahouShiDrawReturnedEvent(long FrameId, int RecipientSeat, int RequestedCount, int ActualCount,
    int ReplacementCount, long SequenceBefore, long SequenceAfter, long? AttemptId) : IGameEvent;
public sealed record MouXiahouShiCompletedEvent(long FrameId, string SkillId, bool Accepted) : IGameEvent;
public sealed record MouXiahouShiMovementSignedEvent(long FrameId, int CardId, CardKind Kind, CardLocation From,
    CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;

internal interface IMouXiahouShiHost
{
    SkillProgramStepOutcome Discard(ProgramSkillFrame frame);
    SkillProgramStepOutcome Support(ProgramSkillFrame frame);
    SkillProgramStepOutcome Qiaoshi(ProgramSkillFrame frame);
}
internal abstract class MouXiahouShiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
}
