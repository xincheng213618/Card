using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record ZhaoXiangExchangePaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,long ParentFrameId,int Turn,int Removed,int CumulativeRemoved,string PoolFingerprint):IGameEvent;
public sealed record ZhaoXiangExchangeSelectedEvent(long FrameId,int OwnerSeat,string OriginalGeneralId,string GeneralId,int MaximumBefore,int MaximumAfter):IGameEvent;
public sealed record ZhaoXiangExchangeRecoveryIssuedEvent(long FrameId,int OwnerSeat,int Requested):IGameEvent;
internal sealed class ExchangeFiveShuBySpentMeiYingDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ExchangeFiveShuBySpentMeiYing;
    public override ISkillProgramEffectHandler Handler{get;}=new ZhaoXiangHandler(1);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.Recover,static(e,c)=>c.Recover(new(SkillProgramEffectOp.Recover,SkillProgramEffectTarget.Owner,1,e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
