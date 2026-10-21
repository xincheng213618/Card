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
internal interface IMouHuangGaiHost
{SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,SkillProgramEffectOp op);}
internal sealed class MouHuangGaiHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<IMouHuangGaiHost>().Resolve(f,op);
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
