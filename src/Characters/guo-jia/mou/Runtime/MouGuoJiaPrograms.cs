using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouGuoJiaStage { Opening, RemoveGrantChildren, Target, Select, MovementChildren, GrantChildren, Judging, JudgmentChildren, DamageChildren, YijiCard, YijiTarget, YijiChildren }
public sealed record MouGjEntity(int Id,CardKind Kind,CardLocation From,string Label);
public sealed record MouGuoJiaReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn,long ParentId,bool Yin,int X)
{
 public MouGuoJiaStage Stage {get;init;}public int Target {get;init;}=-1;public int Cursor {get;init;}public int CardId {get;init;}public int Actual {get;init;}public long Before {get;init;}public long After {get;init;}
 public long JudgmentId {get;init;}public int? JudgmentCard {get;init;}public Suit? JudgmentSuit {get;init;}public int? JudgmentRank {get;init;}public long? JudgmentHandoff {get;init;}public bool LightningHit {get;init;}
 private IReadOnlyList<MouGjEntity>_viewed=[];private IReadOnlyList<int>_selected=[];
 public IReadOnlyList<MouGjEntity> Viewed {get=>_viewed;init=>_viewed=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> Selected {get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame {[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouGuoJiaReceipt? MouGuoJia {get;init;}}
public sealed record MouGjLossSampleEvent(int Turn,int Owner,string Instance,int Count):IGameEvent;
public sealed record MouGjStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn,long ParentId,bool Yin,int X):IGameEvent;
public sealed record MouGjViewedEvent(long FrameId,int Viewer,int Target,int Count,bool Top):IGameEvent;
public sealed record MouGjMovementPaidEvent(long FrameId,int Cursor,int Actual,long Before,long After):IGameEvent;
public sealed record MouGjYijiGrantEvent(long FrameId,CardConversionSource Source,string GrantSource,bool Granted,int Count):IGameEvent;
public sealed record MouGjJudgmentReturnedEvent(long FrameId,long JudgmentId,int? CardId,Suit? Suit,int? Rank,long? Handoff):IGameEvent;
public sealed record MouGjLightningIssuedEvent(long FrameId,long JudgmentId,int Target,int Amount):IGameEvent;
public sealed record MouGjCompletedEvent(long FrameId):IGameEvent;
internal interface IMouGuoJiaRuntime:IProgramRuntimeOperations {SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal interface IMouGuoJiaFacts:IProgramSelectionQuery
{
 int Turn {get;}long Sequence {get;}bool Finished {get;}bool Alive(int seat);bool Human(int seat);string Name(int seat);SkillProgram? Program(string id);IEnumerable<T> History<T>()where T:IGameEvent;
 bool Parent(ProgramSkillFrame f);bool Yin(int seat,string instance);IReadOnlyList<int> Living {get;}IReadOnlyList<MouGjEntity> Hand(int seat);bool At(MouGjEntity card);bool Discardable(int seat,int id);int LossCount(int seat);IReadOnlyList<(int Seat,string Instance)> XianmouOwners {get;}
}
internal interface IMouGuoJiaNative:IProgramRuntimeOperations
{
 void Finish(ProgramSkillFrame f);void Publish();bool Drain(ProgramSkillFrame f);void State(ProgramSkillFrame f,bool yin);IReadOnlyList<MouGjEntity> Peek(ProgramSkillFrame f,int count);void Move(ProgramSkillFrame f);void YijiGrant(ProgramSkillFrame f,bool acquire);void Judge(ProgramSkillFrame f);void Damage(ProgramSkillFrame f);
}
internal abstract class MouGuoJiaDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);public override ISkillProgramEffectHandler Handler=>new MouGuoJiaHandler(Op);public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
 public override ProgramFactRequirements FactRequirements=>new(Triggered:ProgramTrackedFact.NativeLogicalDiscard);public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c)=>c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class InitializeMouGuoJiaXianmouDescriptor:MouGuoJiaDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.InitializeMouGuoJiaXianmou;}
internal sealed class ResolveMouGuoJiaXianmouDescriptor:MouGuoJiaDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouGuoJiaXianmou;}
internal sealed class ResolveMouGuoJiaYijiDescriptor:MouGuoJiaDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouGuoJiaYiji;}
internal sealed class MouGuoJiaHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IMouGuoJiaRuntime>().Resolve(f,op);}
