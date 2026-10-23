using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryYanXiangHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversaryYanXiangDescriptor : ProgramOperationDescriptorBase
{
 public override ISkillProgramEffectHandler Handler => new AnniversaryYanXiangHandler(Op);
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
 public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzhengReward ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy => new(ProgramOperationAiSemantic.ChooseOption,static(_,c)=>c.PublicControlValue(3));
 public override ProgramFactRequirements FactRequirements => new(Triggered:ProgramTrackedFact.NativeLogicalDiscard|ProgramTrackedFact.WholeActualUseDeclaration|ProgramTrackedFact.NativeActionAppearance,Activated:ProgramTrackedFact.NativeLogicalDiscard|ProgramTrackedFact.WholeActualUseDeclaration|ProgramTrackedFact.ActualDiscardRecoveryPhase|ProgramTrackedFact.NativeActionAppearance);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => Op switch {
  SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzheng => [new ReadSelectedTarget()],
  SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzhengReward => [new RequireTriggerWindows([SkillProgramTriggerWindow.CardUseCompleted,SkillProgramTriggerWindow.CardResponseCompleted])],
  _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)]};
}
internal sealed class ResolveAnniversaryYanXiangYuzhengDescriptor:AnniversaryYanXiangDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzheng;}
internal sealed class ResolveAnniversaryYanXiangYuzhengRewardDescriptor:AnniversaryYanXiangDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzhengReward;}
internal sealed class ResolveAnniversaryYanXiangSuishiDescriptor:AnniversaryYanXiangDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveAnniversaryYanXiangSuishi;}
internal sealed class AnniversaryYanXiangHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>h.GetCapability<IAnniversaryYanXiangHost>().Begin(f,op);}
internal static class AnniversaryYanXiangComposition
{
 internal const string Yuzheng="anniversary:yuzheng",Suishi="anniversary:suishi";
 [ProgramCatalogValidation(46800)]internal static void Validate(string path,SkillProgram p)
 {
  if(!p.Activations.SelectMany(a=>a.Effects).Concat(p.Triggers.SelectMany(t=>t.Effects)).Any(e=>e.Op is SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzheng or SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzhengReward or SkillProgramEffectOp.ResolveAnniversaryYanXiangSuishi))return;
  if(p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.BooleanStates.Count!=0||p.Contributions.Count!=0)throw new InvalidOperationException(path+": Yan Xiang's declared native operations cannot acquire unrelated static policies.");
  if(p.Id==Yuzheng&&p.Activations is[{Id:"admonish",MinCards:0,MaxCards:0,MinTargets:1,MaxTargets:1,TargetKind:SkillProgramTargetKind.AnyLiving,UsesPerTurn:null,UsesPerPhase:null,Effects:[{Op:SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzheng,Target:SkillProgramEffectTarget.Owner}]}]&&p.Triggers.Count==2&&p.Triggers.All(t=>!t.Optional&&!t.IsSkillInvocation&&t.OwnerRelation==SkillProgramCardActionOwnerRelation.Observer&&t.Effects is[{Op:SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzhengReward,Target:SkillProgramEffectTarget.Owner}]&&
   (t.Id=="issued-use-reward"&&t.Window==SkillProgramTriggerWindow.CardUseCompleted&&t.IncludeResponseUses||t.Id=="issued-response-reward"&&t.Window==SkillProgramTriggerWindow.CardResponseCompleted&&!t.IncludeResponseUses)))return;
  if(p.Id==Suishi&&p.Activations.Count==0&&p.Triggers.Count==2&&p.Triggers.All(t=>t.Optional&&t.IsSkillInvocation&&t.Window==SkillProgramTriggerWindow.TurnEnding&&t.Subject==SkillProgramTriggerSubject.Owner&&t.Effects is[{Op:SkillProgramEffectOp.ResolveAnniversaryYanXiangSuishi,Target:SkillProgramEffectTarget.Owner}]&&
   (t.Id=="own-ending-damage-name"&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.Own||t.Id=="other-ending-damage-name"&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.OtherLiving)))return;
  throw new InvalidOperationException(path+": Yan Xiang requires the exact one-recipient activation, issued completion rewards and actual own/other Ending declarations.");
 }
}
public enum AnniversaryYanXiangStage { Option, HandCards, Drawing, MovementChildren, DamageName, UseChoice, IssuingUse, NativeUse, NativeTail, Completed }
public sealed record AnniversaryYanXiangPlan(int CardId,CardLocation From,CardKind Kind,LegalActionKind ActionKind,int? TargetCardId=null,CardKind? RequiredKind=null,bool Adjusted=false)
{
 private IReadOnlyList<int> _targets=Array.AsReadOnly(Array.Empty<int>());
 public IReadOnlyList<int> Targets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
}
public sealed record AnniversaryYanXiangReceipt(CardConversionSource Source,string Hash,SkillProgramEffectOp Operation,long ParentId,int Turn,int Round,int Phase,int Actor,AnniversaryYanXiangStage Stage)
{
 private IReadOnlyList<int> _cards=Array.AsReadOnly(Array.Empty<int>());
 private IReadOnlyList<long> _leases=Array.AsReadOnly(Array.Empty<long>()),_batches=Array.AsReadOnly(Array.Empty<long>());
 private IReadOnlyList<CardMovementRecord> _moves=Array.AsReadOnly(Array.Empty<CardMovementRecord>());
 public IReadOnlyList<int> Cards {get=>_cards;init=>_cards=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<long> RewardLeases {get=>_leases;init=>_leases=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<long> NativeBatches {get=>_batches;init=>_batches=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<CardMovementRecord> Moves {get=>_moves;init=>_moves=Array.AsReadOnly(value.ToArray());}
 public int Option {get;init;}
 public int Required {get;init;}
 public int Requested {get;init;}
 public int Cursor {get;init;}
 public long RewardAction {get;init;}
 public NativeDrawOutcome? Draw {get;init;}
 public CardKind? Name {get;init;}
 public AnniversaryYanXiangPlan? Plan {get;init;}
 public long Before {get;init;}
 public long After {get;init;}
 public long UseId {get;init;}
 public long ActionId {get;init;}
 public bool Returned {get;init;}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public AnniversaryYanXiangReceipt? AnniversaryYanXiang {get;init;}}
public sealed record AnniversaryYanXiangUseOrigin(long FrameId,CardConversionSource Source,string Hash,int Actor,int CardId,CardLocation From,CardKind Kind,long UseId,long ActionId);
public sealed partial record CardUseFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public AnniversaryYanXiangUseOrigin? AnniversaryYanXiangUse {get;init;}}
public sealed record AnniversaryYanXiangStartedEvent(long FrameId,CardConversionSource Source,string Hash,SkillProgramEffectOp Operation,long ParentId,int Turn,int Round,int Phase,int Actor):IGameEvent;
public sealed record AnniversaryYanXiangOptionChosenEvent(long FrameId,CardConversionSource Source,string Hash,int Turn,int Phase,int Actor,int Option):IGameEvent;
public sealed record AnniversaryYanXiangPaidEvent(long FrameId,int Actor,int Count,long Before,long After):IGameEvent;
public sealed record AnniversaryYanXiangDrawReturnedEvent(long FrameId,int Cursor,int Actor,int Requested,int Actual,long Before,long After):IGameEvent;
public sealed record AnniversaryYanXiangLeaseEvent(long Id,CardConversionSource Source,string Hash,int Round,int Actor,int Option,int Amount,int UseCutoff,long ActionCutoff):IGameEvent;
public sealed record AnniversaryYanXiangRewardRequestedEvent(long FrameId,long LeaseId,long ActionId):IGameEvent;
public sealed record AnniversaryYanXiangNameDeclaredEvent(long FrameId,int Actor,CardKind Name):IGameEvent;
public sealed record AnniversaryYanXiangUseIssuedEvent(long FrameId,long UseId,long ActionId,int Actor,int CardId,CardKind Kind):IGameEvent;
public sealed record AnniversaryYanXiangUseReturnedEvent(long FrameId,long UseId,long ActionId):IGameEvent;
public sealed record AnniversaryYanXiangNativeBatchEvent(long FrameId,long UseId,long BatchId):IGameEvent;
public sealed record AnniversaryYanXiangColorBannedEvent(long FrameId,long UseId,long ActionId,long DamageId,int Victim,bool IsRed,int OwnReturns):IGameEvent;
public sealed record AnniversaryYanXiangCompletedEvent(long FrameId):IGameEvent;
