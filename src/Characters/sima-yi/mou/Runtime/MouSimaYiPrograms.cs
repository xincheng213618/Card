using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouSmStage { Polarity, Target, Give, GiveChildren, Respondent, PlayedAccepted, PlayedCostChildren, PlayedCompleted, PlayedChildren, Disclosure, Drawing, DrawChildren, SpreadTargets, DamageChildren, PlayedDeclaration }
public enum MouSmPrivateCall { CommittedSlash, TargetSlash }
public sealed record MouSmEntity(int Id, CardKind Kind, CardLocation From, string Label);
public sealed record MouSmPlayedPlan(CardKind Kind, CardConversionSource? Source, bool Zhangba)
{
    private IReadOnlyList<int> _cards=[];
    public IReadOnlyList<int> Cards { get=>_cards; init=>_cards=Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouSmReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn,int TurnOwner,int PhaseId,long ParentId)
{
    public MouSmStage Stage {get;init;} public int Target {get;init;}=-1; public int Cursor {get;init;} public int Actor {get;init;}=-1;
    public bool Yin {get;init;} public bool Reward {get;init;} public long DebtId {get;init;} public long UseId {get;init;} public long ActionId {get;init;}
    public long ResponseActionId {get;init;} public long Before {get;init;} public long After {get;init;} public NativeDrawOutcome? Draw {get;init;}
    public MouSmPlayedPlan? Played {get;init;} public CardActionContext? Response {get;init;}
    private IReadOnlyList<int> _targets=[],_respondents=[],_playedSeats=[]; private IReadOnlyList<MouSmEntity> _materials=[];
    public IReadOnlyList<int> Targets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> Respondents {get=>_respondents;init=>_respondents=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> PlayedSeats {get=>_playedSeats;init=>_playedSeats=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<MouSmEntity> Materials {get=>_materials;init=>_materials=Array.AsReadOnly(value.ToArray());}
}
public sealed record MouSmSecretLease(CardConversionSource Source,string GameplayHash,long ActionId,long EventStart,long LogStart)
{
    public long WindowId {get;init;} public MouSmPrivateCall Call {get;init;} public bool Started {get;init;} public bool Released {get;init;}
    private IReadOnlyList<long> _events=[],_logs=[];
    public IReadOnlyList<long> EventSequences {get=>_events;init=>_events=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<long> LogSequences {get=>_logs;init=>_logs=Array.AsReadOnly(value.ToArray());}
}
public sealed record MouSmDeclarationSecret(CardConversionSource Source,string GameplayHash);
public sealed partial record DeclaredCardPayment { [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public MouSmDeclarationSecret? MouSimaYiSecret {get;init;} }
public sealed record MouSmSecretDeclarationRecordedEvent(long DeclarationId,int Actor):IGameEvent;
public sealed record MouSmPrivateReturn(long UseId,long ActionId,MouSmPrivateCall Call);
public sealed record MouSmPlayedReturn(long FrameId,long ActionId,SkillProgramTriggerWindow Window);
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public MouSmReceipt? MouSimaYi {get;init;} }
public sealed partial record CardUseFrame { [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public MouSmSecretLease? MouSimaYiSecret {get;init;} }
public sealed partial record ProgramCardTriggerWindowFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public MouSmPrivateReturn? MouSimaYiPrivate {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public MouSmPlayedReturn? MouSimaYiPlayed {get;init;}
}
public sealed record MouSmStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int Turn,int TurnOwner,int PhaseId,long ParentId,long UseId,long ActionId):IGameEvent;
public sealed record MouSmPolarityEvent(long FrameId,CardConversionSource Source,bool Yin):IGameEvent;
public sealed record MouSmDebtIssuedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Turn,int TurnOwner,int PhaseId,int Target,bool Yin):IGameEvent;
public sealed record MouSmDebtConsumedEvent(long DebtId,long FrameId,long DamageParent):IGameEvent;
public sealed record MouSmGivePaidEvent(long FrameId,int Giver,int CardId,long Sequence):IGameEvent;
public sealed record MouSmPlayedEvent(long FrameId,int Cursor,int Actor,long ActionId,bool Completed):IGameEvent;
public sealed record MouSmReplyEvent(long FrameId,int Cursor,int Actor,bool Played):IGameEvent;
public sealed record MouSmSlashAllowanceEvent(long FrameId,CardConversionSource Source,int Turn,int TurnOwner,int PhaseId):IGameEvent;
public sealed record MouSmDamageIssuedEvent(long FrameId,int Cursor,int Target):IGameEvent;
public sealed record MouSmDeclarationReturnedEvent(long FrameId,int Cursor,long DeclarationId,bool Succeeded):IGameEvent;
public sealed record MouSmCompletedEvent(long FrameId):IGameEvent;
public sealed record MouSmTargetsDisclosedEvent(long UseId,long ActionId,int Actor,int CardId,CardKind Kind):IGameEvent
{
    private IReadOnlyList<int> _targets=[]; private IReadOnlyList<long> _events=[],_logs=[];
    public IReadOnlyList<int> Targets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<long> HeldEventSequences {get=>_events;init=>_events=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<long> HeldLogSequences {get=>_logs;init=>_logs=Array.AsReadOnly(value.ToArray());}
}
internal interface IMouSimaYiRuntime:IProgramRuntimeOperations { SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op); }
internal abstract class MouSmDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler=>new MouSmHandler(Op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements=>new(Triggered:ProgramTrackedFact.NativeActionAppearance|ProgramTrackedFact.ActualDiscardRecoveryPhase,Activated:ProgramTrackedFact.NativeActionAppearance|ProgramTrackedFact.ActualDiscardRecoveryPhase);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c)=>c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class InitializeMouSimaYiQuanmouDescriptor:MouSmDescriptor { public override SkillProgramEffectOp Op=>SkillProgramEffectOp.InitializeMouSimaYiQuanmou; }
internal sealed class ResolveMouSimaYiQuanmouDescriptor:MouSmDescriptor { public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouSimaYiQuanmou; }
internal sealed class ResolveMouSimaYiPingliaoDescriptor:MouSmDescriptor { public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouSimaYiPingliao; }
internal sealed class PreventMouSimaYiQuanmouDamageDescriptor:MouSmDescriptor { public override SkillProgramEffectOp Op=>SkillProgramEffectOp.PreventMouSimaYiQuanmouDamage; }
internal sealed class SpreadMouSimaYiQuanmouDamageDescriptor:MouSmDescriptor { public override SkillProgramEffectOp Op=>SkillProgramEffectOp.SpreadMouSimaYiQuanmouDamage; }
internal sealed class MouSmHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<IMouSimaYiRuntime>().Resolve(f,op);
}
