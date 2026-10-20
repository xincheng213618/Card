using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record MiuyanUseInvoice(long UseId,long ActionId,CardConversionSource Source,string GameplayHash,
    int ActualTurn,int TurnOwner,int RoundNumber,ActualDiscardRecoveryPhaseKey Phase,SkillPolarity PreviousState,SkillPolarity CurrentState,
    int OriginalActor,int ProviderSeat,CardActionCost Material,long BeforePayment);
public sealed record MiuyanUseIssuedEvent(MiuyanUseInvoice Invoice):IGameEvent;
public sealed record MiuyanPhaseHandRevealedEvent(ActualDiscardRecoveryPhaseKey Phase,long RevealFrameId,int CardId,int HolderSeat,long Entry,string RevealKind):IGameEvent;
public sealed record MiuyanRoundDisabledEvent(long ProgramFrameId,long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int RoundNumber):IGameEvent;
public sealed record MiuyanClaimEntityFrozenEvent(long FrameId,int Index,WangGuanHandEntity Entity):IGameEvent;
public sealed record MiuyanEntityObtainedEvent(long FrameId,int Index,WangGuanHandEntity Entity,long MovementSequence):IGameEvent;
internal sealed class ResolveMiuyanCompletedUseDescriptor:WangGuanDescriptor
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMiuyanCompletedUse; public override bool UsesConversionPolarity=>true;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => true;
}
