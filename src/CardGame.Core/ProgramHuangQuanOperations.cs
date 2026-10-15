namespace CardGame.Core;
internal interface IHuangQuanProgramHost
{
 SkillProgramStepOutcome ArmDianhuRelation(ProgramSkillFrame frame,int target);
 SkillProgramStepOutcome DrawForDianhuActualOccurrence(ProgramSkillFrame frame);
 SkillProgramStepOutcome DrawAndOfferJianjiExactCard(ProgramSkillFrame frame,int target);
}
internal abstract class HuangQuanDescriptor:ProgramOperationDescriptorBase
{
 public override ISkillProgramEffectHandler Handler=>new HuangQuanHandler(Op);
 public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>c.Draw(new(SkillProgramEffectOp.Draw,e.Target,1,e.Condition)));
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
 {
  r.AllowOnly("op","target","condition");var target=r.RequiredEnum<SkillProgramEffectTarget>("target");
  if(target!=(Op==SkillProgramEffectOp.DrawForDianhuActualOccurrence?SkillProgramEffectTarget.Owner:SkillProgramEffectTarget.SelectedTarget))throw new InvalidOperationException(r.Path+": Huang Quan requires its actual selected counterpart or owner.");
  var e=new SkillProgramEffect(Op,target,0,r.Condition());RequireAlways(e,r.Path);return e;
 }
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>Op==SkillProgramEffectOp.DrawForDianhuActualOccurrence?[]:Op==SkillProgramEffectOp.ArmDianhuRelation?[new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting),new ReadSelectedTarget(),new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)]:[new ReadSelectedTarget(),new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)];
}
internal sealed class ArmDianhuRelationDescriptor:HuangQuanDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ArmDianhuRelation;}
internal sealed class DrawForDianhuActualOccurrenceDescriptor:HuangQuanDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DrawForDianhuActualOccurrence;}
internal sealed class DrawAndOfferJianjiExactCardDescriptor:HuangQuanDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DrawAndOfferJianjiExactCard;public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;}
internal sealed class HuangQuanHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
 public SkillProgramEffectOp Op=>op;
 public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>op switch
 {SkillProgramEffectOp.ArmDianhuRelation=>((IHuangQuanProgramHost)h).ArmDianhuRelation(f,seat),SkillProgramEffectOp.DrawForDianhuActualOccurrence=>((IHuangQuanProgramHost)h).DrawForDianhuActualOccurrence(f),_=>((IHuangQuanProgramHost)h).DrawAndOfferJianjiExactCard(f,seat)};
}
internal static class HuangQuanContract
{
 internal static bool Own(SkillProgramEffect e)=>e.Op is SkillProgramEffectOp.ArmDianhuRelation or SkillProgramEffectOp.DrawForDianhuActualOccurrence or SkillProgramEffectOp.DrawAndOfferJianjiExactCard;
 internal static void Activation(string path,SkillProgramActivation a)
 {if(!a.Effects.Any(Own))return;if(a.Effects is not [{Op:SkillProgramEffectOp.DrawAndOfferJianjiExactCard}]||a.MinCards!=0||a.MaxCards!=0||a.MinTargets!=1||a.MaxTargets!=1||a.TargetKind!=SkillProgramTargetKind.OtherLiving||a.UsesPerPhase!=1||a.UsesPerTurn is not null)throw new InvalidOperationException(path+": Jianji requires one real other-living target once per actual Play.");}
 internal static void Trigger(string path,SkillProgramTrigger t)
 {
  if(!t.Effects.Any(Own))return;
  var arm=t.Effects is [{Op:SkillProgramEffectOp.SelectTarget,TargetKind:SkillProgramTargetKind.OtherLiving},{Op:SkillProgramEffectOp.ArmDianhuRelation}]&&t.Window==SkillProgramTriggerWindow.GameStarting&&t.Subject==SkillProgramTriggerSubject.Owner;
  var occurrence=t.Effects is [{Op:SkillProgramEffectOp.DrawForDianhuActualOccurrence}]&&t.OwnerRelation is null&&t.MarkerCost is null&&(t.Window==SkillProgramTriggerWindow.AfterDamageApplied&&t.Subject==SkillProgramTriggerSubject.DamageSource&&t.DamageOccurrence==SkillProgramDamageOccurrence.PerDamage||t.Window==SkillProgramTriggerWindow.AfterHpRecovered&&t.Subject==SkillProgramTriggerSubject.Owner&&t.HpChangeOccurrence==SkillProgramHpChangeOccurrence.PerEvent&&t.DamageOccurrence is null);
  if((!arm&&!occurrence)||t.Optional||t.UsageLimit is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always)throw new InvalidOperationException(path+": Dianhu requires its exact actual permanent relation and native occurrence.");
 }
}
