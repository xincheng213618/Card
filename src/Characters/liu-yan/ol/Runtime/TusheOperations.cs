using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record LiuYanTusheDrawnEvent(long FrameId,int OwnerSeat,int Requested,int Count,long Before,long After):IGameEvent;
internal sealed class DrawTusheForActualNonEquipmentTargetsDescriptor:LiuYanDescriptor
{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DrawTusheForActualNonEquipmentTargets;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];}
