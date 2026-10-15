using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum GuoSiStage { TargetChoice, MovementChildren, SidaoChoice, NativeUse, Completed }
public sealed record GuoSiUseStamp(int Turn,int Phase,long ActionId,long NativeUseId,int ActorSeat, bool Finalized,bool Completed,IReadOnlyList<int> Targets)
{
 private readonly IReadOnlyList<int> _targets=Array.AsReadOnly(Targets.ToArray());
 public IReadOnlyList<int> Targets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
}
public sealed record GuoSiPair(GuoSiUseStamp First,GuoSiUseStamp Second,IReadOnlyList<int> CommonTargets)
{
 private readonly IReadOnlyList<int> _targets=Array.AsReadOnly(CommonTargets.ToArray());
 public IReadOnlyList<int> CommonTargets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
}
public sealed record GuoSiTurnPact(long FrameId,CardConversionSource Source,string GameplayHash,int Turn,int TurnOwner,int Phase,int Actor,int Target,int Branch);
public sealed record GuoSiReceipt(CardConversionSource Source,string GameplayHash,int Instruction,int Turn,int Phase,int Target,GuoSiStage Stage,GuoSiPair? Pair=null,int Branch=-1,long Before=0,long After=0,int? SelectedId=null,CardLocation? SelectedFrom=null,uint RandomBefore=0,uint RandomAfter=0,GuoSiNativeReceipt? Native=null);
public sealed record GuoSiNativeReceipt(long ProgramFrameId,CardConversionSource Source,string GameplayHash,int Turn,int Phase,int Instruction,long NativeUseId,long ActionId,long ParentActionId,int Material,int Target,LegalActionKind ActionKind,int? TargetCardId,CardKind? RequiredKind);
public sealed record GuoSiPactPaidEvent(GuoSiTurnPact Pact,long Before,long After,int ActualCount,uint RandomBefore,uint RandomAfter):IGameEvent;
public sealed record GuoSiNativeIssuedEvent(GuoSiNativeReceipt Receipt):IGameEvent;
public sealed record GuoSiNativeReturnedEvent(GuoSiNativeReceipt Receipt):IGameEvent;
public sealed record GuoSiPairQualifiedEvent(long NativeUseId,int OwnerSeat,long FirstActionId,long SecondActionId):IGameEvent;
public sealed partial class CharacterState
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<GuoSiTurnPact>? GuoSiPacts {get;set;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<GuoSiUseStamp>? GuoSiUses {get;set;}
}
public sealed partial record ProgramSkillFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public GuoSiReceipt? GuoSi {get;init;}
}
public sealed partial record CardUseFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public GuoSiPair? GuoSiPair {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public GuoSiNativeReceipt? GuoSiNative {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public long? GuoSiCostBefore {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public long? GuoSiCostAfter {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public bool? GuoSiCostDrained {get;init;}
}
internal interface IGuoSiProgramHost {SkillProgramStepOutcome ExecuteGuoSi(SkillProgramEffectOp op,ProgramSkillFrame f);}
internal abstract class GuoSiDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override ISkillProgramEffectHandler Handler=>new GuoSiHandler(Op);
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>c.Draw(new(SkillProgramEffectOp.Draw,SkillProgramEffectTarget.Owner,1,e.Condition)));
}
internal sealed class ChooseTanbeiTargetPactDescriptor:GuoSiDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ChooseTanbeiTargetPact;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ReadSelectedTarget()];
}
internal sealed class OfferSidaoCompletedPairSnatchDescriptor:GuoSiDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.OfferSidaoCompletedPairSnatch;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)];
}
internal sealed class GuoSiHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
 public SkillProgramEffectOp Op=>op;
 public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IGuoSiProgramHost>().ExecuteGuoSi(op,f);
}
internal static class GuoSiContract
{
 internal static bool Own(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ChooseTanbeiTargetPact or SkillProgramEffectOp.OfferSidaoCompletedPairSnatch;
 internal static void Activation(string path,SkillProgramActivation a)
 {
  if(!a.Effects.Any(e=>Own(e.Op)))return;
  if(a.Effects is not[{Op:SkillProgramEffectOp.ChooseTanbeiTargetPact,Condition.Kind:SkillProgramConditionKind.Always}]||a.MinCards!=0||a.MaxCards!=0||a.MinTargets!=1||a.MaxTargets!=1||a.TargetKind!=SkillProgramTargetKind.OtherLiving||a.UsesPerPhase!=1||a.UsesPerTurn is not null||a.MarkerCost is not null||a.UsesPerGame is not null||a.Condition.Kind!=SkillProgramConditionKind.Always)throw new InvalidOperationException(path+": Tanbei requires one actual Play activation, no material, and exactly one other target.");
 }
 internal static void Trigger(string path,SkillProgramTrigger t)
 {
  if(!t.Effects.Any(e=>Own(e.Op)))return;
  if(t.Effects is not[{Op:SkillProgramEffectOp.OfferSidaoCompletedPairSnatch,Condition.Kind:SkillProgramConditionKind.Always}]||t.Window!=SkillProgramTriggerWindow.CardUseCompleted||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor||!t.Optional||!t.IncludeResponseUses||t.UsageScope!=SkillUsageScope.Phase||t.UsageLimit!=1||t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.MarkerCost is not null||t.Subject is not null||t.SourceSkillId is not null||t.SourceViewAsId is not null||t.DynamicUsageLimit is not null||t.NamedUsageGroup is not null||t.ChoiceGroup is not null||t.SingleActionInstance||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.EvaluateConditionAtResolution||t.Suits.Count!=0||t.SourceZones.Count!=0||t.DamageOccurrence is not null||t.MovementOccurrence is not null||t.OnlyDesignatedCardTargets||t.AllowNoEventTarget)throw new InvalidOperationException(path+": Sidao requires an optional actor completed Use and one actual Play quota.");
 }
}
