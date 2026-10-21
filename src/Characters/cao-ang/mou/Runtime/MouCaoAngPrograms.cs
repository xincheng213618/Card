using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouCaoAngStage { DiscardChildren, Material, UseChoice, IssuingUse, NativeUse, ReturnedUse, Drawing, DrawChildren }
public sealed record MouCaEntity(int Id,CardKind Kind,CardLocation From);
public sealed record MouCaPair(int CardId,int Actor,CardKind Kind,string Name);
public sealed record MouCaNativeUse(long FrameId,int Instruction,CardConversionSource Source,string GameplayHash,int Turn,int TurnOwner,int Actor,int CardId,CardKind Kind,LegalAction Action,long UseId,long ActionId)
{
    private IReadOnlyList<int> _targets=[];
    public IReadOnlyList<int> Targets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
}
public sealed record MouCaoAngReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn,int TurnOwner,long ParentId)
{
    public MouCaoAngStage Stage {get;init;} public int Loser {get;init;}=-1;public int Requested {get;init;}public int Actor {get;init;}=-1;public int CardId {get;init;}public int ActualDiscard {get;init;}
    public long Before {get;init;}public long After {get;init;}public NativeDrawOutcome? Draw {get;init;}public MouCaNativeUse? Native {get;init;}
    private IReadOnlyList<MouCaEntity> _equipment=[];
    public IReadOnlyList<MouCaEntity> Equipment {get=>_equipment;init=>_equipment=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public MouCaoAngReceipt? MouCaoAng {get;init;} }
public sealed partial record CardUseFrame { [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public MouCaNativeUse? MouCaoAngZhiwang {get;init;} }
public sealed record MouCaStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn,int TurnOwner,long ParentId):IGameEvent;
public sealed record MouCaFengminPaidEvent(long FrameId,int Owner,string Instance,int Turn,int TurnOwner,long BatchId,int Loser,int Count):IGameEvent;
public sealed record MouCaDiscardedEvent(long FrameId,int CardId,CardKind Kind,CardLocation To,long Sequence):IGameEvent;
public sealed record MouCaUseIssuedEvent(long FrameId,long UseId,long ActionId,int CardId,int Actor):IGameEvent;
public sealed record MouCaUseReturnedEvent(long FrameId,long UseId,long ActionId):IGameEvent;
public sealed record MouCaSourceLessEvent(long UseId,long ActionId,int Target,int CardId):IGameEvent;
public sealed record MouCaDrawReturnedEvent(long FrameId,int Requested,int Actual,int Replacement,long? AttemptId):IGameEvent;
public sealed record MouCaCompletedEvent(long FrameId):IGameEvent;

internal interface IMouCaoAngRuntime:IProgramRuntimeOperations { SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op); }
internal interface IMouCaoAngFacts:IProgramSelectionQuery
{
    int Turn {get;}int TurnOwner {get;}long Sequence {get;}bool Finished {get;}bool Alive(int seat);bool Human(int seat);string Name(int seat);SkillProgram? Program(string id);IEnumerable<T> History<T>()where T:IGameEvent;
    bool Parent(ProgramSkillFrame f);int LostEquipmentOwner(ProgramSkillFrame f);int EmptySlots(int seat);CardKind Kind(int id);IReadOnlyList<MouCaEntity> Equipment(int seat);IReadOnlyList<MouCaPair> Pairs(ProgramSkillFrame f);IReadOnlyList<LegalAction> Plans(ProgramSkillFrame f);double TargetValue(int actor,int target);
}
internal interface IMouCaoAngNative:IProgramRuntimeOperations
{ void Finish(ProgramSkillFrame f);void Publish();bool Drain(ProgramSkillFrame f);void Discard(ProgramSkillFrame f);void Use(ProgramSkillFrame f); }
internal abstract class MouCaoAngDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler=>new MouCaoAngHandler(Op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements=>new(Triggered:ProgramTrackedFact.NativeActionAppearance|ProgramTrackedFact.NativeLogicalDiscard|ProgramTrackedFact.ActualDiscardRecoveryPhase);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c)=>c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ResolveMouCaoAngFengminDescriptor:MouCaoAngDescriptor { public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouCaoAngFengmin; }
internal sealed class ResolveMouCaoAngZhiwangDescriptor:MouCaoAngDescriptor { public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouCaoAngZhiwang; }
internal sealed class MouCaoAngHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{ public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IMouCaoAngRuntime>().Resolve(f,op); }
