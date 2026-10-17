using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record SpPangTongReceipt(CardConversionSource Source,string GameplayHash,int Instruction,int Turn,string Stage,int Target=-1,int? RevealedCard=null,int? OwnCard=null,CardLocation? OwnFrom=null,int? TargetRank=null,int? OwnRank=null,int? RewardSeat=null,long Before=0,long After=0,int ActualCount=0,bool Wounded=false,int HpBefore=0,int MaximumBefore=0,long? DrawInvocation=null,long? BatchId=null,long? ParentId=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] bool? RuleWounded=null)
{
 private readonly IReadOnlyList<int> _hand=[];
 public IReadOnlyList<int> OriginalHand {get=>_hand;init=>_hand=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public SpPangTongReceipt? SpPangTong {get;init;}}
public sealed record GuolunRevealedEvent(long FrameId,CardConversionSource Source,int TargetSeat,int CardId):IGameEvent;
public sealed record SpPangTongStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Instruction,int Turn,long? ParentId,SkillProgramEffectOp Operation):IGameEvent;
public sealed record SpPangTongSegmentPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Instruction,string Stage,int TargetSeat,long Before,long After,int Count,long? DrawInvocation):IGameEvent;
public sealed record SongsangHealthIssuedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int VictimSeat,bool Wounded,int HpBefore,int MaximumBefore,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] bool? RuleWounded=null):IGameEvent;
public sealed record ZhanjiDrawEntitlement(CardConversionSource Source,string GameplayHash);
public sealed record ZhanjiDrawEntitlementsCapturedEvent(long InvocationId,int OwnerSeat,long SequenceBefore,int Count):IGameEvent;
public sealed record ZhanjiDrawSourceCapturedEvent(long InvocationId,int OwnerSeat,long SequenceBefore,ZhanjiDrawEntitlement Entitlement):IGameEvent;
public sealed partial record NativeDrawInvocationProof
{
 private readonly IReadOnlyList<ZhanjiDrawEntitlement>? _zhanjiEntitlements;
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
 public IReadOnlyList<ZhanjiDrawEntitlement>? ZhanjiEntitlements {get=>_zhanjiEntitlements;init=>_zhanjiEntitlements=value is null?null:Array.AsReadOnly(value.ToArray());}
}
internal interface ISpPangTongProgramHost{SkillProgramStepOutcome ExecuteSpPangTong(ProgramSkillFrame frame,SkillProgramEffectOp op);}
internal sealed class SpPangTongHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>host.GetCapability<ISpPangTongProgramHost>().ExecuteSpPangTong(f,op);}
internal abstract class SpPangTongDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
 public override SkillProgramEffectOp Op=>op;public override ISkillProgramEffectHandler Handler{get;}=new SpPangTongHandler(op);
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override ProgramOperationInteraction Interaction=>op==SkillProgramEffectOp.ResolveGuolunRevealOptionalAtomicExchange?ProgramOperationInteraction.Choice:ProgramOperationInteraction.Automatic;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,_)=>{});
}
internal sealed class ResolveGuolunRevealOptionalAtomicExchangeDescriptor():SpPangTongDescriptor(SkillProgramEffectOp.ResolveGuolunRevealOptionalAtomicExchange)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ReadSelectedTarget(),new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)];}
internal sealed class ResolveSongsangLimitedOtherDeathDescriptor():SpPangTongDescriptor(SkillProgramEffectOp.ResolveSongsangLimitedOtherDeath)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CharacterDied)];}
internal sealed class DrawAfterActualPlayDrawInvocationDescriptor():SpPangTongDescriptor(SkillProgramEffectOp.DrawAfterActualPlayDrawInvocation)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardsGained)];}
internal static class SpPangTongComposition
{
 internal static bool Own(SkillProgramEffectOp op)=>(int)op is >=24700 and <=24702;
 internal static void ValidateActivation(string path,SkillProgramActivation a)
 {
  if(!a.Effects.Any(e=>Own(e.Op)))return;
  if(a.Effects is not[{Op:SkillProgramEffectOp.ResolveGuolunRevealOptionalAtomicExchange,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}]||a.MinCards!=0||a.MaxCards!=0||a.MinTargets!=1||a.MaxTargets!=1||a.TargetKind!=SkillProgramTargetKind.OtherLiving||a.UsesPerPhase!=1||a.UsesPerTurn is not null||a.UsesPerGame is not null||a.MarkerCost is not null)throw new InvalidOperationException(path+": Guolun requires exactly one real other selected participant, no material and one actual Play activation.");
 }
 internal static void ValidateTrigger(string path,SkillProgramTrigger t)
 {
  if(!t.Effects.Any(e=>Own(e.Op)))return;
  if(t.Effects is not[var e]||e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.MarkerCost is not null||t.NamedUsageGroup is not null||t.DynamicUsageLimit is not null)throw new InvalidOperationException(path+": SP Pang Tong needs one exact native operation.");
  var valid=e.Op switch{
   SkillProgramEffectOp.ResolveSongsangLimitedOtherDeath=>t.Window==SkillProgramTriggerWindow.CharacterDied&&t.Subject==SkillProgramTriggerSubject.Owner&&t.Optional&&t.UsageScope==SkillUsageScope.Game&&t.UsageLimit==1,
   SkillProgramEffectOp.DrawAfterActualPlayDrawInvocation=>t.Window==SkillProgramTriggerWindow.CardsGained&&t.Subject==SkillProgramTriggerSubject.Owner&&!t.Optional&&t.UsageLimit is null&&t.UsageScope is null&&t.DestinationZones.SequenceEqual([CardZoneKind.Hand])&&t.MovementOccurrence==SkillProgramMovementOccurrence.PerBatch,
   _=>false};if(!valid)throw new InvalidOperationException(path+": SP Pang Tong trigger has an incompatible native window or cost.");
 }
}
