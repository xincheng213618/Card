using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record LeXidiOpeningEntity(int OwnerSeat, int CardId, long EntrySequence);
public sealed record LeXidiEntityMarkedEvent(long FrameId, CardConversionSource Source, int CardId, long EntrySequence, bool Initial) : IGameEvent;
public sealed record LeXidiInitializedEvent(long FrameId, CardConversionSource Source, long WindowId, int Count) : IGameEvent;
public sealed record LeXidiReorderStartedEvent(long FrameId, int OwnerSeat, int FluteCount, int RequestedCount) : IGameEvent;
internal sealed class InitializeLeZhugeGuoXidiDescriptor : LeZhugeGuoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeLeZhugeGuoXidi;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)];
}
internal sealed class ReorderLeZhugeGuoXidiDescriptor : LeZhugeGuoDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReorderLeZhugeGuoXidi; }
