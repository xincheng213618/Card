using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record FudaoHandRecordedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int HandCount):IGameEvent;
internal sealed class InitializeFudaoHandRecordDescriptor():DingShangWanDescriptor(SkillProgramEffectOp.InitializeFudaoHandRecord)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)]; }
internal sealed class DrawFudaoRecordedPairDescriptor():DingShangWanDescriptor(SkillProgramEffectOp.DrawFudaoRecordedPair)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)]; }
