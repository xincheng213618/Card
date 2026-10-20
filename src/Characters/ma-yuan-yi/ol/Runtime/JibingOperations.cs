using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class ReplaceJibingDrawWithTwoBingDescriptor():MaYuanYiDescriptor(SkillProgramEffectOp.ReplaceJibingDrawWithTwoBing,SkillProgramTriggerWindow.DrawPhaseStarting)
{
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>{c.Draw(new(SkillProgramEffectOp.Draw,SkillProgramEffectTarget.Owner,2,e.Condition));c.PublicControlValue(2);});
}
