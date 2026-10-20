using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum WangCanStage { SanwenDiscard, SanwenDraw, QiaiChoose, QiaiGift, DenglouViewChildren, DenglouView, DenglouGain, DenglouChoose, DenglouNative, DenglouDiscard, DenglouCancel, Completed }
public sealed record WangCanReceipt(CardConversionSource Source,string GameplayHash,int Instruction,long WindowId,int ActualTurn,int Phase,WangCanStage Stage,SanwenGainProof? Sanwen=null,int Cursor=0,int ActualCount=0,long Before=0,long After=0,CardLocation? Lease=null,WangCanNativeReceipt? Native=null,long? DyingFrameId=null,long LeaseBefore=0,long LeaseAfter=0)
{
 private readonly IReadOnlyList<int> _entities=Array.AsReadOnly(Array.Empty<int>()),_remaining=Array.AsReadOnly(Array.Empty<int>()),_seats=Array.AsReadOnly(Array.Empty<int>());
 public IReadOnlyList<int> Entities{get=>_entities;init=>_entities=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> Remaining{get=>_remaining;init=>_remaining=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> Seats{get=>_seats;init=>_seats=Array.AsReadOnly(value.ToArray());}
}
public sealed record WangCanNativeReceipt(long ProgramFrameId,CardConversionSource Source,string GameplayHash,int Instruction,int ActualTurn,int Phase,long WindowId,int CardId,CardLocation From,CardKind EffectiveKind,CardConversionSource? Conversion,long UseId,long ActionId,long? ParentActionId)
{
 private readonly IReadOnlyList<int> _targets=Array.AsReadOnly(Array.Empty<int>());
 public IReadOnlyList<int> Targets{get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
}
public sealed record WangCanStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long WindowId,int ActualTurn,SkillProgramEffectOp Operation):IGameEvent;
public sealed record WangCanSegmentPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,WangCanStage Stage,int ActualTurn,int SubjectSeat,long Before,long After,int ActualCount):IGameEvent;
public sealed record WangCanNativeIssuedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long UseId,long ActionId,int ActorSeat,CardKind EffectiveKind):IGameEvent;
public sealed record WangCanNativeReturnedEvent(long FrameId,long UseId,long ActionId):IGameEvent;
public sealed record WangCanCompletedEvent(long FrameId,CardConversionSource Source,string GameplayHash):IGameEvent;
public sealed partial record CardMovementBatchContext
{
 private readonly IReadOnlyList<SanwenGainProof>? _sanwenGains;
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public IReadOnlyList<SanwenGainProof>? SanwenGains{get=>_sanwenGains;init=>_sanwenGains=value is null?null:Array.AsReadOnly(value.Select(q=>q with{Incoming=q.Incoming,Witnesses=q.Witnesses}).ToArray());}
}
public sealed partial record ProgramSkillWindowContext
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public SanwenGainProof? Sanwen{get;init;}}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public WangCanReceipt? WangCan{get;init;}}
public sealed partial record CardUseFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public WangCanNativeReceipt? DenglouNative{get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public long? DenglouCostBefore{get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public long? DenglouCostAfter{get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public bool? DenglouCostDrained{get;init;}
}
internal interface IWangCanProgramHost{SkillProgramStepOutcome ExecuteWangCan(SkillProgramEffectOp op,ProgramSkillFrame f);}
internal abstract class WangCanDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override ISkillProgramEffectHandler Handler=>new WangCanHandler(Op);
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>c.Draw(new(SkillProgramEffectOp.Draw,SkillProgramEffectTarget.Owner,1,e.Condition)));
}
internal sealed class WangCanHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int t,ISkillProgramEffectHost h)=>h.GetCapability<IWangCanProgramHost>().ExecuteWangCan(op,f);}
internal static class WangCanContract
{
 internal static bool Own(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveSanwenActualGainDuplicates or SkillProgramEffectOp.CollectQiaiLivingContributions or SkillProgramEffectOp.ViewDenglouTopAndUseBasics;
 internal static void Activation(string path,SkillProgramActivation a){if(a.Effects.Any(e=>Own(e.Op)))throw new InvalidOperationException(path+": Wang Can requires native gain, dying or own ending windows.");}
 internal static void Trigger(string path,SkillProgramTrigger t)
 {
  if(!t.Effects.Any(e=>Own(e.Op)))return;
  if(t.Effects.Count!=1||t.Effects[0].Condition.Kind!=SkillProgramConditionKind.Always||t.Effects[0].Target!=SkillProgramEffectTarget.Owner||!t.Optional||t.Subject!=SkillProgramTriggerSubject.Owner||t.OwnerRelation is not null||t.MarkerCost is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.IncludeResponseUses||t.SingleActionInstance||t.SourceSkillId is not null||t.SourceViewAsId is not null||t.DynamicUsageLimit is not null||t.NamedUsageGroup is not null||t.UsageLimit!=1)throw new InvalidOperationException(path+": Wang Can requires one exact optional native owner occurrence and one fixed printed quota.");
  var op=t.Effects[0].Op;var gain=op==SkillProgramEffectOp.ResolveSanwenActualGainDuplicates;
  if(t.Window!=(gain?SkillProgramTriggerWindow.CardsGained:op==SkillProgramEffectOp.CollectQiaiLivingContributions?SkillProgramTriggerWindow.DyingEntering:SkillProgramTriggerWindow.TurnEnding)||t.UsageScope!=(gain?SkillUsageScope.Turn:SkillUsageScope.Game)||gain&&(t.MovementOccurrence!=SkillProgramMovementOccurrence.PerBatch||!t.DestinationZones.SequenceEqual([CardZoneKind.Hand])))throw new InvalidOperationException(path+": Wang Can cannot change its actual gain batch, limited dying or limited ending contract.");
 }
}
