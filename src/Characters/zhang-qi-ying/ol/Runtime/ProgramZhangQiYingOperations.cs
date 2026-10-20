using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record ZhenyiJudgmentOverride(long ProgramFrameId,long JudgmentId,CardConversionSource Source,string GameplayHash,int CardId,Suit Suit,int Rank);
public sealed partial record JudgmentFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ZhenyiJudgmentOverride? ZhenyiOverride {get;init;}}
public sealed record ZhangQiYingReceipt(CardConversionSource Source,string GameplayHash,long ParentId,int Instruction,string Stage,long Before=0,long After=0)
{
 private readonly IReadOnlyList<int> _cards=[];
 public IReadOnlyList<int> Cards {get=>_cards;init=>_cards=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ZhangQiYingReceipt? ZhangQiYing {get;init;}}
public sealed record FaluTokenChangedEvent(long FrameId,int OwnerSeat,PlayerMarkerKind Marker,int Before,int After,string Reason):IGameEvent;
public sealed record ZhenyiJudgmentOverriddenEvent(ZhenyiJudgmentOverride Receipt):IGameEvent;
public sealed record ZhenyiDamageIncreasedEvent(long FrameId,long WindowId,int SourceSeat,int TargetSeat,int Before,int After):IGameEvent;
public sealed record ZhenyiHoutuPaidEvent(long ActionId,CardConversionSource Source,string GameplayHash,int Turn,int Actor,int Provider,CardActionCost Material):IGameEvent;
public sealed record ZhenyiDeckGainPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,long Before,long After,int ActualCount):IGameEvent;
internal interface IZhangQiYingProgramHost{SkillProgramStepOutcome ExecuteZhangQiYing(ProgramSkillFrame frame,SkillProgramEffectOp op);}
internal sealed class ZhangQiYingHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>host.GetCapability<IZhangQiYingProgramHost>().ExecuteZhangQiYing(f,op);}
internal abstract class ZhangQiYingDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
 public override SkillProgramEffectOp Op=>op;public override ISkillProgramEffectHandler Handler{get;}=new ZhangQiYingHandler(op);
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>op switch
 {
  SkillProgramEffectOp.InitializeFaluSuitTokens=>[new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)],
  SkillProgramEffectOp.GainFaluTokensAfterActualDiscard=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)],
  SkillProgramEffectOp.ReorderTopByCurrentFaluTokens=>[new RequireTriggerWindows([SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,SkillProgramTriggerWindow.TurnEnding])],
  SkillProgramEffectOp.ChooseFinalJudgmentFiveByZiwei=>[new RequireTriggerWindow(SkillProgramTriggerWindow.JudgmentBeforeResult)],
  SkillProgramEffectOp.IncreaseActualSourceDamageByYuqing=>[new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)],
  SkillProgramEffectOp.GainThreeDeckCategoriesAfterElementalDamage=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)],
  _=>throw new InvalidOperationException("Unknown Falu/Zhenyi operation.")
 };
 public override ProgramOperationInteraction Interaction=>op==SkillProgramEffectOp.ChooseFinalJudgmentFiveByZiwei?ProgramOperationInteraction.Choice:ProgramOperationInteraction.Automatic;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,_)=>{});
}
internal static class ZhangQiYingComposition
{
 internal static bool Own(SkillProgramEffectOp op)=>(int)op is >=24400 and <=24405;
 internal static void ValidateActivation(string path,SkillProgramActivation a){if(a.Effects.Any(e=>Own(e.Op)))throw new InvalidOperationException(path+": Falu/Zhenyi operations require their actual trigger windows.");}
 internal static void ValidateTrigger(string path,SkillProgramTrigger t)
 {
  if(!t.Effects.Any(e=>Own(e.Op)))return;
  if(t.Effects is not[var e]||!Own(e.Op)||e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always||t.MarkerCost is not null||t.UsageLimit is not null)throw new InvalidOperationException(path+": Falu/Zhenyi requires one exact native operation with its own once-paid cost.");
  var valid=e.Op switch{
   SkillProgramEffectOp.InitializeFaluSuitTokens=>t.Window==SkillProgramTriggerWindow.GameStarting&&!t.Optional,
   SkillProgramEffectOp.GainFaluTokensAfterActualDiscard=>t.Window==SkillProgramTriggerWindow.CardsMoved&&!t.Optional&&t.MovementDiscardOnly&&t.MovementOccurrence==SkillProgramMovementOccurrence.PerOwnerBatch&&t.SourceZones.SequenceEqual([CardZoneKind.Hand,CardZoneKind.Equipment,CardZoneKind.WoodenOxGrain]),
   SkillProgramEffectOp.ReorderTopByCurrentFaluTokens=>(t.Window is SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or SkillProgramTriggerWindow.TurnEnding)&&t.Optional,
   SkillProgramEffectOp.ChooseFinalJudgmentFiveByZiwei=>t.Window==SkillProgramTriggerWindow.JudgmentBeforeResult&&t.Subject==SkillProgramTriggerSubject.Any&&t.Optional,
   SkillProgramEffectOp.IncreaseActualSourceDamageByYuqing=>t.Window==SkillProgramTriggerWindow.BeforeDamageApplied&&t.Subject==SkillProgramTriggerSubject.DamageSource&&t.Optional,
   SkillProgramEffectOp.GainThreeDeckCategoriesAfterElementalDamage=>t.Window==SkillProgramTriggerWindow.AfterDamageApplied&&t.Subject==SkillProgramTriggerSubject.Owner&&t.Optional,
   _=>false};if(!valid)throw new InvalidOperationException(path+": Falu/Zhenyi operation has an incompatible native window or subject.");
 }
}
