using System.Text.Json.Serialization;
namespace CardGame.Core;
internal interface IAnniversaryCaiYangHost { SkillProgramStepOutcome Begin(ProgramSkillFrame f,SkillProgramEffectOp operation); }
internal abstract class AnniversaryCaiYangDescriptor(SkillProgramEffectOp operation):ProgramOperationDescriptorBase
{
 public override SkillProgramEffectOp Op=>operation;
 public override ISkillProgramEffectHandler Handler=>new AnniversaryCaiYangHandler(operation);
 public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)=>ParseOwnerAlways(reader);
 public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
 public override ProgramFactRequirements FactRequirements=>new(Activated:ProgramTrackedFact.NativeActionAppearance,Triggered:ProgramTrackedFact.NativeActionAppearance);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>operation switch{
  SkillProgramEffectOp.ArmAnniversaryCaiYangXunji=>[new ReadSelectedTarget(),new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)],
  SkillProgramEffectOp.ResolveAnniversaryCaiYangXunji=>[new RequireTriggerWindows([SkillProgramTriggerWindow.TurnEnding,SkillProgramTriggerWindow.AfterDamageApplied])],
  _=>[new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)]};
 public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c)=>c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ArmAnniversaryCaiYangXunjiDescriptor():AnniversaryCaiYangDescriptor(SkillProgramEffectOp.ArmAnniversaryCaiYangXunji);
