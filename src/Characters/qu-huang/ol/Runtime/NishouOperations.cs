using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record QuHuangNishouUseOrigin(long ProgramFrameId,long CardUseFrameId,long ActionId,CardConversionSource Source,string GameplayHash,int ActorSeat,int CardId,CardKind PrintedKind,Suit Suit,int Rank,long LossSequence);
public sealed record NishouEquipmentLostEvent(long BatchId,int OwnerSeat,int CardId,CardKind Kind,Suit PrintedSuit,int Rank,long Sequence,ActualDiscardRecoveryPhaseKey? Phase):IGameEvent;
public sealed record NishouSwapDebtIssuedEvent(long DebtId,CardConversionSource Source,string GameplayHash,ActualDiscardRecoveryPhaseKey Phase):IGameEvent;
public sealed record NishouSwapDebtConsumedEvent(long DebtId,long FrameId,long BoundaryId):IGameEvent;
public sealed record NishouOriginalHandFrozenEvent(long FrameId,int HolderSeat,int CardId,CardKind Kind):IGameEvent;
public sealed record NishouLightningPaidEvent(QuHuangNishouUseOrigin Origin,long Sequence):IGameEvent;
public sealed record NishouLightningReturnedEvent(QuHuangNishouUseOrigin Origin):IGameEvent;
internal sealed class ResolveNishouEquipmentLossChoiceDescriptor:QuHuangDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveNishouEquipmentLossChoice;}
internal sealed class ResolveNishouIssuedCurrentPhaseSwapDescriptor:QuHuangDescriptor {
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveNishouIssuedCurrentPhaseSwap;}
