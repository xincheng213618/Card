using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouHgStage { Reveal, RevealChildren, Choice, Target, NativeUse, NativeChildren, ObtainChildren, DiscardSlashChildren }
public sealed record MouHgReveal(int CardId,CardKind Kind,long Sequence);
public sealed record MouHgLiejiGrant(long FrameId,CardConversionSource Source,string GameplayHash,int Turn,int TurnOwner,int CardId);
public sealed record MouHgUseBonus(long UseId,long ActionId,MouHgLiejiGrant Grant);
public sealed record MouHgNativeUse(long FrameId,long UseId,long ActionId,int CardId,CardKind Kind,int Actor,int Target);
public sealed record MouHgLiejiGrantedEvent(long FrameId,CardConversionSource Source,int Turn,int TurnOwner):IGameEvent;
public sealed record MouHgBonusIssuedEvent(long UseId,long ActionId,long GrantFrameId,int CardId):IGameEvent;
public sealed record MouHgStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Turn,int TurnOwner):IGameEvent;
public sealed record MouHgRevealedEvent(long FrameId,int Index,int CardId,CardKind Kind,long Sequence):IGameEvent;
public sealed record MouHgMovedEvent(long FrameId,int CardId,CardLocation From,CardLocation To,long Sequence):IGameEvent;
public sealed record MouHgNativeIssuedEvent(MouHgNativeUse Origin):IGameEvent;
public sealed record MouHgNativeReturnedEvent(MouHgNativeUse Origin):IGameEvent;
public sealed record MouHgCompletedEvent(long FrameId,int RevealedCount):IGameEvent;
public sealed record MouHgReceipt(CardConversionSource Source,string GameplayHash,int Turn,int TurnOwner)
{
    public MouHgStage Stage{get;init;}
    public long Before{get;init;}
    public long After{get;init;}
    public bool NativeReturned{get;init;}
    public bool RevealExhausted{get;init;}
    public MouHgNativeUse? Native{get;init;}
    private IReadOnlyList<MouHgReveal> _reveals=[];
    public IReadOnlyList<MouHgReveal> Reveals{get=>_reveals;init=>_reveals=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouHgReceipt? MouHuangGai{get;init;}}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouHgNativeUse? MouHgQuzhou{get;init;}
    private IReadOnlyList<MouHgUseBonus>? _mouHgBonuses;
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public IReadOnlyList<MouHgUseBonus>? MouHgBonuses{get=>_mouHgBonuses;init=>_mouHgBonuses=value is null?null:Array.AsReadOnly(value.ToArray());}
}
public sealed partial class CharacterState
{internal IReadOnlyList<MouHgLiejiGrant>? MouHgLiejiGrants{get;set;}}
internal sealed record MouHgUseFacts(long UseId,long ActionId,IReadOnlyList<int> Materials,IReadOnlyList<MouHgUseBonus>? Bonuses);
internal interface IMouHuangGaiRuntime : IProgramRuntimeOperations
{SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,SkillProgramEffectOp op);}
internal interface IMouHuangGaiFacts : IProgramSelectionQuery
{
    int Turn{get;} int TurnOwner{get;} long Sequence{get;} bool Finished{get;} int LivingCount{get;}
    bool Alive(int seat); bool Human(int seat); bool OwnPlay(int seat); bool LiejiParent(ProgramSkillFrame frame); bool HasNativeUse(long id); bool SameChoice(PromptChoice left,PromptChoice right);
    string Name(int seat); SkillProgram? Program(string skill); SkillPromptPresentation Presentation(string skill); double TargetScore(int owner,int target);
    IReadOnlyList<int> Seats(); IReadOnlyList<int> DamageHand(int owner); IReadOnlyList<int> Targets(ProgramSkillFrame frame); IReadOnlyList<int> ProcessingReveals(ProgramSkillFrame frame);
    IReadOnlyList<MouHgLiejiGrant> Grants(int owner); MouHgUseFacts? Use(long id);
    IEnumerable<T> History<T>() where T:IGameEvent;
}
internal interface IMouHuangGaiNative : IProgramRuntimeOperations
{
    void SetGrants(int owner,IReadOnlyList<MouHgLiejiGrant>? grants); void SetUseBonuses(long useId,IReadOnlyList<MouHgUseBonus> bonuses);
    MouHgReveal? Reveal(ProgramSkillFrame frame,string binding); void Obtain(ProgramSkillFrame frame,IReadOnlyList<int> ids); void DiscardSlash(ProgramSkillFrame frame); void Slash(ProgramSkillFrame frame,int target);
    bool Drain(long frameId); void Publish(); void Finish(ProgramSkillFrame frame);
}
internal sealed class MouHuangGaiHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<IMouHuangGaiRuntime>().Resolve(f,op);
}
internal abstract class MouHuangGaiDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler=>new MouHuangGaiHandler(Op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(4));
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c)=>c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ArmMouHuangGaiLiejiDescriptor:MouHuangGaiDescriptor
{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ArmMouHuangGaiLieji;}
internal sealed class ResolveMouHuangGaiQuzhouDescriptor:MouHuangGaiDescriptor
{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouHuangGaiQuzhou;}
