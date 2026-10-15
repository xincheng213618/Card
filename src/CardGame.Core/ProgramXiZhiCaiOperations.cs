namespace CardGame.Core;
internal interface IXiZhiCaiProgramHost
{
 SkillProgramStepOutcome ArmXianfuRelation(ProgramSkillFrame frame,int target);
 SkillProgramStepOutcome SettleXianfuNativeEcho(ProgramSkillFrame frame);
 SkillProgramStepOutcome StartChouceDamagePointJudgment(ProgramSkillFrame frame,SkillProgramEffect effect);
 SkillProgramStepOutcome ResolveChouceFrozenJudgment(ProgramSkillFrame frame,SkillProgramEffect effect);
}
internal abstract class XiZhiCaiDescriptor:ProgramOperationDescriptorBase
{
 public override ISkillProgramEffectHandler Handler=>new XiZhiCaiHandler(Op);
 public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.StartJudgment,static(_,_)=>{});
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
 {
  var judgment=Op==SkillProgramEffectOp.StartChouceDamagePointJudgment;var result=Op==SkillProgramEffectOp.ResolveChouceFrozenJudgment;
  if(judgment)r.AllowOnly("op","target","judgmentReason","resultBind","visibility","condition");else if(result)r.AllowOnly("op","target","sourceBind","condition");else r.AllowOnly("op","target","condition");
  var target=r.RequiredEnum<SkillProgramEffectTarget>("target");if(target!=(Op==SkillProgramEffectOp.ArmXianfuRelation?SkillProgramEffectTarget.SelectedTarget:SkillProgramEffectTarget.Owner))throw new InvalidOperationException("XiZhiCai requires the exact owning or selected participant.");
  var e=new SkillProgramEffect(Op,target,0,r.Condition(),judgmentReason:judgment?r.RequiredIdentifier("judgmentReason"):null,resultBind:judgment?r.RequiredIdentifier("resultBind"):null,visibility:judgment?r.RequiredEnum<SkillProgramCardSetVisibility>("visibility"):SkillProgramCardSetVisibility.Public,sourceBind:result?r.RequiredIdentifier("sourceBind"):null);
  RequireAlways(e,r.Path);if(judgment&&e.Visibility!=SkillProgramCardSetVisibility.Public)throw new InvalidOperationException("Chouce requires native public judgment.");return e;
 }
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>Op switch
 {
  SkillProgramEffectOp.ArmXianfuRelation=>[new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting),new ReadSelectedTarget(),new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)],
  SkillProgramEffectOp.StartChouceDamagePointJudgment=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied),new CreateCardSet(e.ResultBind!,1,false,AlreadyMoved:true)],
  SkillProgramEffectOp.ResolveChouceFrozenJudgment=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied),new ReadFrozenSingleCardSet(e.SourceBind!)],_=>[]
 };
}
internal sealed class ArmXianfuRelationDescriptor:XiZhiCaiDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ArmXianfuRelation;}
internal sealed class SettleXianfuNativeEchoDescriptor:XiZhiCaiDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.SettleXianfuNativeEcho;}
internal sealed class StartChouceDamagePointJudgmentDescriptor:XiZhiCaiDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.StartChouceDamagePointJudgment;public override ProgramContextCapability RequiredCapabilities=>ProgramContextCapability.Judgment|ProgramContextCapability.Damage;}
internal sealed class ResolveChouceFrozenJudgmentDescriptor:XiZhiCaiDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveChouceFrozenJudgment;public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;}
internal sealed class XiZhiCaiHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
 public SkillProgramEffectOp Op=>op;
 public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>op switch
 {SkillProgramEffectOp.ArmXianfuRelation=>((IXiZhiCaiProgramHost)h).ArmXianfuRelation(f,seat),SkillProgramEffectOp.SettleXianfuNativeEcho=>((IXiZhiCaiProgramHost)h).SettleXianfuNativeEcho(f),SkillProgramEffectOp.StartChouceDamagePointJudgment=>((IXiZhiCaiProgramHost)h).StartChouceDamagePointJudgment(f,e),_=>((IXiZhiCaiProgramHost)h).ResolveChouceFrozenJudgment(f,e)};
}
internal static class XiZhiCaiContract
{
 internal static bool Own(SkillProgramEffect e)=>e.Op is SkillProgramEffectOp.ArmXianfuRelation or SkillProgramEffectOp.SettleXianfuNativeEcho or SkillProgramEffectOp.StartChouceDamagePointJudgment or SkillProgramEffectOp.ResolveChouceFrozenJudgment;
 internal static void Activation(string path,SkillProgramActivation a){if(a.Effects.Any(Own))throw new InvalidOperationException(path+": XiZhiCai requires its actual game-start, damage or recovery producer.");}
 internal static void Trigger(string path,SkillProgramTrigger t)
 {
  if(!t.Effects.Any(Own))return;bool valid;
  if(t.Effects.Any(e=>e.Op==SkillProgramEffectOp.ArmXianfuRelation))valid=t.Window==SkillProgramTriggerWindow.GameStarting&&t.Subject==SkillProgramTriggerSubject.Owner&&!t.Optional&&t.Effects is [{Op:SkillProgramEffectOp.SelectTarget,TargetKind:SkillProgramTargetKind.OtherLiving},{Op:SkillProgramEffectOp.ArmXianfuRelation}];
  else if(t.Effects.Any(e=>e.Op==SkillProgramEffectOp.SettleXianfuNativeEcho))valid=!t.Optional&&t.OwnerRelation is null&&t.MarkerCost is null&&t.SourceSkillId is null&&t.SourceViewAsId is null&&t.RequireDamageSource is null&&t.RequireNoCardConversion is null&&t.DamageCardKinds.Count==0&&t.Effects is [{Op:SkillProgramEffectOp.SettleXianfuNativeEcho}]&&
   (t.Window==SkillProgramTriggerWindow.AfterDamageApplied&&t.Subject==SkillProgramTriggerSubject.Any&&t.DamageOccurrence==SkillProgramDamageOccurrence.PerDamage||
    t.Window==SkillProgramTriggerWindow.AfterHpRecovered&&t.Subject==SkillProgramTriggerSubject.Owner&&t.DamageOccurrence is null&&t.HpChangeOccurrence==SkillProgramHpChangeOccurrence.PerEvent);
  else valid=t.Window==SkillProgramTriggerWindow.AfterDamageApplied&&t.Subject==SkillProgramTriggerSubject.Owner&&t.OwnerRelation is null&&t.DamageOccurrence==SkillProgramDamageOccurrence.PerDamagePoint&&t.Optional&&t.Effects is [{Op:SkillProgramEffectOp.StartChouceDamagePointJudgment} start,{Op:SkillProgramEffectOp.ResolveChouceFrozenJudgment} result]&&start.ResultBind==result.SourceBind;
  if(!valid||t.UsageLimit is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always)throw new InvalidOperationException(path+": XiZhiCai requires its exact native relation or point judgment contract.");
 }
}
