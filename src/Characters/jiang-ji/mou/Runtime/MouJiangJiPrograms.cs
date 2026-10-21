using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouJiangJiStage{Give,GiveChildren,Use,IssuingUse,NativeUse,ReturnedUse,AfterUse,Drawing,DrawChildren,YingshiTarget,YingshiChoice,Discard,DiscardChildren}
public sealed record MouJiangJiCard(int Id,CardLocation From,CardKind Kind,string Label);
public sealed record MouJiangJiUse(long UseId,long ActionId,long WindowId,CardKind Kind)
{private IReadOnlyList<int>_targets=[];public IReadOnlyList<int> Targets{get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}}
public sealed record MouJiangJiNativeUse(long FrameId,int Instruction,CardConversionSource Source,string GameplayHash,int Turn,int Actor,int CardId,CardKind Kind,LegalAction Action,long UseId,long ActionId)
{private IReadOnlyList<int>_targets=[];public IReadOnlyList<int> Targets{get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}}
public sealed record MouJiangJiReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn,int Giver,long PhaseId,long WindowId,long UseId,long ActionId)
{
 public MouJiangJiStage Stage{get;init;} public MouJiangJiCard? Card{get;init;} public int Target{get;init;}=-1;public int Requested{get;init;}public bool Delivered{get;init;}public bool Damaged{get;init;}public int DrawCursor{get;init;}public NativeDrawOutcome? Draw{get;init;}public MouJiangJiNativeUse? Native{get;init;}
 public long Before{get;init;}public long After{get;init;}
 private IReadOnlyList<int>_targets=[],_selected=[];public IReadOnlyList<int> Targets{get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}public IReadOnlyList<int> Selected{get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouJiangJiReceipt? MouJiangJi{get;init;}}
public sealed partial record CardUseFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouJiangJiNativeUse? MouJiangJiShiju{get;init;}[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouJiangJiEffectRounds? MouJiangJiRounds{get;init;}}
public sealed record MouJiangJiEffectGrant(long FrameId,CardConversionSource Source,string GameplayHash,long WindowId,long UseId,long ActionId,int Target);
public sealed record MouJiangJiEffectRounds(long ActionId,ExtraCardEffectState Native)
{public int Started{get;init;}public bool Active{get;init;}public int ActiveTarget{get;init;}=-1;public int ActiveIndex{get;init;}=-1;private IReadOnlyList<MouJiangJiEffectGrant>_grants=[];public IReadOnlyList<MouJiangJiEffectGrant> Grants{get=>_grants;init=>_grants=Array.AsReadOnly(value.ToArray());}}
public sealed record MouJiangJiStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn,int Giver,long PhaseId,long WindowId,long UseId,long ActionId):IGameEvent;
public sealed record MouJiangJiShijuCommittedEvent(long FrameId,int Owner,int Giver,int Turn,long PhaseId,int CardId,bool Equipment):IGameEvent;
public sealed record MouJiangJiSelfCardCommittedEvent(long FrameId,int CardId):IGameEvent;
public sealed record MouJiangJiGivenEvent(long FrameId,int Giver,int Recipient,int CardId,long Sequence):IGameEvent;
public sealed record MouJiangJiUseIssuedEvent(long FrameId,long UseId,long ActionId,int CardId):IGameEvent;
public sealed record MouJiangJiUseReturnedEvent(long FrameId,long UseId,long ActionId,bool Damaged):IGameEvent;
public sealed record MouJiangJiRangeGrantedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Recipient,int Turn,int Amount):IGameEvent;
public sealed record MouJiangJiDrawReturnedEvent(long FrameId,int Cursor,int Recipient,int Actual,int Replacement,long? AttemptId):IGameEvent;
public sealed record MouJiangJiDiscardPaidEvent(long FrameId,int Target,int CardId,long Sequence):IGameEvent;
public sealed record MouJiangJiDisabledEvent(long FrameId,CardConversionSource Source,int Turn):IGameEvent;
public sealed record MouJiangJiEffectIssuedEvent(MouJiangJiEffectGrant Grant):IGameEvent;
public sealed record MouJiangJiEffectStartedEvent(long UseId,long ActionId,long FrameId,int Round,int Target):IGameEvent;
public sealed record MouJiangJiEffectCompletedEvent(long UseId,long ActionId,long FrameId,int Round,int Target):IGameEvent;
public sealed record MouJiangJiCompletedEvent(long FrameId):IGameEvent;
internal interface IMouJiangJiRuntime:IProgramRuntimeOperations{SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp operation);}
internal interface IMouJiangJiFacts:IProgramSelectionQuery
{
 int Turn{get;}int TurnOwner{get;}long PhaseId{get;}long Sequence{get;}bool Finished{get;}bool Alive(int seat);bool Human(int seat);string Name(int seat);bool ActualPlay(int seat);int EquipmentCount(int seat);SkillProgram? Program(string id);IEnumerable<T> History<T>()where T:IGameEvent;
 IReadOnlyList<int> ShijuOwners(int actor);IReadOnlyList<MouJiangJiCard> Cards(int seat,bool discard);bool Disabled(int seat,string instance);bool Parent(ProgramSkillFrame f);MouJiangJiUse? Use(ProgramTriggerCandidate c,ProgramSkillWindowContext w);IReadOnlyList<LegalAction> UseOptions(ProgramSkillFrame f);CardKind UseKind(int actor,LegalAction action);
}
internal interface IMouJiangJiNative:IProgramRuntimeOperations
{void Finish(ProgramSkillFrame f);void Publish();bool Drain(ProgramSkillFrame f);void Give(ProgramSkillFrame f);void Use(ProgramSkillFrame f);void Repeat(ProgramSkillFrame f);void Discard(ProgramSkillFrame f);void Disable(ProgramSkillFrame f);}
internal abstract class MouJiangJiDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);public override ISkillProgramEffectHandler Handler=>new MouJiangJiHandler(Op);public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
 public override ProgramFactRequirements FactRequirements=>new(Triggered:ProgramTrackedFact.NativeActionAppearance|ProgramTrackedFact.NativeLogicalDiscard|ProgramTrackedFact.ActualDiscardRecoveryPhase,Activated:ProgramTrackedFact.NativeActionAppearance|ProgramTrackedFact.NativeLogicalDiscard|ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>Op==SkillProgramEffectOp.ResolveMouJiangJiYingshi?[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)]:[];
 public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c)=>c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ResolveMouJiangJiShijuDescriptor:MouJiangJiDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouJiangJiShiju;}
internal sealed class ResolveMouJiangJiYingshiDescriptor:MouJiangJiDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouJiangJiYingshi;}
internal sealed class MouJiangJiHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IMouJiangJiRuntime>().Resolve(f,op);}
