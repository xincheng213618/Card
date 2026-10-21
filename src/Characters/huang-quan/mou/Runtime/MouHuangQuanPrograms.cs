using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouHqStage { Cards, Recipient, GiveChildren, Reply, ReplyChildren, Drawing, DrawChildren, Effect }
public sealed record MouHqEntity(int CardId,CardLocation From);
public sealed record MouHqGift(long Id,long ProducerId,int Giver,int Recipient,int Turn,int TurnOwner,long CompletionSequence)
{
    private IReadOnlyList<CardMovementRecord> _moves=[];
    public IReadOnlyList<CardMovementRecord> Movements{get=>_moves;init=>_moves=Array.AsReadOnly(value.ToArray());}
}
public sealed record MouHqReceipt(CardConversionSource Source,string GameplayHash,int Turn,int TurnOwner,long WindowId)
{
    public MouHqStage Stage{get;init;}
    public long UseId{get;init;}
    public long ActionId{get;init;}
    public bool Outgoing{get;init;}
    public bool Paid{get;init;}
    public int Recipient{get;init;}=-1;
    public int Count{get;init;}
    public long Before{get;init;}
    public long After{get;init;}
    public MouHqGift? Gift{get;init;}
    public NativeDrawOutcome? Draw{get;init;}
    private IReadOnlyList<MouHqEntity> _selected=[];
    public IReadOnlyList<MouHqEntity> Selected{get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouHqReceipt? MouHuangQuan{get;init;}}
public sealed record MouHqEffectGrant(long FrameId,CardConversionSource Source,string GameplayHash,long WindowId,long UseId,long ActionId,int Recipient,int GivenCount);
public sealed record MouHqEffectRounds(long ActionId,ExtraCardEffectState Native)
{
    private ExtraCardEffectState _native=Freeze(Native);
    public ExtraCardEffectState Native{get=>_native;init=>_native=Freeze(value);}
    private IReadOnlyList<MouHqEffectGrant> _grants=[];
    public IReadOnlyList<MouHqEffectGrant> Grants{get=>_grants;init=>_grants=Array.AsReadOnly(value.ToArray());}
    public int Started{get;init;}
    public bool Active{get;init;}
    private static ExtraCardEffectState Freeze(ExtraCardEffectState x)=>x with{
        Simple=x.Simple is{}s?s with{RecoveryPolicySources=s.RecoveryPolicySources is{}policies?Array.AsReadOnly(policies.ToArray()):null}:null,
        Fangtian=x.Fangtian is{}f?f with{TargetSeats=Array.AsReadOnly(f.TargetSeats.ToArray())}:null,
        OriginalAttacks=x.OriginalAttacks.Select(s=>s with{Attack=s.Attack with{
            PhysicalCardIds=Array.AsReadOnly(s.Attack.PhysicalCardIds.ToArray()),ChainedTargetSeats=Array.AsReadOnly(s.Attack.ChainedTargetSeats.ToArray()),
            ResponseProhibitingSkillNames=Array.AsReadOnly(s.Attack.ResponseProhibitingSkillNames.ToArray()),
            RangePreventionVisitedTargets=s.Attack.RangePreventionVisitedTargets is{}v?Array.AsReadOnly(v.ToArray()):null}}).ToArray(),MaterialClaims=x.MaterialClaims.ToArray()};
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouHqEffectRounds? MouHqRounds{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouHqEffectGrant? MouHqIneffective{get;init;}
}
public sealed partial class CharacterState
{internal IReadOnlyList<MouHqGift>? MouHqActualGifts{get;set;}}
// Private transferred entity ids remain on trusted movement batches and owning frames.
public sealed record NativeCardsGivenEvent(long Id,long ProducerId,int Giver,int Recipient,int Count,int Turn,int TurnOwner,long CompletionSequence):IGameEvent;
public sealed record MouHqStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Turn,int TurnOwner,long WindowId,long UseId,long ActionId):IGameEvent;
public sealed record MouHqCommittedEvent(long FrameId,int OwnerSeat,int Recipient,int Count,int Turn,int TurnOwner):IGameEvent;
public sealed record MouHqReplyEvent(long FrameId,int Recipient,int RequiredCount,bool Gave):IGameEvent;
public sealed record MouHqDrawReturnedEvent(long FrameId,int RequestedCount,int ActualCount,int ReplacementCount,long? AttemptId):IGameEvent;
public sealed record MouHqEffectIssuedEvent(MouHqEffectGrant Grant,bool Repeats):IGameEvent;
public sealed record MouHqEffectRoundStartedEvent(long UseId,long ActionId,long FrameId,int Round):IGameEvent;
public sealed record MouHqEffectRoundCompletedEvent(long UseId,long ActionId,long FrameId,int Round):IGameEvent;
public sealed record MouHqCompletedEvent(long FrameId):IGameEvent;
internal interface IMouHuangQuanHost{SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal sealed class MouHuangQuanHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<IMouHuangQuanHost>().Resolve(f,op);
}
internal abstract class MouHuangQuanDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler=>new MouHuangQuanHandler(Op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c)=>c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ResolveMouHuangQuanQiaoduiDescriptor:MouHuangQuanDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouHuangQuanQiaodui;}
internal sealed class ResolveMouHuangQuanTuichengDescriptor:MouHuangQuanDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouHuangQuanTuicheng;}
