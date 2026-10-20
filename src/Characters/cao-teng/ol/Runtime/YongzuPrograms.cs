using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record CaoTengHandLimitGrantedEvent(long FrameId,int Index,int RecipientSeat):IGameEvent;
public sealed record CaoTengSkillGrantedEvent(long FrameId,int Index,int RecipientSeat,string SkillId,string SkillInstanceId,string GrantId,string SourceId,int ActualTurn):IGameEvent;
public sealed record CaoTengSkillExpiredEvent(string GrantId,int RecipientSeat,int ActualTurn):IGameEvent;
internal sealed class ResolveYongzuDistinctBenefitsDescriptor:CaoTengDescriptor
{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveYongzuDistinctBenefits;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];}
