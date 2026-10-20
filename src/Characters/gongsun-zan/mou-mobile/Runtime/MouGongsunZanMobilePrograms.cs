using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouGongsunZanMobileStage { Choice, MovementChildren, Drawing, DrawChildren }
public sealed record MouGongsunZanMobileReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    long WindowId, int Round, long DamageFrameId, int TargetSeat)
{
    public MouGongsunZanMobileStage Stage { get; init; }
    public bool Accepted { get; init; }
    public int Charge { get; init; }
    public bool Offensive { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouGongsunZanMobileReceipt? MouGongsunZanMobile { get; init; } }
public sealed record MouGongsunZanMobileInitializedEvent(long FrameId,int OwnerSeat,CardConversionSource Source,int Charge):IGameEvent;
public sealed record MouGongsunZanMobileChargeEvent(long FrameId,int OwnerSeat,int Before,int After):IGameEvent;
public sealed record MouGongsunZanMobileStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,long WindowId,int Round,long DamageFrameId,int TargetSeat):IGameEvent;
public sealed record MouGongsunZanMobileDistanceEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Round,bool Offensive,int Charge):IGameEvent;
public sealed record MouGongsunZanMobileStoredEvent(long FrameId,int CardId,CardKind Kind,CardLocation From,CardLocation To,int Sequence):IGameEvent;
public sealed record MouGongsunZanMobileDiscardedEvent(long FrameId,int CardId,CardKind Kind,CardLocation From,CardLocation To,int Sequence):IGameEvent;
public sealed record MouGongsunZanMobileDrawReturnedEvent(long FrameId,int Requested,int Actual,int Replacement,long? AttemptFrameId):IGameEvent;
public sealed record MouGongsunZanMobileCompletedEvent(long FrameId,SkillProgramEffectOp Op):IGameEvent;
internal interface IMouGongsunZanMobileHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,SkillProgramEffectOp op); }
internal sealed class MouGongsunZanMobileHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{ public SkillProgramEffectOp Op=>op; public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IMouGongsunZanMobileHost>().Resolve(f,op); }
internal abstract class MouGongsunZanMobileDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction=>Op==SkillProgramEffectOp.InitializeMouGongsunZanMobile?ProgramOperationInteraction.Automatic:ProgramOperationInteraction.Choice;
    public override ProgramFactRequirements FactRequirements=>new(Triggered:ProgramTrackedFact.NativeLogicalDiscard);
    public override ISkillProgramEffectHandler Handler=>new MouGongsunZanMobileHandler(Op);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c)=>c.State.CanContinueAfterOwnerDeath(c.Frame);
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(Op switch
    { SkillProgramEffectOp.InitializeMouGongsunZanMobile=>SkillProgramTriggerWindow.GameStarting,SkillProgramEffectOp.ResolveMouGongsunZanYicong=>SkillProgramTriggerWindow.RoundStarting,SkillProgramEffectOp.ExpireMouGongsunZanRound=>SkillProgramTriggerWindow.RoundEnding,_=>SkillProgramTriggerWindow.AfterDamageApplied})];
}
internal sealed class InitializeMouGongsunZanMobileDescriptor:MouGongsunZanMobileDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.InitializeMouGongsunZanMobile;}
internal sealed class ResolveMouGongsunZanYicongDescriptor:MouGongsunZanMobileDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouGongsunZanYicong;}
internal sealed class ResolveMouGongsunZanQiaomengDescriptor:MouGongsunZanMobileDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouGongsunZanQiaomeng;}
internal sealed class ExpireMouGongsunZanRoundDescriptor:MouGongsunZanMobileDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ExpireMouGongsunZanRound;}
