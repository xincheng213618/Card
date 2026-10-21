using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouCyStage { Peek, ObtainChildren, Invalidate, Target, RevealChildren, RevealChoice, GainChildren, DiscardChildren, Damage, Complete }
public sealed record MouCyCard(int Id, CardKind Kind, string Label);
public sealed record MouCyUse(long WindowId,long ParentId,CardActionContext Action,bool Response);
public sealed record MouCyNameRecordedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Round,CardKind Name,int CardId,long Sequence):IGameEvent;
public sealed record MouCyNameConsumedEvent(long FrameId,long RecordFrameId,long ActionId):IGameEvent;
public sealed record MouCyUseInvalidatedEvent(long FrameId,long RecordFrameId,CardConversionSource Source,string GameplayHash,MouCyUse Use):IGameEvent;
public sealed record MouCyStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn,int Round,long ParentId):IGameEvent;
public sealed record MouCyMovedEvent(long FrameId,int CardId,CardLocation From,CardLocation To,long Sequence):IGameEvent;
public sealed record MouCyRevealedEvent(long FrameId,int CardId,CardKind Kind,int Count,long Sequence):IGameEvent;
public sealed record MouCyRangeIssuedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Target,int Amount,int IssuedTurn):IGameEvent;
public sealed record MouCyCompletedEvent(long FrameId):IGameEvent;
public sealed record MouCyReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn,int Round,long ParentId)
{
 public MouCyStage Stage{get;init;}public int Target{get;init;}=-1;public int CardId{get;init;}public CardKind Kind{get;init;}public int RevealCount{get;init;}public bool DamageCard{get;init;}public long Before{get;init;}public long After{get;init;}public bool Again{get;init;}public bool Obtained{get;init;}public MouCyUse? Use{get;init;}public MouCyNameRecordedEvent? Record{get;init;}
 private IReadOnlyList<MouCyCard> _view=[];public IReadOnlyList<MouCyCard> View{get=>_view;init=>_view=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouCyReceipt? MouChengYuAnniversary{get;init;}}
public sealed partial record CardUseFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouCyUseInvalidatedEvent? MouCyInvalidation{get;init;}
 private IReadOnlyList<MouCyUseInvalidatedEvent>? _mouCyResponses;
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public IReadOnlyList<MouCyUseInvalidatedEvent>? MouCyResponseInvalidations{get=>_mouCyResponses;init=>_mouCyResponses=value is null?null:Array.AsReadOnly(value.ToArray());}
}
public sealed partial record NullificationWindowFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouCyUseInvalidatedEvent? MouCyResponseInvalidation{get;init;}}
internal interface IMouChengYuAnniversaryRuntime:IProgramRuntimeOperations{SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,SkillProgramEffectOp op);}
internal interface IMouChengYuAnniversaryFacts:IProgramSelectionQuery
{
 int Turn{get;}int Round{get;}long Sequence{get;}int LivingCount{get;}bool Finished{get;}bool Enabled{get;}bool Alive(int seat);bool Human(int seat);bool OwnPlay(int seat);string Name(int seat);CardKind CardName(CardKind kind);SkillProgram? Program(string skill);SkillPromptPresentation Presentation(string skill);IEnumerable<T> History<T>()where T:IGameEvent;
 IReadOnlyList<int> Living();IReadOnlyList<ProgramTriggerCandidate> Candidates(int owner,SkillProgramTriggerWindow window);bool Parent(ProgramSkillFrame frame);MouCyUse? Use(ProgramSkillWindowContext context);bool At(int id,CardLocation location);bool SameChoice(PromptChoice left,PromptChoice right);bool DamageCard(CardKind kind);
}
internal interface IMouChengYuAnniversaryNative:IProgramRuntimeOperations
{
 IReadOnlyList<MouCyCard> Peek(ProgramSkillFrame frame);MouCyCard? Reveal(ProgramSkillFrame frame);void Move(ProgramSkillFrame frame,int id,CardLocation from,CardLocation to);void Invalidate(ProgramSkillFrame frame,MouCyUseInvalidatedEvent proof);void Damage(ProgramSkillFrame frame,int target,int amount);bool Drain(ProgramSkillFrame frame);void Finish(ProgramSkillFrame frame);void Publish();
}
internal abstract class MouChengYuAnniversaryDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);public override ISkillProgramEffectHandler Handler=>new MouChengYuAnniversaryHandler(Op);public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.ChooseOption,static(_,c)=>c.PublicControlValue(3));public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
 public override ProgramFactRequirements FactRequirements=>new(Triggered:ProgramTrackedFact.NativeActionAppearance);
 public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c)=>c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ResolveMouChengYuAnniversaryShizhaDescriptor:MouChengYuAnniversaryDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouChengYuAnniversaryShizha;}
internal sealed class ConsumeMouChengYuAnniversaryShizhaDescriptor:MouChengYuAnniversaryDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ConsumeMouChengYuAnniversaryShizha;}
internal sealed class ResolveMouChengYuAnniversaryGaojianDescriptor:MouChengYuAnniversaryDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouChengYuAnniversaryGaojian;}
internal sealed class MouChengYuAnniversaryHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IMouChengYuAnniversaryRuntime>().Resolve(f,op);}
