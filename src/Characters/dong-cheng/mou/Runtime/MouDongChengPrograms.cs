using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouDongChengStage { Opening, Slot, SlotChildren, ClaimChildren, Cost, Targets, CostChildren, IssuingUse, NativeUse, ReturnedUse, RecoveryChildren, Restore, RestoreChildren }
public sealed record MouDcLink(CardConversionSource Source,int Other);
public sealed record MouDcOccurrence(long Id,int Turn,long AttackId,long UseId,int CardId,int Target,bool First);
public sealed record MouDcClaim(long ProtectionFrameId,CardConversionSource Source,string GameplayHash,int Turn,long BeforeWindowId,long AttackId,long UseId,long ActionId,int Target,EquipmentSlot Slot)
{
 public bool Settled {get;init;}public long Before {get;init;}public long After {get;init;}
 private IReadOnlyList<int>_cards=[];public IReadOnlyList<int> Cards {get=>_cards;init=>_cards=Array.AsReadOnly(value.ToArray());}
}
public sealed record MouDcNativeUse(long FrameId,CardConversionSource Source,string GameplayHash,int Turn,CardKind Kind,long UseId,long ActionId)
{
 private IReadOnlyList<int>_targets=[];public IReadOnlyList<int> Targets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
}
public sealed record MouDcEntity(int Id,CardKind Kind,CardLocation From,string Label);
public sealed record MouDongChengReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn,long ParentId)
{
 public MouDongChengStage Stage {get;init;}public int CardId {get;init;}public CardKind Kind {get;init;}public EquipmentSlot? Slot {get;init;}public long Before {get;init;}public long After {get;init;}public int Actual {get;init;}public MouDcOccurrence? Occurrence {get;init;}public long UseId {get;init;}public MouDcNativeUse? Native {get;init;}public bool Returned {get;init;}
 private IReadOnlyList<int>_targets=[];public IReadOnlyList<int> Targets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
 private IReadOnlyList<long>_dying=[];public IReadOnlyList<long> Dying {get=>_dying;init=>_dying=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame {[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouDongChengReceipt? MouDongCheng {get;init;}}
public sealed partial record CardUseFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouDcNativeUse? MouDongChengDouwei {get;init;}
 private IReadOnlyList<MouDcClaim>?_mouDcClaims;
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public IReadOnlyList<MouDcClaim>? MouDongChengClaims {get=>_mouDcClaims;init=>_mouDcClaims=value is null?null:Array.AsReadOnly(value.ToArray());}
}
public sealed partial record BeforeDamageProgramWindowFrame {[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouDcOccurrence? MouDongChengOccurrence {get;init;}}
public sealed partial class CharacterState
{
 private IReadOnlyList<MouDcLink>?_mouDcLinks;
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public IReadOnlyList<MouDcLink>? MouDongChengLinks {get=>_mouDcLinks;set=>_mouDcLinks=value is null?null:Array.AsReadOnly(value.ToArray());}
 public int MouDongChengDisabledTurn {get;set;}=-1;
}
public sealed record MouDcStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn,long ParentId):IGameEvent;
public sealed record MouDcLinkedEvent(long FrameId,CardConversionSource Source,int Other):IGameEvent;
public sealed record MouDcDamageSeenEvent(MouDcOccurrence Occurrence):IGameEvent;
public sealed record MouDcSlotPaidEvent(long FrameId,long BeforeWindowId,int Target,EquipmentSlot Slot,long UseId):IGameEvent;
public sealed record MouDcMovementPaidEvent(long FrameId,int CardId,int Actual,long Before,long After):IGameEvent;
public sealed record MouDcClaimSettledEvent(long FrameId,long UseId,int Count):IGameEvent;
public sealed record MouDcUseIssuedEvent(long FrameId,long UseId,long ActionId,int Owner,CardKind Kind):IGameEvent;
public sealed record MouDcUseReturnedEvent(long FrameId,long UseId,long ActionId):IGameEvent;
public sealed record MouDcDyingEvent(long FrameId,long UseId,long DamageId,long DyingId,int Target):IGameEvent;
public sealed record MouDcRestoredEvent(long FrameId,EquipmentSlot Slot):IGameEvent;
public sealed record MouDcCompletedEvent(long FrameId):IGameEvent;
internal interface IMouDongChengRuntime:IProgramRuntimeOperations {SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal interface IMouDongChengFacts:IProgramSelectionQuery
{
 int Turn {get;}long Sequence {get;}bool Finished {get;}bool Alive(int seat);bool Human(int seat);string Name(int seat);double TargetScore(int owner,int target);int KeepValue(int owner,int card);SkillProgram? Program(string id);IEnumerable<T> History<T>()where T:IGameEvent;
 bool Parent(ProgramSkillFrame f);MouDcOccurrence? Before(ProgramTriggerCandidate c,ProgramSkillWindowContext w);long ClaimUse(ProgramTriggerCandidate c,ProgramSkillWindowContext w);bool MayActivate(int owner);IReadOnlyList<int> Living {get;}IReadOnlyList<EquipmentSlot> Slots(int owner,bool abolished);IReadOnlyList<MouDcEntity> Costs(int owner);IReadOnlyList<int> Targets(ProgramSkillFrame f,CardKind kind);(long Frame,long Use,long Damage)? DyingCause(PlayerDyingEvent fact);
}
internal interface IMouDongChengNative:IProgramRuntimeOperations
{
 void Finish(ProgramSkillFrame f);void Publish();bool Drain(ProgramSkillFrame f);void Link(ProgramSkillFrame f,int other);void Protect(ProgramSkillFrame f);void Claim(ProgramSkillFrame f);void Discard(ProgramSkillFrame f);void Use(ProgramSkillFrame f);void Recover(ProgramSkillFrame f);void Restore(ProgramSkillFrame f);void Disable(ProgramSkillFrame f);
}
internal abstract class MouDongChengDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);public override ISkillProgramEffectHandler Handler=>new MouDongChengHandler(Op);public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c)=>c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class InitializeMouDongChengBaojiaDescriptor:MouDongChengDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.InitializeMouDongChengBaojia;}
internal sealed class ResolveMouDongChengBaojiaDescriptor:MouDongChengDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouDongChengBaojia;}
internal sealed class ClaimMouDongChengBaojiaDescriptor:MouDongChengDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ClaimMouDongChengBaojia;}
internal sealed class ResolveMouDongChengDouweiDescriptor:MouDongChengDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouDongChengDouwei;}
internal sealed class MouDongChengHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IMouDongChengRuntime>().Resolve(f,op);}
