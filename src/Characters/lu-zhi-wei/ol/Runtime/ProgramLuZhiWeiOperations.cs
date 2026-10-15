namespace CardGame.Core;
internal interface ILuZhiWeiProgramHost
{
 SkillProgramStepOutcome DrawAndArmQingzhongActualPlayDebt(ProgramSkillFrame frame);
 SkillProgramStepOutcome SettleQingzhongActualPlayDebt(ProgramSkillFrame frame);
}
internal abstract class QingzhongDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override ISkillProgramEffectHandler Handler=>new QingzhongHandler(Op);
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>c.Draw(new(SkillProgramEffectOp.Draw,SkillProgramEffectTarget.Owner,2,e.Condition)));
}
internal sealed class DrawAndArmQingzhongActualPlayDebtDescriptor:QingzhongDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DrawAndArmQingzhongActualPlayDebt;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}
internal sealed class SettleQingzhongActualPlayDebtDescriptor:QingzhongDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.SettleQingzhongActualPlayDebt;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)];
}
internal sealed class QingzhongHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
 public SkillProgramEffectOp Op=>op;
 public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int s,ISkillProgramEffectHost h)=>op==SkillProgramEffectOp.DrawAndArmQingzhongActualPlayDebt?h.GetCapability<ILuZhiWeiProgramHost>().DrawAndArmQingzhongActualPlayDebt(f):h.GetCapability<ILuZhiWeiProgramHost>().SettleQingzhongActualPlayDebt(f);
}
internal static class LuZhiWeiContract
{
 internal static void ValidateActivation(string path,SkillProgramActivation a)
 {if(a.Effects.Any(e=>e.Op is SkillProgramEffectOp.DrawAndArmQingzhongActualPlayDebt or SkillProgramEffectOp.SettleQingzhongActualPlayDebt))throw new InvalidOperationException(path+": Qingzhong requires actual own Play boundaries.");}
 internal static void ValidateTrigger(string path,SkillProgramTrigger t)
 {
  if(!t.Effects.Any(e=>e.Op is SkillProgramEffectOp.DrawAndArmQingzhongActualPlayDebt or SkillProgramEffectOp.SettleQingzhongActualPlayDebt))return;
  if(t.Effects.Count!=1||t.Subject!=SkillProgramTriggerSubject.Owner||t.OwnerRelation is not null||t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own||t.UsageLimit is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.MarkerCost is not null||
   !(t.Window==SkillProgramTriggerWindow.PlayPhaseStarting&&t.Optional&&t.Effects[0].Op==SkillProgramEffectOp.DrawAndArmQingzhongActualPlayDebt||t.Window==SkillProgramTriggerWindow.PlayEnding&&!t.Optional&&t.Effects[0].Op==SkillProgramEffectOp.SettleQingzhongActualPlayDebt))throw new InvalidOperationException(path+": Qingzhong requires optional actual draw then mandatory matching phase debt.");
 }
}
