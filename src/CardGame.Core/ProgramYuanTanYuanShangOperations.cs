using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum NeifaStage { Branch, Field, GainChildren, Discard, DiscardChildren, Targets, EquipmentDrawChildren }
public sealed record NeifaTurnState(int ActualTurn,bool BasicBranch,bool NonBasicBranch,int EquipmentUses,CardConversionSource Source,string GameplayHash,long ArmFrameId);
public sealed record NeifaEquipmentUseProof(long UseId,CardActionContext Action,int ActualTurn,int Ordinal,int RequestedDraw,CardConversionSource Source,string GameplayHash);
public sealed record NeifaPaidSegment(string Name,long Before,long After,int Requested,int Actual,long? BatchId,int? CardId=null,CardLocation? From=null);
public sealed record NeifaReceipt(CardConversionSource Source,string GameplayHash,int InstructionIndex,int ActualTurn,int PhaseInstance,long WindowId,NeifaStage Stage,string? Branch=null,int? CardId=null,CardLocation? From=null,long Before=0,long After=0,int Requested=0,int Actual=0,long? BatchId=null,SkillProgramCardCategory? DiscardCategory=null,NeifaEquipmentUseProof? Equipment=null,long? UseId=null,long? ActionId=null)
{
 private readonly IReadOnlyList<NeifaPaidSegment> _segments=Array.AsReadOnly(Array.Empty<NeifaPaidSegment>());
 public IReadOnlyList<NeifaPaidSegment> Segments {get=>_segments;init=>_segments=Array.AsReadOnly(value.ToArray());}
 private readonly IReadOnlyList<int> _original=Array.AsReadOnly(Array.Empty<int>());
 public IReadOnlyList<int> OriginalTargets {get=>_original;init=>_original=Array.AsReadOnly(value.ToArray());}
}
public sealed record NeifaSegmentPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,long WindowId,string Segment,long Before,long After,int Requested,int Actual,long? BatchId):IGameEvent;
public sealed record NeifaTurnArmedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,SkillProgramCardCategory DiscardCategory,bool BasicBranch,bool NonBasicBranch):IGameEvent;
public sealed record NeifaEquipmentUseCountedEvent(long UseId,long ActionId,int Actor,int ActualTurn,int Ordinal,CardConversionSource Source,string GameplayHash):IGameEvent;
public sealed record NeifaEquipmentUseQualifiedEvent(long UseId,long ActionId,int Actor,int ActualTurn,int Ordinal,int RequestedDraw,CardConversionSource Source,string GameplayHash):IGameEvent;
public sealed record NeifaTargetsResolvedEvent(long FrameId,long UseId,long ActionId,CardConversionSource Source,string GameplayHash,string Branch):IGameEvent
{
 private readonly IReadOnlyList<int> _before=Array.AsReadOnly(Array.Empty<int>()),_after=Array.AsReadOnly(Array.Empty<int>());
 public IReadOnlyList<int> OriginalTargets {get=>_before;init=>_before=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> ResultTargets {get=>_after;init=>_after=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public NeifaReceipt? Neifa {get;init;}
}
public sealed partial record CardUseFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public NeifaEquipmentUseProof? NeifaEquipment {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public CardUsePaymentMovementReceipt? NeifaMaterialPayment {get;init;}
}
public sealed partial class CharacterState
{
 internal NeifaTurnState? NeifaTurnState {get;set;}
}
internal interface INeifaProgramHost {SkillProgramStepOutcome BeginNeifa(ProgramSkillFrame frame,SkillProgramEffect effect);}
internal abstract class NeifaDescriptor:ProgramOperationDescriptorBase
{
 public override ISkillProgramEffectHandler Handler=>new NeifaHandler(Op);
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,_)=>{});
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
}
internal sealed class GainThenDiscardAndArmNeifaActualTurnDescriptor:NeifaDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.GainThenDiscardAndArmNeifaActualTurn;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}
internal sealed class AdjustNeifaOrdinaryTrickTargetsDescriptor:NeifaDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.AdjustNeifaOrdinaryTrickTargets;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
internal sealed class DrawForFirstTwoPaidNeifaEquipmentUsesDescriptor:NeifaDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DrawForFirstTwoPaidNeifaEquipmentUses;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
internal sealed class NeifaHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
 public SkillProgramEffectOp Op=>op;
 public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>((INeifaProgramHost)host).BeginNeifa(f,e);
}
internal static class NeifaComposition
{
 internal static bool IsOp(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.GainThenDiscardAndArmNeifaActualTurn or SkillProgramEffectOp.AdjustNeifaOrdinaryTrickTargets or SkillProgramEffectOp.DrawForFirstTwoPaidNeifaEquipmentUses;
 internal static void Activation(string path,SkillProgramActivation a){if(a.Effects.Any(e=>IsOp(e.Op)))throw new InvalidOperationException(path+": Neifa requires exact native trigger windows.");}
 internal static void Trigger(string path,SkillProgramTrigger t)
 {
  if(!t.Effects.Any(e=>IsOp(e.Op)))return;
  if(t.Effects is not[var e]||e.Condition.Kind!=SkillProgramConditionKind.Always)throw new InvalidOperationException(path+": Neifa requires one unconditional native instruction.");
  if(e.Op==SkillProgramEffectOp.GainThenDiscardAndArmNeifaActualTurn)
  {if(t.Window!=SkillProgramTriggerWindow.PlayPhaseStarting||t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own||!t.Optional)throw new InvalidOperationException(path+": Neifa activation requires optional actual own Play start.");}
  else if(t.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor||t.Optional)throw new InvalidOperationException(path+": issued Neifa rights require mandatory actual actor finalized use.");
 }
}
