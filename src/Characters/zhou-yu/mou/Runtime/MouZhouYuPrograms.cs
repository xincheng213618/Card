using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouZhouYuStage { Initial, Target, MaxHandActor, OfferUse, IssuingUse, NativeUse, ReturnedUse, Drawing, DrawChildren, Discard, DiscardChildren, Damage, DamageChildren }
public sealed record MouZhouYuCard(int Id, CardLocation From, string Label, CardKind Kind, bool Related);
public sealed record MouZhouYuCompletedUse(long WindowId,long UseId,CardActionContext Action);
public sealed record MouZhouYuNativeUse(long FrameId,int Instruction,CardConversionSource Source,string GameplayHash,int Turn,int Actor,int CardId,CardKind Kind,LegalAction? Action,long UseId,long ActionId)
{
 private IReadOnlyList<int> _targets=[];
 public IReadOnlyList<int> Targets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
}
public sealed record MouZhouYuReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn,long ParentId,long OriginalAction,bool Yin)
{
 public MouZhouYuStage Stage {get;init;}
 public int Target {get;init;}=-1; public int Actor {get;init;}=-1; public int Requested {get;init;}
 public long Before {get;init;} public long After {get;init;} public bool Damaged {get;init;}
 public NativeDrawOutcome? Draw {get;init;} public MouZhouYuNativeUse? Native {get;init;}
 private IReadOnlyList<int> _targets=[],_frozen=[],_remaining=[],_selected=[];
 public IReadOnlyList<int> Targets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> Frozen {get=>_frozen;init=>_frozen=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> Remaining {get=>_remaining;init=>_remaining=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> Selected {get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public MouZhouYuReceipt? MouZhouYuAnniversary {get;init;} }
public sealed partial record CardUseFrame { [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public MouZhouYuNativeUse? MouZhouYuYingmou {get;init;} }
public sealed record MouZhouYuStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn,long ParentId,long OriginalAction,bool Yin):IGameEvent;
public sealed record MouZhouYuActivatedEvent(long FrameId,int Owner,int Turn,long ActionId,bool Yin):IGameEvent;
public sealed record MouZhouYuFireGrantedEvent(long DamageId,CardConversionSource Source,string GameplayHash):IGameEvent;
public sealed record MouZhouYuFireConsumedEvent(long DamageId,CardConversionSource Source,long UseId,long ActionId):IGameEvent;
public sealed record MouZhouYuUseIssuedEvent(long FrameId,long UseId,long ActionId,int Actor,int CardId,int Target):IGameEvent;
public sealed record MouZhouYuUseReturnedEvent(long FrameId,long UseId,long ActionId):IGameEvent;
public sealed record MouZhouYuTargetDamagedEvent(long FrameId,long DamageId,int Target,int Amount):IGameEvent;
public sealed record MouZhouYuDiscardPaidEvent(long FrameId,int Actor,int CardId,long Sequence):IGameEvent;
public sealed record MouZhouYuDrawReturnedEvent(long FrameId,int Requested,int Drawn,int Replacement,long? AttemptId):IGameEvent;
public sealed record MouZhouYuCompletedEvent(long FrameId):IGameEvent;
internal interface IMouZhouYuRuntime:IProgramRuntimeOperations { SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp operation); }
internal interface IMouZhouYuFacts:IProgramSelectionQuery
{
 int Turn{get;} long Sequence{get;} bool Finished{get;} bool Alive(int seat); bool Human(int seat); string Name(int seat); int HandCount(int seat); int FactionCount{get;}
 IReadOnlyList<int> Living(); SkillProgram? Program(string id); IEnumerable<T> History<T>() where T:IGameEvent;
 string Instance(int seat,string skill); bool Yin(int seat,string instance); IReadOnlyList<CardConversionSource> FireSources();
 long? FireDamage(ProgramTriggerCandidate c,ProgramSkillWindowContext w);
 MouZhouYuCompletedUse? Use(ProgramTriggerCandidate c,ProgramSkillWindowContext w); bool Parent(ProgramSkillFrame f);
 IReadOnlyList<MouZhouYuCard> Hand(int seat); IReadOnlyList<MouZhouYuCard> DiscardableHand(int seat);
 IReadOnlyList<LegalAction> UseOptions(ProgramSkillFrame f); IReadOnlyList<IReadOnlyList<int>> FireOptions(ProgramSkillFrame f);
}
internal interface IMouZhouYuNative:IProgramRuntimeOperations
{
 void Finish(ProgramSkillFrame f); void Publish(); bool Drain(ProgramSkillFrame f); void State(ProgramSkillFrame f,bool yin);
 void Use(ProgramSkillFrame f); void Discard(ProgramSkillFrame f); void Damage(ProgramSkillFrame f);
}
internal abstract class MouZhouYuDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override ISkillProgramEffectHandler Handler=>new MouZhouYuHandler(Op);
 public override ProgramOperationInteraction Interaction=>Op==SkillProgramEffectOp.IssueMouZhouYuRonghuo?ProgramOperationInteraction.Automatic:ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.Damage,static(_,c)=>c.PublicControlValue(3));
 public override ProgramFactRequirements FactRequirements=>new(Triggered:ProgramTrackedFact.NativeActionAppearance|ProgramTrackedFact.WholeActualUseDeclaration|ProgramTrackedFact.NativeLogicalDiscard);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>Op==SkillProgramEffectOp.ResolveMouZhouYuYingmou?[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)]:Op==SkillProgramEffectOp.IssueMouZhouYuRonghuo?[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)]:[];
 public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c)=>c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class IssueMouZhouYuRonghuoDescriptor:MouZhouYuDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.IssueMouZhouYuRonghuo;}
internal sealed class InitializeMouZhouYuYingmouDescriptor:MouZhouYuDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.InitializeMouZhouYuYingmou;}
internal sealed class ResolveMouZhouYuYingmouDescriptor:MouZhouYuDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouZhouYuYingmou;}
internal sealed class MouZhouYuHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
 public SkillProgramEffectOp Op=>op;
 public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IMouZhouYuRuntime>().Resolve(f,op);
}
