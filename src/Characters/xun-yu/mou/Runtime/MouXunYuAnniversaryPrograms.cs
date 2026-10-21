using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouXyStage{Initial,Target,Drawing,DrawChildren,Discard,DiscardChildren,Kind,Plan,Repeat,IssuingUse,NativeUse,ReturnedUse,Beneficiary,Allocation,AllocationChildren}
public sealed record MouXyCard(int Id,CardKind Kind,string Label);
public sealed record MouXyPlan(CardKind Kind,LegalActionKind ActionKind,int? TargetCardId,CardKind? Required,bool Extra)
{private IReadOnlyList<int>_targets=[];public IReadOnlyList<int> Targets{get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}}
public sealed record MouXyBizuoGrant(long FrameId,CardConversionSource Source,string GameplayHash,int IssuedTurn,int Beneficiary);
public sealed record MouXyBizuoStartedEvent(MouXyBizuoGrant Grant,int ActualTurn,int Actor):IGameEvent;
public sealed record MouXyBizuoEndedEvent(MouXyBizuoGrant Grant,int ActualTurn,int Actor):IGameEvent;
public sealed record MouXyBizuoGrantedEvent(MouXyBizuoGrant Grant):IGameEvent;
public sealed record MouXyDiscardEntry(long GrantFrameId,int ActualTurn,int CardId,long Sequence);
public sealed record MouXyDiscardEntryRecordedEvent(MouXyDiscardEntry Entry):IGameEvent;
public sealed record MouXyAllocationCard(int Id,CardKind Kind,string Label,CardLocation From,long SourceSequence,long? ZhifouFrameId=null);
public sealed record MouXyAllocatedEvent(long FrameId,long GrantFrameId,int CardId,int Recipient,CardLocation From,long SourceSequence,long? ZhifouFrameId,long Sequence):IGameEvent;
public sealed record MouXyAllocationCompletedEvent(long FrameId,long GrantFrameId,int ActualTurn):IGameEvent;
public sealed partial record ActualTurnProgression{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouXyBizuoGrant? MouXyBizuoPending{get;init;}}
public sealed record MouXyReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn)
{
 public MouXyStage Stage{get;init;}public bool Yin{get;init;}public int Actor{get;init;}=-1;public int Requested{get;init;}public int ActualDraw{get;init;}public int ActualDiscard{get;init;}public NativeDrawOutcome? Draw{get;init;}public CardKind? Kind{get;init;}public MouXyPlan? Plan{get;init;}public bool Repeat{get;init;}public long UseId{get;init;}public long ActionId{get;init;}public long Before{get;init;}public long After{get;init;}public MouXyBizuoGrant? Lease{get;init;}public MouXyAllocationCard? AllocationCard{get;init;}public int AllocationRecipient{get;init;}=-1;
 private IReadOnlyList<int>_selected=[];public IReadOnlyList<int> Selected{get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouXyReceipt? MouXunYuAnniversary{get;init;}}
public sealed record MouXyStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn):IGameEvent;
public sealed record MouXyDrawReturnedEvent(long FrameId,int Actor,int Requested,int Actual,int Replacement,long? AttemptId):IGameEvent;
public sealed record MouXyDiscardPaidEvent(long FrameId,int Actor,int CardId,CardLocation From,CardLocation To,long Sequence):IGameEvent;
public sealed record MouXyUseIssuedEvent(long FrameId,long UseId,long ActionId,int Actor,CardKind Kind,bool ExtraTarget,bool Repeat):IGameEvent;
public sealed record MouXyUseReturnedEvent(long FrameId,long UseId,long ActionId):IGameEvent;
public sealed record MouXyCompletedEvent(long FrameId):IGameEvent;
internal interface IMouXunYuAnniversaryRuntime:IProgramRuntimeOperations{SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,SkillProgramEffectOp op);}
internal interface IMouXunYuAnniversaryFacts:IProgramSelectionQuery
{
 int Turn{get;}long Sequence{get;}bool Finished{get;}bool Alive(int seat);bool Human(int seat);bool OwnPlay(int seat);int HandCount(int seat);int MaximumHp(int seat);int Hp(int seat);string Name(int seat);string Instance(int owner,string skill);bool Yin(int owner,string instance);SkillProgram? Program(string skill);SkillPromptPresentation Presentation(string skill);IEnumerable<T> History<T>()where T:IGameEvent;IReadOnlyList<int> Living();IReadOnlyList<MouXyCard> Hand(int actor);IReadOnlyList<MouXyPlan> Plans(ProgramSkillFrame frame);bool Parent(ProgramSkillFrame frame);bool SameChoice(PromptChoice a,PromptChoice b);bool ExtraTurnStarted(TurnStartedEvent started,MouXyBizuoGrant grant);bool NonDiscardEntry(CardMovedEvent move,out int cardId,out long sequence);MouXyBizuoGrant? PendingLease{get;}MouXyBizuoGrant? AllocationLease(ProgramSkillFrame frame);IReadOnlyList<MouXyAllocationCard> AllocationCards(ProgramSkillFrame frame);bool AllocationDue(int owner);
}
internal interface IMouXunYuAnniversaryNative:IProgramRuntimeOperations
{
 void State(ProgramSkillFrame frame,bool yin);void Discard(ProgramSkillFrame frame);void Use(ProgramSkillFrame frame);void Pend(ProgramSkillFrame frame,MouXyBizuoGrant grant);void Suppress(MouXyBizuoGrant grant,int actualTurn,IReadOnlyList<int> targets);void Allocate(ProgramSkillFrame frame);bool Drain(ProgramSkillFrame frame);void Finish(ProgramSkillFrame frame);void Publish();
}
internal abstract class MouXunYuAnniversaryDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);public override ISkillProgramEffectHandler Handler=>new MouXunYuAnniversaryHandler(Op);public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.ChooseOption,static(_,c)=>c.PublicControlValue(3));public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];public override ProgramFactRequirements FactRequirements=>new(Triggered:ProgramTrackedFact.NativeLogicalDiscard,Activated:ProgramTrackedFact.NativeActionAppearance|ProgramTrackedFact.NativeLogicalDiscard);public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c)=>c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class InitializeMouXunYuAnniversaryShimouDescriptor:MouXunYuAnniversaryDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.InitializeMouXunYuAnniversaryShimou;}
internal sealed class ResolveMouXunYuAnniversaryShimouDescriptor:MouXunYuAnniversaryDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouXunYuAnniversaryShimou;}
internal sealed class ResolveMouXunYuAnniversaryBizuoDescriptor:MouXunYuAnniversaryDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouXunYuAnniversaryBizuo;}
internal sealed class SettleMouXunYuAnniversaryBizuoDescriptor:MouXunYuAnniversaryDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.SettleMouXunYuAnniversaryBizuo;}
internal sealed class MouXunYuAnniversaryHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IMouXunYuAnniversaryRuntime>().Resolve(f,op);}
