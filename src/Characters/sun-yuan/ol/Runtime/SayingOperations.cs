using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record SayingSelection(int ActorSeat,int ProviderSeat,int TargetSeat,CardKind Kind)
{
    private IReadOnlyList<SunYuanEntity> _materials=[];private IReadOnlyList<CardConversionSource> _sources=[];
    public IReadOnlyList<SunYuanEntity> Materials{get=>_materials;init=>_materials=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<CardConversionSource> Sources{get=>_sources;init=>_sources=Array.AsReadOnly(value.ToArray());}
    public bool Equals(SayingSelection? other)=>other is not null&&ActorSeat==other.ActorSeat&&ProviderSeat==other.ProviderSeat&&TargetSeat==other.TargetSeat&&Kind==other.Kind&&Materials.SequenceEqual(other.Materials)&&Sources.SequenceEqual(other.Sources);
    public override int GetHashCode()=>HashCode.Combine(ActorSeat,ProviderSeat,TargetSeat,Kind,Materials.Count,Sources.Count);
}
public sealed record SayingNativeOrigin(long ProgramFrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,long UseId,long ActionId,SayingSelection Selection,CardActionContext OriginalAction,long BeforePayment);
public sealed record SayingRangeGrantedEvent(long FrameId,CardConversionSource Source,int Round,int OwnerSeat,int Holder):IGameEvent;
public sealed record SayingRequestIssuedEvent(long FrameId,int ActorSeat,int TargetSeat):IGameEvent;
public sealed record SayingFactionRequestedEvent(long FrameId,int ActorSeat,int TargetSeat,FactionResponsePolicySource Source):IGameEvent;
public sealed record SayingSlashPreparedEvent(long FrameId,int ActorSeat,int ProviderSeat,int TargetSeat,CardKind Kind,string SelectionHash):IGameEvent;
public sealed record SayingSlashIssuedEvent(long FrameId,long UseId,long ActionId,string OriginalActionHash):IGameEvent;
public sealed record SayingSlashMaterialPaidEvent(long UseId,long ActionId,int CardId,CardLocation From,CardLocation To,long Sequence):IGameEvent;
public sealed record SayingSlashReturnedEvent(long FrameId,long UseId,long ActionId):IGameEvent;
internal sealed class ResolveSayingPlayStartObtainAndReplyDescriptor():SunYuanDescriptor(SkillProgramEffectOp.ResolveSayingPlayStartObtainAndReply,SkillProgramTriggerWindow.PlayPhaseStarting) {
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.SunYuan is { Paid: true };
}
