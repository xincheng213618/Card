using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record ZhaoXiangConversionPayment(long ActionId,CardConversionSource Source,string GameplayHash,int ActualTurn,int ActualActorSeat,int ProviderSeat,int? RequesterSeat,int PhysicalCardId,CardKind InputKind,CardKind OutputKind,int Removed,int Remaining):IGameEvent;
public sealed record ZhaoXiangMeiYingGainedEvent(long ProgramFrameId,long DamageWindowId,long DamageParentId,CardConversionSource Source,int Amount,int Total):IGameEvent;
public sealed record ZhaoXiangConversionDrawPaidEvent(long FrameId,long ActionId,int OwnerSeat,long Before,long After,int Actual):IGameEvent;
internal sealed class GainMeiYingAfterActualSlashDamageDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.GainMeiYingAfterActualSlashDamage;
    public override ISkillProgramEffectHandler Handler{get;}=new ZhaoXiangHandler(0);
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,_)=>{});
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class DrawAfterPaidMeiYingConversionDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DrawAfterPaidMeiYingConversion;
    public override ISkillProgramEffectHandler Handler{get;}=new ZhaoXiangHandler(2);
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>c.Draw(new(SkillProgramEffectOp.Draw,SkillProgramEffectTarget.Owner,1,e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
}
