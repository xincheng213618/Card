using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record DiciAcceptedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int TargetSeat, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record DiciGiftEntityEvent(long FrameId, long LeaseId, int CardId, CardKind Kind, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record DiciRecoveryIssuedEvent(long FrameId, int TargetSeat) : IGameEvent;
public sealed record DiciUnlinkIssuedEvent(long FrameId, int TargetSeat) : IGameEvent;
public sealed record DiciNeighborsFrozenEvent(long FrameId, int Count) : IGameEvent;
public sealed record DiciNeighborFrozenEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record DiciNeighborIssuedEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record DiciLeaseIssuedEvent(long LeaseId, CardConversionSource Source, string GameplayHash, int IssuerSeat, int TargetSeat, int Ordinal, int ActualTurn) : IGameEvent;
public sealed record DiciLeaseDueEvent(long LeaseId, long ChangeId, int TargetSeat) : IGameEvent;
public sealed record DiciLeaseStartedEvent(long LeaseId, long ChangeId, long ProgramFrameId) : IGameEvent;
public sealed record DiciLeaseCancelledEvent(long LeaseId, int TargetSeat) : IGameEvent;
public sealed record DiciNextChoiceEvent(long FrameId, long LeaseId, string Branch) : IGameEvent;
public sealed record DiciThunderIssuedEvent(long FrameId, long LeaseId, int SourceSeat, int TargetSeat) : IGameEvent;
internal sealed class DiciGiftRecoveryAndNeighborChainDescriptor() : ZhaoZhongDescriptor(SkillProgramEffectOp.ResolveDiciGiftRecoveryAndNeighborChain)
{
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override ProgramOperationLegalityPolicy LegalityPolicy { get; } = new(RequiresTargetHand: true, ExcludesOwnerAsTarget: true);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class IssuedDiciNextChainChoiceDescriptor() : ZhaoZhongDescriptor(SkillProgramEffectOp.ResolveIssuedDiciNextChainChoice){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
