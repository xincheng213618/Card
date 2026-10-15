namespace CardGame.Core;
internal interface IKanZeProgramHost
{
 SkillProgramStepOutcome BeginXiashu(ProgramSkillFrame frame,int recipient);
 SkillProgramStepOutcome ArmKuanshi(ProgramSkillFrame frame,int target);
}
internal abstract class KanZeDescriptor:ProgramOperationDescriptorBase
{
 public override ISkillProgramEffectHandler Handler=>new KanZeHandler(Op);
 public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.Gift,static(_,_)=>{});
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
 {r.AllowOnly("op","target","condition");if(r.RequiredEnum<SkillProgramEffectTarget>("target")!=SkillProgramEffectTarget.SelectedTarget)throw new InvalidOperationException("KanZe requires its selected actual target.");var e=new SkillProgramEffect(Op,SkillProgramEffectTarget.SelectedTarget,0,r.Condition());RequireAlways(e,r.Path);return e;}
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ReadSelectedTarget(),new RequireSelectedTargetKind(Op==SkillProgramEffectOp.GiveAllHandAndResolveXiashu?SkillProgramTargetKind.OtherLiving:SkillProgramTargetKind.AnyLiving),new RequireTriggerWindow(Op==SkillProgramEffectOp.GiveAllHandAndResolveXiashu?SkillProgramTriggerWindow.PlayPhaseStarting:SkillProgramTriggerWindow.TurnEnding)];
}
internal sealed class GiveAllHandAndResolveXiashuDescriptor:KanZeDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.GiveAllHandAndResolveXiashu;public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;}
internal sealed class ArmKuanshiProtectionDescriptor:KanZeDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ArmKuanshiProtection;}
internal sealed class KanZeHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>op==SkillProgramEffectOp.GiveAllHandAndResolveXiashu?((IKanZeProgramHost)h).BeginXiashu(f,seat):((IKanZeProgramHost)h).ArmKuanshi(f,seat);}
internal static class KanZeContract
{
 internal static void Activation(string path,SkillProgramActivation a){if(a.Effects.Any(e=>e.Op is SkillProgramEffectOp.GiveAllHandAndResolveXiashu or SkillProgramEffectOp.ArmKuanshiProtection))throw new InvalidOperationException(path+": KanZe needs its actual phase boundary.");}
 internal static void Trigger(string path,SkillProgramTrigger t)
 {var ops=t.Effects.Where(e=>e.Op is SkillProgramEffectOp.GiveAllHandAndResolveXiashu or SkillProgramEffectOp.ArmKuanshiProtection).ToArray();if(ops.Length==0)return;var xiashu=ops[0].Op==SkillProgramEffectOp.GiveAllHandAndResolveXiashu;if(ops.Length!=1||t.Effects is not [{Op:SkillProgramEffectOp.SelectTarget} select,var last]||last!=ops[0]||select.Target!=SkillProgramEffectTarget.Owner||select.TargetKind!=(xiashu?SkillProgramTargetKind.OtherLiving:SkillProgramTargetKind.AnyLiving)||t.Window!=(xiashu?SkillProgramTriggerWindow.PlayPhaseStarting:SkillProgramTriggerWindow.TurnEnding)||t.Subject!=SkillProgramTriggerSubject.Owner||!t.Optional||t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own||t.OwnerRelation is not null||t.UsageLimit is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always)throw new InvalidOperationException(path+": KanZe requires one native boundary, selected target and terminal instruction.");}
}
