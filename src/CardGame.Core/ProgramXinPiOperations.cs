using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record YinjuTurnRelation(CardConversionSource Source,string GameplayHash,int ActualTurn,int TargetSeat,long ActivationFrameId);
public sealed record XinPiMaterialInvoice(int CardId,CardMovementRecord Payment,long? PaymentBatchId,CardMovementRecord? Finished=null,long? FinishedBatchId=null);
public sealed record XinPiUseOrigin(long UseId,CardActionContext Action,long Before)
{
 private readonly IReadOnlyList<XinPiMaterialInvoice> _materials=Array.AsReadOnly(Array.Empty<XinPiMaterialInvoice>());
 public IReadOnlyList<XinPiMaterialInvoice> Materials{get=>_materials;init=>_materials=Array.AsReadOnly(value.ToArray());}
}
public sealed record XinPiClaimProof(long FrameId,long UseId,long ActionId,CardConversionSource Source,string GameplayHash,long Before,long? After=null,long? BatchId=null)
{
 private readonly IReadOnlyList<int> _ids=Array.AsReadOnly(Array.Empty<int>());
 public IReadOnlyList<int> Entities{get=>_ids;init=>_ids=Array.AsReadOnly(value.ToArray());}
}
public sealed record ChijieClaimIssuedEvent(long FrameId,long UseId,long ActionId,CardConversionSource Source,string GameplayHash,long Before,int Count):IGameEvent;
public sealed record ChijieInvalidationProof(long ProgramFrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,long UseId,long ActionId,long DamageFrameId,int TargetSeat,int SourceSeat);
public enum XinPiStage { ClaimChildren, RecoveryChildren, OwnerDrawChildren, RecipientDrawChildren }
public sealed record XinPiReceipt(CardConversionSource Source,string GameplayHash,int InstructionIndex,int ActualTurn,long WindowId,XinPiStage Stage,long? UseId=null,long? ActionId=null,long? DamageAttemptId=null,int? TargetSeat=null,int Requested=0,long Before=0,long After=0,int Actual=0,long? BatchId=null,YinjuTurnRelation? Relation=null)
{
 private readonly IReadOnlyList<int> _ids=Array.AsReadOnly(Array.Empty<int>());
 public IReadOnlyList<int> Entities {get=>_ids;init=>_ids=Array.AsReadOnly(value.ToArray());}
 private readonly IReadOnlyList<CardLocation> _from=Array.AsReadOnly(Array.Empty<CardLocation>());
 public IReadOnlyList<CardLocation> From {get=>_from;init=>_from=Array.AsReadOnly(value.ToArray());}
}
public sealed record ChijieUsedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,string Branch,long UseId,long ActionId):IGameEvent;
public sealed record ChijieUseInvalidatedEvent(ChijieInvalidationProof Proof):IGameEvent;
public sealed record ChijieClaimPaidEvent(long FrameId,CardConversionSource Source,long UseId,long ActionId,long Before,long After,long BatchId,int Count):IGameEvent;
public sealed record YinjuRelationArmedEvent(YinjuTurnRelation Relation):IGameEvent;
public sealed record YinjuRecoveryPaidEvent(long FrameId,YinjuTurnRelation Relation,long WindowId,long DamageAttemptId,int SourceSeat,int TargetSeat,int Prevented,int RequestedRecovery,int HpBefore):IGameEvent;
public sealed record YinjuUseDrawStartedEvent(long FrameId,YinjuTurnRelation Relation,long UseId,long ActionId,long WindowId):IGameEvent;
public sealed record YinjuDrawPaidEvent(long FrameId,YinjuTurnRelation Relation,long UseId,long ActionId,int SubjectSeat,long Before,long After,int Actual):IGameEvent;
public sealed partial class CharacterState
{
 internal int? ChijieUsedActualTurn {get;set;}
 internal YinjuTurnRelation? YinjuRelation {get;set;}
}
public sealed partial record CardUseFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public XinPiUseOrigin? XinPiOrigin {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ChijieInvalidationProof? ChijieInvalidation {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public XinPiClaimProof? XinPiClaim {get;init;}
}
public sealed partial record ProgramSkillFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public XinPiReceipt? XinPi {get;init;}
}
internal interface IXinPiProgramHost{SkillProgramStepOutcome BeginXinPi(ProgramSkillFrame f,SkillProgramEffect e);}
internal abstract class XinPiDescriptor:ProgramOperationDescriptorBase
{
 public override ISkillProgramEffectHandler Handler=>new XinPiHandler(Op);
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Automatic;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,_)=>{});
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>Op switch
 {
  SkillProgramEffectOp.InvalidateRemainingActualDamageCardEffects=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)],
  SkillProgramEffectOp.ClaimCompletedUndamagedIncomingCardEntities=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)],
  SkillProgramEffectOp.ArmYinjuActualTurnRelation=>[new ReadSelectedTarget()],
  SkillProgramEffectOp.PreventYinjuDamageAndRecover=>[new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)],
  SkillProgramEffectOp.DrawBothAfterYinjuActualTargetUse=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)],
  _=>throw new InvalidOperationException("Unknown XinPi operation.")
 };
}
internal sealed class InvalidateRemainingActualDamageCardEffectsDescriptor:XinPiDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.InvalidateRemainingActualDamageCardEffects;}
internal sealed class ClaimCompletedUndamagedIncomingCardEntitiesDescriptor:XinPiDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ClaimCompletedUndamagedIncomingCardEntities;}
internal sealed class ArmYinjuActualTurnRelationDescriptor:XinPiDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ArmYinjuActualTurnRelation;}
internal sealed class PreventYinjuDamageAndRecoverDescriptor:XinPiDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.PreventYinjuDamageAndRecover;}
internal sealed class DrawBothAfterYinjuActualTargetUseDescriptor:XinPiDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DrawBothAfterYinjuActualTargetUse;}
internal sealed class XinPiHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
 public SkillProgramEffectOp Op=>op;
 public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>((IXinPiProgramHost)h).BeginXinPi(f,e);
}
internal static class XinPiComposition
{
 internal static bool IsOp(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.InvalidateRemainingActualDamageCardEffects or SkillProgramEffectOp.ClaimCompletedUndamagedIncomingCardEntities or SkillProgramEffectOp.ArmYinjuActualTurnRelation or SkillProgramEffectOp.PreventYinjuDamageAndRecover or SkillProgramEffectOp.DrawBothAfterYinjuActualTargetUse;
 internal static void Activation(string path,SkillProgramActivation a)
 {
  if(!a.Effects.Any(e=>IsOp(e.Op)))return;
  if(a.MinCards!=0||a.MaxCards!=0||a.MinTargets!=1||a.MaxTargets!=1||a.TargetKind!=SkillProgramTargetKind.OtherLiving||a.UsesPerGame!=1||a.UsesPerTurn is not null||a.Effects is not[{Op:SkillProgramEffectOp.ArmYinjuActualTurnRelation,Condition.Kind:SkillProgramConditionKind.Always}])throw new InvalidOperationException(path+": Yinju requires exactly one actual other participant, no cards, and one game activation.");
 }
 internal static void Trigger(string path,SkillProgramTrigger t)
 {
  if(!t.Effects.Any(e=>IsOp(e.Op)))return;
  if(t.Effects is not[{Op:var op,Condition.Kind:SkillProgramConditionKind.Always}]||op==SkillProgramEffectOp.ArmYinjuActualTurnRelation)throw new InvalidOperationException(path+": XinPi trigger requires one exact native instruction.");
  var expected=op switch{SkillProgramEffectOp.InvalidateRemainingActualDamageCardEffects=>SkillProgramTriggerWindow.AfterDamageApplied,SkillProgramEffectOp.ClaimCompletedUndamagedIncomingCardEntities=>SkillProgramTriggerWindow.CardUseCompleted,SkillProgramEffectOp.PreventYinjuDamageAndRecover=>SkillProgramTriggerWindow.BeforeDamageApplied,_=>SkillProgramTriggerWindow.CardUseTargetsFinalized};
  if(t.Window!=expected||t.Optional!=(op is SkillProgramEffectOp.InvalidateRemainingActualDamageCardEffects or SkillProgramEffectOp.ClaimCompletedUndamagedIncomingCardEntities))throw new InvalidOperationException(path+": XinPi requires its exact native optional Chijie or mandatory issued Yinju window.");
 }
}
