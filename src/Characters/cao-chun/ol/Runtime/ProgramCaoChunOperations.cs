using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum CaoChunStage { DrawChildren, InitialSlash, EndingSlash, NativeSlash, DebtChoice, DiscardChildren }
public sealed record ShanjiaPhaseDebt(CardConversionSource Source,string GameplayHash,long ActivationFrameId,int ActualTurn,int PhaseInstance,int FrozenX,int Remaining,bool DiscardedAny=false,bool OnlyEquipment=true,bool EndingConsumed=false);
public sealed record ShanjiaState(CardConversionSource Source,string GameplayHash,int LossMarks,ShanjiaPhaseDebt? Debt=null);
public sealed record CaoChunHandUsePayment(ShanjiaPhaseDebt Debt,long NativeParentId,CardActionContext Action,long Before,long After,ProgramCompletedResponseKind? ResponseKind=null)
{
 private readonly IReadOnlyList<CardActionCost> _hand=Array.AsReadOnly(Array.Empty<CardActionCost>());
 public IReadOnlyList<CardActionCost> HandMaterials {get=>_hand;init=>_hand=Array.AsReadOnly(value.ToArray());}
 private readonly IReadOnlyList<CardMovementRecord> _moves=Array.AsReadOnly(Array.Empty<CardMovementRecord>());
 public IReadOnlyList<CardMovementRecord> Payments {get=>_moves;init=>_moves=Array.AsReadOnly(value.ToArray());}
}
public sealed record CaoChunNativeSlash(long ParentFrameId,CardConversionSource Source,string GameplayHash,int InstructionIndex,int ActualTurn,int PhaseInstance,bool Ending,int TargetSeat,CardKind Kind,long NativeUseId=0,long ActionId=0,bool Returned=false)
{
 private readonly IReadOnlyList<CardActionCost> _materials=Array.AsReadOnly(Array.Empty<CardActionCost>());
 public IReadOnlyList<CardActionCost> Materials {get=>_materials;init=>_materials=Array.AsReadOnly(value.ToArray());}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public RoundOnceCardlessBasicPaymentReceipt? RoundOncePayment {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public DrawFundedDistinctBasicPayment? DrawFundedPayment {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ChainedStateBasicPayment? ChainedPayment {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public int? ProviderSeat {get;init;}
 public long PaymentBefore {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public long? PaymentAfter {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public CardActionContext? Action {get;init;}
}
public sealed record CaoChunReceipt(CardConversionSource Source,string GameplayHash,int InstructionIndex,int ActualTurn,int PhaseInstance,CaoChunStage Stage,long Before=0,long After=0,int ActualCount=0,int? DiscardId=null,CardLocation? DiscardFrom=null,CardKind? DiscardKind=null,long? PaidBatchId=null,CaoChunHandUsePayment? Payment=null,CaoChunNativeSlash? Slash=null)
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public CaoChunFactionRequest? Faction {get;init;}
}
public sealed record CaoChunFactionRequest(long FrameId,int TargetSeat,FactionResponsePolicySource Policy);
public sealed record ShanjiaMarksChangedEvent(CardConversionSource Source,string GameplayHash,int Before,int After,long MovementSequence):IGameEvent;
public sealed record ShanjiaDebtArmedEvent(CardConversionSource Source,string GameplayHash,long ActivationFrameId,int ActualTurn,int PhaseInstance,int FrozenX):IGameEvent;
public sealed record ShanjiaMovementPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,CaoChunStage Stage,long Before,long After,int ActualCount,long? PaidBatchId):IGameEvent;
public sealed record ShanjiaHandUseDebitedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,int PhaseInstance,long ActionId,int RemainingBefore,int RemainingAfter):IGameEvent;
public sealed record ShanjiaNativeSlashIssuedEvent(long FrameId,long NativeUseId,long ActionId,bool Ending,int TargetSeat):IGameEvent;
public sealed record ShanjiaNativeMaterialPaidEvent(long FrameId,long NativeUseId,long ActionId,long Before,long After,int Count):IGameEvent;
public sealed record ShanjiaNativeSlashReturnedEvent(long FrameId,long NativeUseId,long ActionId):IGameEvent;
public sealed partial class CharacterState
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ShanjiaState? Shanjia {get;set;}
}
public sealed partial record ProgramSkillFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public CaoChunReceipt? CaoChun {get;init;}
}
public sealed partial record CardUseFrame
{
 private CardMovementBatchContext? _caoChunMaterialBatch;
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public CardMovementBatchContext? CaoChunMaterialBatch
 {
  get=>_caoChunMaterialBatch;
  init=>_caoChunMaterialBatch=value is null?null:value with{Movements=Array.AsReadOnly(value.Movements.ToArray()),SourceCounts=Array.AsReadOnly(value.SourceCounts.ToArray()),DestinationCounts=value.DestinationCounts is null?null:Array.AsReadOnly(value.DestinationCounts.ToArray()),SanwenGains=value.SanwenGains,LiJueUseDiscard=value.LiJueUseDiscard is{}li?CommittedEventProjection.FreezeLiJueUseDiscard(li):null,NativeDrawInvocation=value.NativeDrawInvocation is{}draw?draw with{Materials=draw.Materials}:null,NativeBuZhiDiscardInvocation=value.NativeBuZhiDiscardInvocation is{}discard?discard with{Zones=discard.Zones,Materials=discard.Materials}:null};
 }
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public CaoChunHandUsePayment? CaoChunDebtQualification {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public CaoChunNativeSlash? CaoChunSlash {get;init;}
}
public sealed partial record ProgramSkillWindowContext
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public CaoChunHandUsePayment? CaoChunPayment {get;init;}
}
internal interface ICaoChunProgramHost {SkillProgramStepOutcome ExecuteCaoChun(ProgramSkillFrame frame,SkillProgramEffectOp op);}
internal abstract class CaoChunDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override ISkillProgramEffectHandler Handler=>new CaoChunHandler(Op);
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,_)=>{});
}
internal sealed class InitializeShanjiaLossMarkersDescriptor:CaoChunDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.InitializeShanjiaLossMarkers;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)];
}
internal sealed class DrawAndRequestShanjiaSlashDescriptor:CaoChunDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DrawAndRequestShanjiaSlash;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
}
internal sealed class PayShanjiaHandUseDebtDescriptor:CaoChunDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.PayShanjiaHandUseDebt;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindows(new[]{SkillProgramTriggerWindow.CardUseBeforeTargetEffects,SkillProgramTriggerWindow.CardUseCommitted})];
}
internal sealed class RequestShanjiaEndingSlashDescriptor:CaoChunDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.RequestShanjiaEndingSlash;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)];
}
internal sealed class CaoChunHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
 public SkillProgramEffectOp Op=>op;
 public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>h.GetCapability<ICaoChunProgramHost>().ExecuteCaoChun(f,op);
}
internal static class CaoChunComposition
{
 private static bool Own(SkillProgramEffectOp op)=>(int)op is >=24500 and <=24503;
 internal static void Activation(string path,SkillProgramActivation a)
 {
  if(!a.Effects.Any(e=>Own(e.Op)))return;
  if(a.Effects is not[{Op:SkillProgramEffectOp.DrawAndRequestShanjiaSlash,Condition.Kind:SkillProgramConditionKind.Always}]||a.MinCards!=0||a.MaxCards!=0||a.MinTargets!=0||a.MaxTargets!=0||a.UsesPerPhase!=1||a.UsesPerTurn is not null||a.MarkerCost is not null||a.Condition.Kind!=SkillProgramConditionKind.Always)throw new InvalidOperationException(path+": Shanjia activation requires an empty actual-Play once-phase invocation.");
 }
 internal static void Trigger(string path,SkillProgramTrigger t)
 {
  if(!t.Effects.Any(e=>Own(e.Op)))return;
  if(t.Effects.Count!=1||t.Effects[0].Condition.Kind!=SkillProgramConditionKind.Always||t.MarkerCost is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always)throw new InvalidOperationException(path+": Shanjia requires one exact unconditional native contract.");
  var op=t.Effects[0].Op;
  if(op==SkillProgramEffectOp.InitializeShanjiaLossMarkers){if(t.Window!=SkillProgramTriggerWindow.GameStarting||t.Subject!=SkillProgramTriggerSubject.Owner||t.Optional)throw new InvalidOperationException(path+": initial loss marks require mandatory native game start.");}
  else if(op==SkillProgramEffectOp.PayShanjiaHandUseDebt){if(t.Window!=SkillProgramTriggerWindow.CardUseBeforeTargetEffects||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor||t.Optional||!t.IncludeResponseUses)throw new InvalidOperationException(path+": debt requires mandatory actual paid own hand use.");}
  else if(op==SkillProgramEffectOp.RequestShanjiaEndingSlash){if(t.Window!=SkillProgramTriggerWindow.PlayEnding||t.Subject!=SkillProgramTriggerSubject.Owner||!t.Optional)throw new InvalidOperationException(path+": ending Slash requires optional actual own Play ending.");}
  else throw new InvalidOperationException(path+": activation-only Shanjia operation in trigger.");
 }
}

public sealed partial record ProgramCardTriggerWindowFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public bool? CaoChunDebtBeforeEffects {get;init;}
}