internal sealed class ResolveAnniversaryCaiYangXunjiDescriptor():AnniversaryCaiYangDescriptor(SkillProgramEffectOp.ResolveAnniversaryCaiYangXunji);
internal sealed class ResolveAnniversaryCaiYangJiaofengDescriptor():AnniversaryCaiYangDescriptor(SkillProgramEffectOp.ResolveAnniversaryCaiYangJiaofeng);
internal sealed class AnniversaryCaiYangHandler(SkillProgramEffectOp operation):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>operation;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IAnniversaryCaiYangHost>().Begin(f,operation);}
internal static class AnniversaryCaiYangComposition
{
 internal const string Xunji="anniversary:xunji",Jiaofeng="anniversary:jiaofeng",Arm="designate-next-turn",Ending="next-black-turn-ending-duel",Retaliate="issued-duel-actual-damage",First="first-source-damage";
 internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ArmAnniversaryCaiYangXunji or SkillProgramEffectOp.ResolveAnniversaryCaiYangXunji or SkillProgramEffectOp.ResolveAnniversaryCaiYangJiaofeng;
 [ProgramCatalogValidation(45600)] internal static void Validate(string path,SkillProgram p)
 {
  if(!p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(e=>Handles(e.Op)))return;
  if(p.Id is not(Xunji or Jiaofeng)||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.BooleanStates.Count!=0||p.Contributions.Count!=0||
   p.Triggers.Count!=(p.Id==Xunji?2:1)||p.Triggers.Any(t=>!Exact(p.Id,t))||p.Activations.Count!=(p.Id==Xunji?1:0)||p.Id==Xunji&&p.Activations is not[{Id:Arm,MinCards:0,MaxCards:0,MinTargets:1,MaxTargets:1,TargetKind:SkillProgramTargetKind.OtherLiving,UsesPerTurn:null,UsesPerPhase:1,Effects:[{Op:SkillProgramEffectOp.ArmAnniversaryCaiYangXunji,Target:SkillProgramEffectTarget.Owner,TargetReference:null,Condition.Kind:SkillProgramConditionKind.Always}]}])
   throw new InvalidOperationException(path+": Cai Yang requires his exact once-per-Play designation, issued ending Duel/actual-damage consequence and mandatory first-source-damage window.");
 }
 private static bool Exact(string id,SkillProgramTrigger t)=>!t.Optional&&t.OwnerRelation is null&&t.Priority==0&&t.Condition.Kind==SkillProgramTriggerConditionKind.Always&&t.UsageScope is null&&t.UsageLimit is null&&t.MarkerCost is null&&
  t.CardKinds.Count==0&&t.CardCategories.Count==0&&t.Suits.Count==0&&t.SourceZones.Count==0&&t.DestinationZones.Count==0&&t.SourceSkillId is null&&!t.IncludeResponseUses&&
  t.Effects is[{Target:SkillProgramEffectTarget.Owner,TargetReference:null,Condition.Kind:SkillProgramConditionKind.Always} e]&&
  (id==Jiaofeng&&t.Id==First&&t.Window==SkillProgramTriggerWindow.BeforeDamageApplied&&t.Subject==SkillProgramTriggerSubject.Any&&t.IsSkillInvocation&&t.DamageOccurrence is null&&e.Op==SkillProgramEffectOp.ResolveAnniversaryCaiYangJiaofeng||
   id==Xunji&&!t.IsSkillInvocation&&e.Op==SkillProgramEffectOp.ResolveAnniversaryCaiYangXunji&&
    (t.Id==Ending&&t.Window==SkillProgramTriggerWindow.TurnEnding&&t.Subject==SkillProgramTriggerSubject.Owner&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.OtherLiving&&t.DamageOccurrence is null||
     t.Id==Retaliate&&t.Window==SkillProgramTriggerWindow.AfterDamageApplied&&t.Subject==SkillProgramTriggerSubject.Any&&t.DamageOccurrence==SkillProgramDamageOccurrence.PerDamage));
}
public sealed record CaiYangXunjiGrant(long FrameId,CardConversionSource Source,string Hash,int IssuedTurn,int Target);
public sealed record CaiYangXunjiArmedEvent(CaiYangXunjiGrant Grant):IGameEvent;
public sealed record CaiYangXunjiDueEvent(long GrantFrameId,int ActualTurn,int Target):IGameEvent;
public sealed record CaiYangXunjiConsumedEvent(long GrantFrameId,int ActualTurn,bool Issued):IGameEvent;
public sealed record CaiYangBlackUseEvent(int ActualTurn,int ActorSeat,long ActionId):IGameEvent;
public sealed record CaiYangXunjiOrigin(long FrameId,int Instruction,CardConversionSource Source,string Hash,long ParentId,int ActualTurn,int Target,long GrantId,long UseId,long ActionId);
public sealed record CaiYangXunjiIssuedEvent(CaiYangXunjiOrigin Origin):IGameEvent;
public sealed record CaiYangXunjiReturnedEvent(long FrameId,long UseId,long ActionId):IGameEvent;
public sealed record CaiYangXunjiDamageBoundEvent(long FrameId,long UseId,long DamageId,int SourceSeat,int TargetSeat,int Amount):IGameEvent;
public sealed record CaiYangJiaofengOrigin(long WindowId,long ParentId,long AttackId,CardConversionSource Source,string Hash,int ActualTurn,int Target,int ChainCursor,bool Chain,int OriginalAmount,int LostHp);
public sealed record CaiYangJiaofengWindowIssuedEvent(CaiYangJiaofengOrigin Origin):IGameEvent;
public sealed record CaiYangJiaofengWindowReturnedEvent(long WindowId,long AttackId,int Target,int ChainCursor):IGameEvent;
public sealed record CaiYangJiaofengStartedEvent(long FrameId,CaiYangJiaofengOrigin Origin):IGameEvent;
public sealed record CaiYangJiaofengDrawReturnedEvent(long FrameId,int Count,long? AttemptId,long Before,long After):IGameEvent;
public sealed record CaiYangJiaofengRecoveryIssuedEvent(long FrameId,int Requested):IGameEvent;
public sealed record CaiYangCompletedEvent(long FrameId):IGameEvent;
public enum AnniversaryCaiYangStage{Duel,Damage,DamageReturned,Drawing,DrawChildren,RecoveryChildren}
public sealed record AnniversaryCaiYangReceipt(CardConversionSource Source,string Hash,long ParentId,int ActualTurn,AnniversaryCaiYangStage Stage,
 CaiYangXunjiOrigin? Duel=null,CaiYangJiaofengOrigin? FirstDamage=null,long DamageId=0,int DamageTarget=-1,int DamageAmount=0,NativeDrawOutcome? Draw=null,bool RecoveryIssued=false);
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public AnniversaryCaiYangReceipt? AnniversaryCaiYang{get;init;}}
public sealed partial record BeforeDamageProgramWindowFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public CaiYangJiaofengOrigin? CaiYangJiaofeng{get;init;}}
public sealed partial record ProgramSelectedActorDuelOrigin
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public CaiYangXunjiOrigin? CaiYangXunji{get;init;}}
