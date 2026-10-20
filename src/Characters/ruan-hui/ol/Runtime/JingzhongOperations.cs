using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record JingzhongDiscardEvent(long PhaseToken, int OwnerSeat, long MovementSequence, int CardId) : IGameEvent;
public sealed record JingzhongLeaseEvent(long FrameId, CardConversionSource Source, string GameplayHash, int RecipientSeat, long AfterPhaseToken, int ActualTurn) : IGameEvent;
public sealed record JingzhongUseEntityEvent(long PhaseToken, long ActionId, long UseId, int OriginalActor, bool Response, int Index, int MaterialCount, int CardId, CardKind PhysicalKind, CardLocation OriginalFrom, long CapturedAfterSequence, long? PaidEntrySequence) : IGameEvent;
public sealed record JingzhongSettledEntityEvent(long LeaseId, long PhaseToken, long ActionId, long BatchId, int CardId, long MovementSequence) : IGameEvent;
public sealed record JingzhongClaimIssuedEvent(long FrameId, long LeaseId, long PhaseToken, long ActionId, long BatchId, int EntityCount) : IGameEvent;
internal sealed class ArmJingzhongNextActualPlayDescriptor : RuanHuiDescriptor {
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.ArmJingzhongNextActualPlay; }
internal sealed class ObtainJingzhongUsedEntitiesDescriptor : RuanHuiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainJingzhongUsedEntities; }
