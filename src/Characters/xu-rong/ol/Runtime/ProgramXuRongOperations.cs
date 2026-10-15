namespace CardGame.Core;
internal interface IXuRongProgramHost
{
 SkillProgramStepOutcome ExecuteXuRong(SkillProgramEffectOp op,ProgramSkillFrame frame);
}
internal abstract class XuRongDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override ISkillProgramEffectHandler Handler=>new XuRongHandler(Op);
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.Damage,static(e,c)=>c.Damage(new(SkillProgramEffectOp.Damage,SkillProgramEffectTarget.SelectedTarget,1,e.Condition)));
}
internal sealed class InitializeXionghuoPoolDescriptor:XuRongDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.InitializeXionghuoPool;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)];
}
internal sealed class TransferXionghuoMarkDescriptor:XuRongDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.TransferXionghuoMark;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ReadSelectedTarget()];
}
internal sealed class SettleXionghuoMarkedPlayDescriptor:XuRongDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.SettleXionghuoMarkedPlay;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}
internal sealed class ReplenishShaJueAndClaimDyingCauseDescriptor:XuRongDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ReplenishShaJueAndClaimDyingCause;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering)];
}
internal sealed class XuRongHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
 public SkillProgramEffectOp Op=>op;
 public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int s,ISkillProgramEffectHost h)=>h.GetCapability<IXuRongProgramHost>().ExecuteXuRong(op,f);
}
internal static class XuRongContract
{
 internal static bool IsXuRongOp(SkillProgramEffectOp op)=>(int)op is >=23600 and <=23603;
 internal static void ValidateActivation(string path,SkillProgramActivation a)
 {
  if(!a.Effects.Any(e=>IsXuRongOp(e.Op)))return;
  if(a.Effects is not [{Op:SkillProgramEffectOp.TransferXionghuoMark}]||a.MinCards!=0||a.MaxCards!=0||a.MinTargets!=1||a.MaxTargets!=1||a.TargetKind!=SkillProgramTargetKind.OtherLiving||a.UsesPerTurn is not null||a.UsesPerPhase is not null||a.UsesPerGame is not null||a.MarkerCost is not null||a.Condition.Kind!=SkillProgramConditionKind.Always)throw new InvalidOperationException(path+": Xionghuo transfers one real owned pool marker to one other living target without a printed usage quota.");
 }
 internal static void ValidateTrigger(string path,SkillProgramTrigger t)
 {
  if(!t.Effects.Any(e=>IsXuRongOp(e.Op)))return;
  if(t.Effects.Count!=1||t.Optional||t.OwnerRelation is not null||t.MarkerCost is not null||t.UsageLimit is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always)throw new InvalidOperationException(path+": Xu Rong requires one exact native mandatory producer.");
  var op=t.Effects[0].Op;
  if(!(op==SkillProgramEffectOp.InitializeXionghuoPool&&t.Window==SkillProgramTriggerWindow.GameStarting&&t.Subject==SkillProgramTriggerSubject.Owner&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.Own||op==SkillProgramEffectOp.SettleXionghuoMarkedPlay&&t.Window==SkillProgramTriggerWindow.PlayPhaseStarting&&t.Subject==SkillProgramTriggerSubject.Owner&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.OtherLiving||op==SkillProgramEffectOp.ReplenishShaJueAndClaimDyingCause&&t.Window==SkillProgramTriggerWindow.DyingEntering&&t.Subject==SkillProgramTriggerSubject.Any&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.Own))throw new InvalidOperationException(path+": Xu Rong's actual GameStart, foreign PlayStart and other Dying entry are distinct exact windows.");
 }
}
