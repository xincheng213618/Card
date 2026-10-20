using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record ZexingShownEvent(long FrameId, int OwnerSeat, int CardId, CardKind Kind, CardLocation From, Suit Suit) : IGameEvent;
public sealed record ZexingPeerChosenEvent(long FrameId, int OwnerSeat, int ActualTurn, int TurnOwner, int Peer, int Index) : IGameEvent;
public sealed record ZexingOptionChosenEvent(long FrameId, int Peer, int Option) : IGameEvent;
public sealed record ZexingHandViewedEvent(long FrameId, int ViewerSeat, int Peer, int Count) : IGameEvent;
public sealed record ZexingHpLossIssuedEvent(long FrameId, int Peer, int Before, int After) : IGameEvent;
internal sealed class ResolveZexingShownCardChainDescriptor() : TangTangDescriptor(SkillProgramEffectOp.ResolveZexingShownCardChain) {
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Activation;
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.TangTang is { Paid: true };
}
