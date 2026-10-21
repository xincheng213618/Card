using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouJiaXuStage{Initial,GrantChildren,Target,Show,Recipient,TransferChildren,Drawing,DrawChildren,Use,IssuingUse,NativeUse,ReturnedUse,HealthChildren,AdjustHand,Discard,DiscardChildren,RestoreState,StateChildren}
public sealed record MouJiaXuCard(int Id,int Holder,string Label,CardKind Kind);
public sealed record MouJiaXuNativeUse(long FrameId,int Instruction,CardConversionSource Source,string GameplayHash,int Turn,int Actor,int CardId,CardKind Kind,LegalAction Action,long UseId,long ActionId)
{private IReadOnlyList<int>_targets=[];public IReadOnlyList<int> Targets{get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}}
public sealed record MouJiaXuReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn,bool Yin,string FumouInstance)
{
 public MouJiaXuStage Stage{get;init;}public int Holder{get;init;}=-1;public int Recipient{get;init;}=-1;public int Requested{get;init;}public int DrawCursor{get;init;}public NativeDrawOutcome? Draw{get;init;}public MouJiaXuRecordedEvent? Record{get;init;}public MouJiaXuNativeUse? Native{get;init;}public long Before{get;init;}public long After{get;init;}public int HpBefore{get;init;}public int HpAfter{get;init;}public long? HpChangeId{get;init;}
 private IReadOnlyList<MouJiaXuCard>_view=[];private IReadOnlyList<int>_selected=[],_frozen=[],_remaining=[];
 public IReadOnlyList<MouJiaXuCard> View{get=>_view;init=>_view=Array.AsReadOnly(value.ToArray());}public IReadOnlyList<int> Selected{get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}public IReadOnlyList<int> Frozen{get=>_frozen;init=>_frozen=Array.AsReadOnly(value.ToArray());}public IReadOnlyList<int> Remaining{get=>_remaining;init=>_remaining=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouJiaXuReceipt? MouJiaXu{get;init;}}
public sealed partial record CardUseFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouJiaXuNativeUse? MouJiaXuFumou{get;init;}}
public sealed record MouJiaXuStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn,bool Yin,string FumouInstance):IGameEvent;
public sealed record MouJiaXuRecordedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Hp,int HandCount,bool Yin):IGameEvent;
public sealed record MouJiaXuDerivedGrantedEvent(long FrameId,int Owner):IGameEvent;
public sealed record MouJiaXuViewedEvent(long FrameId,int Owner,int Holder,int Count):IGameEvent;
public sealed record MouJiaXuShownEvent(long FrameId,int Holder,int CardId):IGameEvent;
public sealed record MouJiaXuTransferredEvent(long FrameId,int Holder,int Recipient,int CardId,long Sequence):IGameEvent;
public sealed record MouJiaXuDrawReturnedEvent(long FrameId,int Cursor,int Recipient,int Requested,int Actual,int Replacement,long? AttemptId):IGameEvent;
public sealed record MouJiaXuDiscardPaidEvent(long FrameId,int CardId,long Sequence):IGameEvent;
public sealed record MouJiaXuHpSetEvent(long FrameId,long RecordFrameId,int Before,int After,long? ChangeId):IGameEvent;
public sealed record MouJiaXuUseIssuedEvent(long FrameId,long UseId,long ActionId,int Actor,int CardId):IGameEvent;
public sealed record MouJiaXuUseReturnedEvent(long FrameId,long UseId,long ActionId):IGameEvent;
public sealed record MouJiaXuCompletedEvent(long FrameId):IGameEvent;
internal interface IMouJiaXuRuntime:IProgramRuntimeOperations{SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal interface IMouJiaXuFacts:IProgramSelectionQuery
{
 int Turn{get;}long Sequence{get;}bool Finished{get;}bool Alive(int seat);bool Human(int seat);bool ActualPlay(int seat);int Hp(int seat);int MaximumHp(int seat);string Name(int seat);SkillProgram? Program(string id);IEnumerable<T> History<T>()where T:IGameEvent;
 IReadOnlyList<int> Living();IReadOnlyList<MouJiaXuCard> Hand(int seat,bool discard=false);string FumouInstance(int owner);bool Yin(int owner,string instance);bool Parent(ProgramSkillFrame f);IReadOnlyList<LegalAction> UseOptions(ProgramSkillFrame f);CardKind UseKind(int actor,LegalAction a);
}
internal interface IMouJiaXuNative:IProgramRuntimeOperations{void Finish(ProgramSkillFrame f);void Publish();bool Drain(ProgramSkillFrame f);void State(ProgramSkillFrame f,bool yin);void Grant(ProgramSkillFrame f);void Reveal(ProgramSkillFrame f);void Transfer(ProgramSkillFrame f);void Use(ProgramSkillFrame f);void SetHealth(ProgramSkillFrame f);void Discard(ProgramSkillFrame f);}
internal abstract class MouJiaXuDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);public override ISkillProgramEffectHandler Handler=>new MouJiaXuHandler(Op);public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>Op==SkillProgramEffectOp.InitializeMouJiaXuFumou?[new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)]:[];
 public override ProgramFactRequirements FactRequirements=>new(Triggered:ProgramTrackedFact.NativeActionAppearance,Activated:ProgramTrackedFact.NativeActionAppearance|ProgramTrackedFact.NativeLogicalDiscard|ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c)=>c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class InitializeMouJiaXuFumouDescriptor:MouJiaXuDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.InitializeMouJiaXuFumou;}
internal sealed class ResolveMouJiaXuFumouDescriptor:MouJiaXuDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouJiaXuFumou;}
internal sealed class RecordMouJiaXuSushenDescriptor:MouJiaXuDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.RecordMouJiaXuSushen;}
internal sealed class RestoreMouJiaXuRushiDescriptor:MouJiaXuDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.RestoreMouJiaXuRushi;}
internal sealed class MouJiaXuHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IMouJiaXuRuntime>().Resolve(f,op);}
