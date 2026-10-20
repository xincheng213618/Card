using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record BuZhiDamageIssuedEvent(long FrameId,int SourceSeat,int TargetSeat,int Amount):IGameEvent;
internal sealed class ResolveRebelCountEquipmentChoiceDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveRebelCountEquipmentChoice;
    public override ISkillProgramEffectHandler Handler{get;}=new BuZhiHandler(false);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>c.Draw(new(SkillProgramEffectOp.Draw,SkillProgramEffectTarget.Owner,1,e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ReadSelectedTarget()];
}
