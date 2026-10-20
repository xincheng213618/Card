using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record ZhangJiReceipt
{
 public CardConversionSource Source {get;init;}=null!;
 public string GameplayHash {get;init;}="";
 public int Turn {get;init;}
 public int PhaseInstance {get;init;}
 public int InstructionIndex {get;init;}
 public int Target {get;init;}
 public string Stage {get;init;}="";
 public bool Equipment {get;init;}
 public int HistoricalCount {get;init;}
 public int ChosenRank {get;init;}
 public long? JudgmentId {get;init;}
 public int? JudgmentCard {get;init;}
 public int? FinalRank {get;init;}
 public bool JudgmentReturned {get;init;}
 public long Before {get;init;}
 public long After {get;init;}
 public int? GainCard {get;init;}
 public CardLocation? GainFrom {get;init;}
 public ZhangJiNativeUseOrigin? NativeUse {get;init;}
 public bool NativeReturned {get;init;}
 private IReadOnlyList<EquipmentSlot> _slots=Array.Empty<EquipmentSlot>();
 public IReadOnlyList<EquipmentSlot> Slots {get=>_slots;init=>_slots=Array.AsReadOnly(value.ToArray());}
}
public sealed record ZhangJiNativeUseOrigin(long ProgramFrameId,CardConversionSource Source,string GameplayHash,int Turn,int PhaseInstance,int InstructionIndex,long UseId,long ActionId,int Actor,int CardId,CardKind Kind,EquipmentSlot Slot,long Before);
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ZhangJiReceipt? ZhangJi {get;init;}}
public sealed partial record CardUseFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ZhangJiNativeUseOrigin? ZhangJiNativeOrigin {get;init;}}
public sealed record ZhangJiStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Turn,int PhaseInstance,int Target,bool Equipment,int HistoricalCount):IGameEvent;
public sealed record ZhangJiRankChosenEvent(long FrameId,int Chooser,int Rank,long JudgmentId):IGameEvent;
public sealed record ZhangJiJudgmentReturnedEvent(long FrameId,long JudgmentId,int? CardId,int? Rank):IGameEvent;
public sealed record ZhangJiCardGainedEvent(long FrameId,int FromOwner,int Recipient,long Before,long After):IGameEvent;
public sealed record ZhangJiDamageIssuedEvent(long FrameId,int Source,int Target,int Amount):IGameEvent;
public sealed record ZhangJiNativeUseIssuedEvent(ZhangJiNativeUseOrigin Origin):IGameEvent;
public sealed record ZhangJiNativeUseReturnedEvent(ZhangJiNativeUseOrigin Origin,long After):IGameEvent;
internal interface IZhangJiProgramHost
{SkillProgramStepOutcome BeginZhangJiOperation(ProgramSkillFrame frame,SkillProgramEffectOp op);}
internal sealed class ZhangJiHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
 public SkillProgramEffectOp Op=>op;
 public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>host.GetCapability<IZhangJiProgramHost>().BeginZhangJiOperation(f,op);
}
internal abstract class ZhangJiDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
 public override SkillProgramEffectOp Op=>op;
 public override ISkillProgramEffectHandler Handler{get;}=new ZhangJiHandler(op);
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,_)=>{});
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ReadSelectedTarget(),new RequireSelectedTargetKind(op==SkillProgramEffectOp.ResolveTargetChosenRankJudgment?SkillProgramTargetKind.OtherLiving:SkillProgramTargetKind.AnyLiving)];
}
internal static class ZhangJiComposition
{
 internal static void ValidateActivation(string path,SkillProgramActivation a)
 {
  var ops=a.Effects.Where(e=>(int)e.Op is >=23800 and <=23801).ToArray();if(ops.Length==0)return;
  var equipment=ops[0].Op==SkillProgramEffectOp.UseDistinctDeckEquipmentByLueMingHistory;
  if(ops.Length!=1 || a.Effects.Count!=1 || ops[0].Target!=SkillProgramEffectTarget.Owner || ops[0].Condition.Kind!=SkillProgramConditionKind.Always ||
   a.MinCards!=0 || a.MaxCards!=0 || a.MinTargets!=1 || a.MaxTargets!=1 || a.TargetKind!=(equipment?SkillProgramTargetKind.AnyLiving:SkillProgramTargetKind.OtherLiving) ||
   a.UsesPerTurn is not null || a.MarkerCost is not null || (equipment?(a.UsesPerGame!=1 || a.UsesPerPhase is not null):(a.UsesPerPhase!=1 || a.UsesPerGame is not null)))
   throw new InvalidOperationException(path+": Zhang Ji requires its exact zero-material selected target and phase/limited native operation.");
 }
 internal static void ValidateTrigger(string path,SkillProgramTrigger t)
 {if(t.Effects.Any(e=>(int)e.Op is >=23800 and <=23801))throw new InvalidOperationException(path+": Zhang Ji operations require actual Play activations.");}
}
