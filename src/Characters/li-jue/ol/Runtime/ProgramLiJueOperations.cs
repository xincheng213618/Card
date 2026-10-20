using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum LiJueStage { ChoosingTarget, DamageChildren, MaximumHpChildren, GainChildren }
public sealed record LiJueUseDiscardInvoice(long BatchId,long? NativeParentId,long NativeUseId,int ActualTurn,int ActualTurnOwner,int PhaseInstance,CardActionContext Action,IReadOnlyList<CardMovementRecord> FinishedMovements,bool NullificationUse=false)
{
 private readonly IReadOnlyList<CardMovementRecord> _moves=Array.AsReadOnly(FinishedMovements.ToArray());
 public IReadOnlyList<CardMovementRecord> FinishedMovements {get=>_moves;init=>_moves=Array.AsReadOnly(value.ToArray());}
}
public sealed record LiJueReceipt(CardConversionSource Source,string GameplayHash,int InstructionIndex,int ActualTurn,long WindowId,LiJueStage Stage,int? TargetSeat=null,int? Roll=null,LiJueUseDiscardInvoice? OriginalUse=null,int MaxBefore=0,int MaxAfter=0,int HpBefore=0,int HpAfter=0,long Before=0,long After=0,int ActualGain=0);
public sealed partial record ProgramSkillFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public LiJueReceipt? LiJue {get;init;}
}
internal interface ILiJueProgramHost {SkillProgramStepOutcome BeginLiJue(ProgramSkillFrame frame,SkillProgramEffect effect);}
internal abstract class LiJueDescriptor:ProgramOperationDescriptorBase
{
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,_)=>{});
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
}
internal sealed class ChooseHpBoundOtherAndDealRandomDamageDescriptor:LiJueDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ChooseHpBoundOtherAndDealRandomDamage;
 public override ISkillProgramEffectHandler Handler{get;}=new LiJueHandler(SkillProgramEffectOp.ChooseHpBoundOtherAndDealRandomDamage);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class PayMaxHpAndObtainActualUsedTrickDescriptor:LiJueDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.PayMaxHpAndObtainActualUsedTrick;
 public override ISkillProgramEffectHandler Handler{get;}=new LiJueHandler(SkillProgramEffectOp.PayMaxHpAndObtainActualUsedTrick);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)];
}
internal sealed class LiJueHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
 public SkillProgramEffectOp Op=>op;
 public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int s,ISkillProgramEffectHost h)=>h.GetCapability<ILiJueProgramHost>().BeginLiJue(f,e);
}
internal static class LiJueComposition
{
 internal static bool IsOp(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ChooseHpBoundOtherAndDealRandomDamage or SkillProgramEffectOp.PayMaxHpAndObtainActualUsedTrick;
 internal static void Activation(string path,SkillProgramActivation a){if(a.Effects.Any(e=>IsOp(e.Op)))throw new InvalidOperationException(path+": LiJue requires actual native trigger windows.");}
 internal static void Trigger(string path,SkillProgramTrigger t)
 {
  if(!t.Effects.Any(e=>IsOp(e.Op)))return;
  if(t.Effects is not[var e]||e.Condition.Kind!=SkillProgramConditionKind.Always||!t.Optional||t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own||t.Window!=(e.Op==SkillProgramEffectOp.ChooseHpBoundOtherAndDealRandomDamage?SkillProgramTriggerWindow.TurnStartBeforeNormalFlow:SkillProgramTriggerWindow.CardsMoved))throw new InvalidOperationException(path+": LiJue requires one exact optional own native prepare/use-discard effect.");
  if(e.Op==SkillProgramEffectOp.PayMaxHpAndObtainActualUsedTrick&&(t.UsageScope!=SkillUsageScope.Phase||t.UsageLimit!=1||t.MovementOccurrence!=SkillProgramMovementOccurrence.PerBatch||!t.SourceZones.SequenceEqual([CardZoneKind.Processing])||!t.DestinationZones.SequenceEqual([CardZoneKind.DiscardPile])))throw new InvalidOperationException(path+": Yisuan requires actual per-batch processing/discard movement and one paid use per Play phase.");
 }
}

public sealed record LiJueActualUseOrigin(long NativeUseId,int ActualTurn,int ActualTurnOwner,int PhaseInstance,long MovementBefore,CardActionContext OriginalAction);
public sealed partial record CardUseFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public LiJueActualUseOrigin? LiJueActualUse {get;init;}
}

public sealed partial record CardMovementBatchContext
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public LiJueUseDiscardInvoice? LiJueUseDiscard {get;init;}
}
