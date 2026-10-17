using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum WeijieIntent { Dodge,Duel,Group,FactionDodge,FactionSlashPlayed,FactionSlashUse,BorrowedSword,Qinglong,ProgramSlash,ProgramNearestSlash,AssistedSlash,NearestLegalSlash,FixedSlash,GenericBasic,Dying,WenyiPeach=28101 }
public enum WeijieStage { Declare,Victim,Card,CostChildren }
public sealed record WeijieNeed(WeijieIntent Intent,long OwnerFrameId,long RequestFrameId,long? ParentActionId,int ProviderSeat,int ActorSeat,int? RequesterSeat,int? OpponentSeat,int Cursor,int TargetIndex,int EffectRound,int InstructionIndex,SkillProgramEffectOp? RequestOp,IReadOnlyList<int> Targets)
{
    private IReadOnlyList<int> _targets=Array.AsReadOnly(Targets.ToArray());
    public IReadOnlyList<int> Targets { get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray()); }
    public bool Equals(WeijieNeed? other)=>other is not null&&Intent==other.Intent&&OwnerFrameId==other.OwnerFrameId&&RequestFrameId==other.RequestFrameId&&ParentActionId==other.ParentActionId&&ProviderSeat==other.ProviderSeat&&ActorSeat==other.ActorSeat&&RequesterSeat==other.RequesterSeat&&OpponentSeat==other.OpponentSeat&&Cursor==other.Cursor&&TargetIndex==other.TargetIndex&&EffectRound==other.EffectRound&&InstructionIndex==other.InstructionIndex&&RequestOp==other.RequestOp&&Targets.SequenceEqual(other.Targets);
    public override int GetHashCode()=>HashCode.Combine(Intent,OwnerFrameId,RequestFrameId,ProviderSeat,ActorSeat,Cursor,InstructionIndex,Targets.Count);
}
public sealed record WeijiePaidOrigin(long AttemptId,CardConversionSource Source,string GameplayHash,int ActualTurn,int ActualTurnOwner,WeijieNeed Need,CardKind OutputKind,int TargetSeat,int VictimSeat,int CardId,CardKind PrintedKind,CardKind EffectiveName,CardLocation From,long PaymentSequence,long PaymentBatchId);
public sealed record WeijieReceipt(CardConversionSource Source,string GameplayHash,int ActualTurn,int ActualTurnOwner,WeijieNeed Need,PendingDecision OriginalDecision,WeijieStage Stage,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]CardKind? OutputKind=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]int? TargetSeat=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]int? VictimSeat=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]WeijiePaidOrigin? Paid=null,long Before=0,long After=0)
{
    private PendingDecision _original=RequestedDeckBasicFrame.FreezeDecision(OriginalDecision);
    public PendingDecision OriginalDecision {get=>_original;init=>_original=RequestedDeckBasicFrame.FreezeDecision(value);}
}
public sealed record WeijieStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,int ActualTurnOwner,WeijieIntent Intent,long OwnerFrameId,long RequestFrameId,PromptId OriginalPromptId,long OriginalRevision,string OriginalDecisionHash):IGameEvent;
public sealed record WeijieDeclaredEvent(long FrameId,CardKind OutputKind,int TargetSeat):IGameEvent;
public sealed record WeijieVictimSelectedEvent(long FrameId,int VictimSeat):IGameEvent;
public sealed record WeijieCostPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,int ActualTurnOwner,int VictimSeat,int CardId,CardKind PrintedKind,CardKind EffectiveName,CardLocation From,long Sequence,long BatchId):IGameEvent;
public sealed record WeijieAttemptResolvedEvent(long FrameId,bool Matched,bool NativeLegal):IGameEvent;
public sealed record WeijieUseIssuedEvent(long AttemptId,long UseId,long ActionId,int ActorSeat,int ProviderSeat,int? RequesterSeat,CardKind OutputKind):IGameEvent;
public sealed record WeijieResponseIssuedEvent(long AttemptId,long OwnerFrameId,long ActionId,int ActorSeat,int ProviderSeat,int? RequesterSeat,CardKind OutputKind,ProgramCardContinuation Continuation):IGameEvent;
public sealed record WeijieUseReturnedEvent(long AttemptId,long UseId,long ActionId):IGameEvent;
public sealed record WeijieProgramReturn(WeijiePaidOrigin Origin,long UseId,long ActionId,bool Returned=false);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public WeijieReceipt? Weijie {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public WeijieProgramReturn? WeijieReturn {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public WeijiePaidOrigin? WeijieResponseOrigin {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public WeijiePaidOrigin? WeijieKeAcceptedOrigin {get;init;}
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public WeijiePaidOrigin? WeijieOrigin {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public WeijiePaidOrigin? WeijieResponseOrigin {get;init;}
}
internal interface IWeijieHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame frame);}
internal sealed class ResolveWeijieRequiredBasicRequestDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveWeijieRequiredBasicRequest;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler {get;}=new WeijieHandler();
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(1));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
}
internal sealed class WeijieHandler:ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveWeijieRequiredBasicRequest;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>host.GetCapability<IWeijieHost>().Resolve(f);
}
internal static class GuoTuWeijieComposition
{
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(!p.Activations.Any(a=>a.Effects.Any(e=>e.Op==SkillProgramEffectOp.ResolveWeijieRequiredBasicRequest))&&!p.Triggers.Any(t=>t.Effects.Any(e=>e.Op==SkillProgramEffectOp.ResolveWeijieRequiredBasicRequest)))return;
        if(p.Id!="ol:weijie"||p.Triggers.Count!=0||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.HpSkillGrants.Count!=0||p.Activations is not[var a]||a.Id!="native-basic-request"||a.MinCards!=0||a.MaxCards!=0||a.MinTargets!=0||a.MaxTargets!=0||a.UsesPerTurn is not null||a.Effects is not[{Op:SkillProgramEffectOp.ResolveWeijieRequiredBasicRequest,Target:SkillProgramEffectTarget.Owner}e]||e.Condition.Kind!=SkillProgramConditionKind.Always)throw new InvalidOperationException(path+": Weijie requires its complete native-only zero-selection request activation.");
    }
}
