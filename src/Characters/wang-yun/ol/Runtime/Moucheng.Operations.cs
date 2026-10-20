using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record WangYunAwakeningReceipt(CardConversionSource Source,string GameplayHash,long ParentFrameId,int Turn,int InstructionIndex);
public sealed record WangYunAwakenedEvent(long FrameId,int OwnerSeat,string LostSkillId,string AcquiredSkillId):IGameEvent;
internal sealed class AwakenFromIssuedLianjiSlashDamageDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.AwakenFromIssuedLianjiSlashDamage;
    public override ISkillProgramEffectHandler Handler{get;}=new WangYunHandler(1);
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GrantSkills,static(_,_)=>{});
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
