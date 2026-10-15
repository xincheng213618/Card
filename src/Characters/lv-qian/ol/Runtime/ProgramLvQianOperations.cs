using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record WeiluPhaseKey(int ActualTurn,int OwnerSeat,int PhaseInstance);
public sealed record WeiluFutureSource(CardConversionSource Source,string GameplayHash,int DamageSourceSeat,long DamageFrameId,int ArmedTurn,int? DueOwnTurn=null,WeiluPhaseKey? FirstPhase=null);
public sealed record WeiluPaidDebt(CardConversionSource Source,string GameplayHash,long ProducerFrameId,WeiluPhaseKey Phase,int TargetSeat,int ActualHpLost,bool Consumed=false);
public sealed record ZengdaoPublicGrant(CardConversionSource Source,string GameplayHash,long ProducerFrameId,int RecipientSeat,CardLocation Location,long Before,long After)
{
 private readonly IReadOnlyList<int> _entities=Array.AsReadOnly(Array.Empty<int>());
 public IReadOnlyList<int> Entities{get=>_entities;init=>_entities=Array.AsReadOnly(value.ToArray());}
}
public sealed partial class CharacterState
{
 private IReadOnlyList<WeiluFutureSource> _weiluFuture=Array.AsReadOnly(Array.Empty<WeiluFutureSource>());
 private IReadOnlyList<WeiluPaidDebt> _weiluDebts=Array.AsReadOnly(Array.Empty<WeiluPaidDebt>());
 private IReadOnlyList<ZengdaoPublicGrant> _zengdao=Array.AsReadOnly(Array.Empty<ZengdaoPublicGrant>());
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<WeiluFutureSource>? WeiluFuture{get=>_weiluFuture.Count==0?null:_weiluFuture;internal set=>_weiluFuture=Array.AsReadOnly((value??[]).ToArray());}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<WeiluPaidDebt>? WeiluDebts{get=>_weiluDebts.Count==0?null:_weiluDebts;internal set=>_weiluDebts=Array.AsReadOnly((value??[]).ToArray());}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<ZengdaoPublicGrant>? ZengdaoGrants{get=>_zengdao.Count==0?null:_zengdao;internal set=>_zengdao=Array.AsReadOnly((value??[]).ToArray());}
}
public enum LvQianStage{LoseHpChildren,RecoveryChildren,DepositChildren,DamageChoice,DamagePaymentChildren}
public sealed record WeiluNativeHpProof(int TargetSeat,int Amount,int HpBefore,HpChangeKind Kind);
public sealed record ZengdaoEntityChoice(int CardId,ZengdaoPublicGrant Grant);
public sealed record LvQianReceipt(CardConversionSource Source,string GameplayHash,int Instruction,long WindowId,int ActualTurn,LvQianStage Stage,int Cursor=0,long Before=0,long After=0,int ActualCount=0,WeiluPhaseKey? Phase=null,ZengdaoPublicGrant? Grant=null,long? DamageAttemptResolutionId=null,WeiluNativeHpProof? HpProof=null)
{
 private readonly IReadOnlyList<int> _targets=Array.AsReadOnly(Array.Empty<int>());
 private readonly IReadOnlyList<int> _entities=Array.AsReadOnly(Array.Empty<int>());
 private readonly IReadOnlyList<ZengdaoEntityChoice> _choices=Array.AsReadOnly(Array.Empty<ZengdaoEntityChoice>());
 public IReadOnlyList<ZengdaoEntityChoice> Choices{get=>_choices;init=>_choices=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> Targets{get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> Entities{get=>_entities;init=>_entities=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public LvQianReceipt? LvQian{get;init;}}
public sealed partial record PlayPhaseStartingBoundaryFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public WeiluPhaseKey? WeiluStartingPhase{get;init;}}
public sealed partial record ProgramLifecycleTriggerWindowFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public WeiluPhaseKey? WeiluEndingPhase{get;init;}}
public sealed partial record ProgramSkillWindowContext
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public WeiluPhaseKey? WeiluPhase{get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZengdaoPublicGrant? ZengdaoGrant{get;init;}
}
public sealed record LvQianStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long WindowId,int ActualTurn,LvQianStage Stage):IGameEvent;
public sealed record WeiluFutureArmedEvent(CardConversionSource Source,string GameplayHash,long DamageFrameId,int DamageSourceSeat,int ActualTurn):IGameEvent;
public sealed record WeiluHpPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,WeiluPhaseKey Phase,int TargetSeat,int ActualHpLost):IGameEvent;
public sealed record WeiluDebtConsumedEvent(long FrameId,long ProducerFrameId,WeiluPhaseKey Phase,int TargetSeat,int Amount,bool TargetAlive):IGameEvent;
public sealed record LvQianSegmentPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,LvQianStage Stage,long Before,long After,int Count,int TargetSeat):IGameEvent;
public sealed record ZengdaoDamagePaidEvent(long FrameId,long DamageAttemptResolutionId,CardConversionSource Source,int RecipientSeat,int TargetSeat,int CardId,long Before,long After):IGameEvent;
internal interface ILvQianProgramHost{SkillProgramStepOutcome ExecuteLvQian(ProgramSkillFrame frame,SkillProgramEffectOp op);}
internal abstract class LvQianDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override ISkillProgramEffectHandler Handler=>new LvQianHandler(Op);
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,_)=>{});
}
internal sealed class RecordWeiluActualDamageSourceDescriptor:LvQianDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.RecordWeiluActualDamageSource;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];}
internal sealed class PayWeiluNextActualPlayHpLossDescriptor:LvQianDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.PayWeiluNextActualPlayHpLoss;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];}
internal sealed class RestoreWeiluExactPaidPlayHpDescriptor:LvQianDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.RestoreWeiluExactPaidPlayHp;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)];}
internal sealed class DepositZengdaoActualEquipmentDescriptor:LvQianDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DepositZengdaoActualEquipment;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ReadSelectedTarget(),new ConsumeSelectedCards(0)];}
internal sealed class ConsumeZengdaoForActualSourceDamageDescriptor:LvQianDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ConsumeZengdaoForActualSourceDamage;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];}
internal sealed class LvQianHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<ILvQianProgramHost>().ExecuteLvQian(f,op);}
internal static class LvQianComposition
{
 internal static bool Own(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.RecordWeiluActualDamageSource or SkillProgramEffectOp.PayWeiluNextActualPlayHpLoss or SkillProgramEffectOp.RestoreWeiluExactPaidPlayHp or SkillProgramEffectOp.DepositZengdaoActualEquipment or SkillProgramEffectOp.ConsumeZengdaoForActualSourceDamage;
 internal static void Activation(string path,SkillProgramActivation a)
 {
  if(!a.Effects.Any(e=>Own(e.Op)))return;
  if(a.Effects.Count!=1||a.Effects[0].Op!=SkillProgramEffectOp.DepositZengdaoActualEquipment||a.Effects[0].Target!=SkillProgramEffectTarget.Owner||a.Effects[0].Condition.Kind!=SkillProgramConditionKind.Always||a.UsesPerGame!=1||a.UsesPerTurn is not null||a.UsesPerPhase is not null||a.MinCards<1||a.MaxCards!=int.MaxValue||!a.SourceZones.SequenceEqual([CardZoneKind.Equipment])||a.MinTargets!=1||a.MaxTargets!=1||a.TargetKind!=SkillProgramTargetKind.OtherLiving)throw new InvalidOperationException(path+": Zengdao requires a once-game actual equipment transfer to one other living character.");
 }
 internal static void Trigger(string path,SkillProgramTrigger t)
 {
  if(!t.Effects.Any(e=>Own(e.Op)))return;
  if(t.Effects.Count!=1||t.Effects[0].Target!=SkillProgramEffectTarget.Owner||t.Effects[0].Condition.Kind!=SkillProgramConditionKind.Always||t.Optional||t.OwnerRelation is not null||t.MarkerCost is not null||t.UsageLimit is not null||t.IncludeResponseUses||t.SingleActionInstance||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always)throw new InvalidOperationException(path+": Lv Qian requires one mandatory exact native occurrence.");
  var op=t.Effects[0].Op;var window=op switch{SkillProgramEffectOp.RecordWeiluActualDamageSource=>SkillProgramTriggerWindow.AfterDamageApplied,SkillProgramEffectOp.PayWeiluNextActualPlayHpLoss=>SkillProgramTriggerWindow.PlayPhaseStarting,SkillProgramEffectOp.RestoreWeiluExactPaidPlayHp=>SkillProgramTriggerWindow.PlayEnding,SkillProgramEffectOp.ConsumeZengdaoForActualSourceDamage=>SkillProgramTriggerWindow.BeforeDamageApplied,_=>throw new InvalidOperationException(path+": deposit is an activation.")};
  var subject=op==SkillProgramEffectOp.RecordWeiluActualDamageSource?SkillProgramTriggerSubject.Owner:op==SkillProgramEffectOp.ConsumeZengdaoForActualSourceDamage?SkillProgramTriggerSubject.DamageSource:SkillProgramTriggerSubject.Owner;
  if(t.Window!=window||t.Subject!=subject||op==SkillProgramEffectOp.RecordWeiluActualDamageSource&&t.DamageOccurrence!=SkillProgramDamageOccurrence.PerDamage)throw new InvalidOperationException(path+": Lv Qian cannot change its applied damage, actual phase or real source contract.");
 }
}
