using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouJuShouStage { DrawingThree, DrawThreeChildren, Choice, HealthChildren, DrawingOne, DrawOneChildren, Use, IssuingUse, Declaration, NativeUse, ReturnedUse, DiscardChildren, RecoverChildren, Penalty, PenaltyChildren, FirstLossChoice }
public enum MouJsMuwangKind { ChooseFirst, Recover, Penalty }
public sealed record MouJsMuwangQueuedEvent(int Ordinal,CardConversionSource Source,string GameplayHash,int Turn,long BatchId,MouJsMuwangKind Kind,int CardId,long FirstSequence):IGameEvent;
public sealed record MouJsFirstCandidateEvent(int Ordinal,int CardId,CardKind Kind,CardLocation From,long Sequence):IGameEvent;
public sealed record MouJsFirstChosenEvent(long FrameId,int Ordinal,CardConversionSource Source,int Turn,int CardId,long FirstSequence):IGameEvent;
public sealed record MouJsRecoveredEvent(long FrameId,int Ordinal,CardConversionSource Source,int Turn,int CardId,long FirstSequence,long ReturnSequence):IGameEvent;
public sealed record MouJsMuwangSettledEvent(long FrameId,int Ordinal):IGameEvent;
internal sealed record MouJsObservedBatch(long Id,int Turn,IReadOnlyList<CardMovementRecord> Moves);
public sealed record MouJsPlan(string Form,LegalAction Action,CardKind Kind)
{
 private IReadOnlyList<int> _cards=[],_targets=[];
 public IReadOnlyList<int> Cards {get=>_cards;init=>_cards=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> Targets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
}
public sealed record MouJsNativeUse(long FrameId,int Instruction,CardConversionSource Source,string GameplayHash,int Turn,int Actor,MouJsPlan Plan,long UseId,long ActionId)
{
 private IReadOnlyList<int> _targets=[];public IReadOnlyList<int> Targets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
}
public sealed record MouJuShouReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn,int Actor)
{
 public MouJuShouStage Stage {get;init;}public long Before {get;init;}public long After {get;init;}public NativeDrawOutcome? Draw {get;init;}public MouJsNativeUse? Native {get;init;}public long DeclarationId {get;init;}public bool DeclarationPassed {get;init;}
 public int CardId {get;init;}public long LossSequence {get;init;}public bool Recover {get;init;}public bool Penalty {get;init;}public int MuwangOrdinal {get;init;}=-1;
 private IReadOnlyList<int> _bound=[],_discard=[];
 public IReadOnlyList<int> Bound {get=>_bound;init=>_bound=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> Discard {get=>_discard;init=>_discard=Array.AsReadOnly(value.ToArray());}
}
public sealed record MouJsZuoJunGrant(long FrameId,CardConversionSource Source,string GameplayHash,int Recipient,int CardId,int GrantedTurn,long Sequence);
public sealed record MouJsFirstLoss(CardConversionSource Source,string GameplayHash,int Turn,int CardId,long FirstSequence,bool Returned=false,bool LostAgain=false);
public sealed partial class CharacterState
{
 internal IReadOnlyList<MouJsZuoJunGrant>? MouJsZuoJunGrants {get;set;}
 internal IReadOnlyList<MouJsFirstLoss>? MouJsFirstLosses {get;set;}
}
public sealed partial record ProgramSkillFrame {[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouJuShouReceipt? MouJuShou {get;init;}}
public sealed partial record CardUseFrame {[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouJsNativeUse? MouJuShouZuoJun {get;init;}}
public sealed record MouJsStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn,int Actor):IGameEvent;
public sealed record MouJsDrawReturnedEvent(long FrameId,int Requested,int Actual,int Replacement,long? AttemptId):IGameEvent;
public sealed record MouJsBranchEvent(long FrameId,int Recipient,bool Use):IGameEvent;
public sealed record MouJsGrantEvent(long FrameId,int Recipient,int Count,int Turn):IGameEvent;
public sealed record MouJsUseIssuedEvent(long FrameId,long UseId,long ActionId,int Actor):IGameEvent;
public sealed record MouJsUseReturnedEvent(long FrameId,long UseId,long ActionId):IGameEvent;
public sealed record MouJsDeclarationReturnedEvent(long FrameId,long DeclarationId,bool Succeeded):IGameEvent;
public sealed record MouJsMovedEvent(long FrameId,int CardId,CardLocation From,CardLocation To,long Sequence):IGameEvent;
public sealed record MouJsCompletedEvent(long FrameId):IGameEvent;
internal interface IMouJuShouRuntime:IProgramRuntimeOperations {SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal interface IMouJuShouFacts:IProgramSelectionQuery
{
 int Turn {get;}long Sequence {get;}bool Finished {get;}bool Alive(int seat);bool Human(int seat);SkillProgram? Program(string id);IEnumerable<T> History<T>()where T:IGameEvent;
 bool Parent(ProgramSkillFrame f);bool InHand(int seat,int id);IReadOnlyList<int> Discardable(int seat);string Label(int id);IReadOnlyList<MouJsPlan> Plans(ProgramSkillFrame f);
 IReadOnlyList<MouJsZuoJunGrant> Grants(int seat);IReadOnlyList<MouJsFirstLoss> Losses(int seat);IReadOnlyList<int> Seats {get;}IReadOnlyList<(CardConversionSource Source,string Hash)> MuwangSources(int seat);bool Eligible(int id);
 bool InDiscard(int id);bool Reclaimable(MouJsFirstLoss loss,CardMovementRecord discarded);bool ReadyToReclaim(MouJsFirstLoss loss);MouJsMuwangQueuedEvent? MuwangOpportunity(ProgramSkillFrame frame);
}
internal interface IMouJuShouNative:IProgramRuntimeOperations
{
 void Finish(ProgramSkillFrame f);void Publish();bool Drain(ProgramSkillFrame f);SkillProgramStepOutcome LoseHp(ProgramSkillFrame f);void Use(ProgramSkillFrame f);void Discard(ProgramSkillFrame f);void Recover(ProgramSkillFrame f);
 void SetGrants(int seat,IReadOnlyList<MouJsZuoJunGrant> grants);void SetLosses(int seat,IReadOnlyList<MouJsFirstLoss> losses);
}
internal abstract class MouJuShouDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);public override ISkillProgramEffectHandler Handler=>new MouJuShouHandler(Op);public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
 public override ProgramFactRequirements FactRequirements=>new(Triggered:ProgramTrackedFact.NativeActionAppearance|ProgramTrackedFact.NativeLogicalDiscard,Activated:ProgramTrackedFact.NativeActionAppearance|ProgramTrackedFact.NativeLogicalDiscard);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c)=>c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ResolveMouJuShouZuoJunDescriptor:MouJuShouDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouJuShouZuoJun;}
internal sealed class ResolveMouJuShouMuwangDescriptor:MouJuShouDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouJuShouMuwang;}
internal sealed class MouJuShouHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IMouJuShouRuntime>().Resolve(f,op);}
