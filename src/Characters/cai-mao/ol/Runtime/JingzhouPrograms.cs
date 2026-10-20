using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record CaiMaoToggleIssuedEvent(long FrameId,int Index,int TargetSeat,bool Before,bool Requested,bool Actual):IGameEvent;
internal sealed class ResolveJingzhouBeforeDamageTogglesDescriptor:CaiMaoDescriptor
{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveJingzhouBeforeDamageToggles;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];}
