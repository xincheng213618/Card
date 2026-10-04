namespace CardGame.Core;
internal sealed record RequireMarkerLifecycle(bool Selected) : ProgramResourceOperation;
internal interface IAttributedEventProgramHost { void ApplyAttributedEvent(ProgramSkillFrame frame,SkillProgramEffect effect,int targetSeat); }
internal abstract class AttributedEventDescriptor : ProgramOperationDescriptorBase
{
 public override ISkillProgramEffectHandler Handler => SkillProgramEffectCatalog.Default.Resolve(Op);
 public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChangeAttributedMarker,static(e,c)=> { if(e.Op==SkillProgramEffectOp.ConsumeMarkerPreventDamage)c.PreventCurrentDamage(e); else if(e.Op==SkillProgramEffectOp.AddMarkerSubjectNormalDraw)c.AdjustNormalDraw(new SkillProgramEffect(SkillProgramEffectOp.AdjustNormalDraw,SkillProgramEffectTarget.Owner,1,e.Condition)); else c.PriceParticipantMarker(e); });
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
 {
  r.AllowOnly("op","target","targetRef","amount","marker","condition");var target=r.RequiredEnum<SkillProgramEffectTarget>("target");var amount=r.RequiredInt("amount");
  if(amount is < -20 or > 20 || amount==0 || amount<0&&target!=SkillProgramEffectTarget.Owner || target is not (SkillProgramEffectTarget.Owner or SkillProgramEffectTarget.SelectedTarget) || Op!=SkillProgramEffectOp.ChangeParticipantMarker && (target!=SkillProgramEffectTarget.Owner || amount!=1))throw new InvalidOperationException("Invalid attributed event marker operation.");
  var targetRef=r.Has("targetRef")?r.RequiredParticipantReference("targetRef"):null;
  if(targetRef is not null&&(target!=SkillProgramEffectTarget.Owner||targetRef.Kind is not (ProgramParticipantRef.EventTarget or ProgramParticipantRef.EventSource)))throw new InvalidOperationException("Attributed marker references support only owner placeholder with eventTarget or eventSource.");
  return new(Op,target,amount,r.Condition(),marker:r.RequiredEnum<PlayerMarkerKind>("marker"),targetReference:targetRef);
 }
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>Op switch {
  SkillProgramEffectOp.ConsumeMarkerPreventDamage=>[new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)],
  SkillProgramEffectOp.AddMarkerSubjectNormalDraw=>[new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseStarting)],
  _=>WithSelectedTarget(e,e.Target==SkillProgramEffectTarget.SelectedTarget ? [new RequireMarkerLifecycle(true),new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)] : [new RequireMarkerLifecycle(false)])};
}
internal sealed class ChangeParticipantMarkerDescriptor:AttributedEventDescriptor { public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ChangeParticipantMarker; }
internal sealed class ConsumeMarkerPreventDamageDescriptor:AttributedEventDescriptor { public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ConsumeMarkerPreventDamage; }
internal sealed class AddMarkerSubjectNormalDrawDescriptor:AttributedEventDescriptor { public override SkillProgramEffectOp Op=>SkillProgramEffectOp.AddMarkerSubjectNormalDraw; }
public abstract class AttributedEventHandler:ISkillProgramEffectHandler
{
 public abstract SkillProgramEffectOp Op {get;}
 public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host){((IAttributedEventProgramHost)host).ApplyAttributedEvent(f,e,target);return SkillProgramStepOutcome.Continue;}
}
public sealed record ProgramMarkerEventAppliedEvent(long ParentFrameId,int SubjectSeat,PlayerMarkerKind Marker,SkillProgramEffectOp Operation,int OwnerSeat,string SkillId,string SkillInstanceId):IGameEvent;

public sealed class ChangeParticipantMarkerHandler:AttributedEventHandler {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ChangeParticipantMarker;}
public sealed class ConsumeMarkerPreventDamageHandler:AttributedEventHandler {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ConsumeMarkerPreventDamage;}
public sealed class AddMarkerSubjectNormalDrawHandler:AttributedEventHandler {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.AddMarkerSubjectNormalDraw;}