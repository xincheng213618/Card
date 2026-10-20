using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ZexingVisited(int owner, int peer) => ProgramEventHistory<ZexingPeerChosenEvent>().Any(e => e.OwnerSeat == owner && e.Peer == peer && e.ActualTurn == _turnNumber && e.TurnOwner == _turnProgression.OwnerSeat);
    private bool ZexingShownStillOwned(ProgramSkillFrame f) => f.TangTang?.Shown is { } card && TangTangTransferCards(f.OwnerSeat, f.OwnerSeat).Any(c => c.Id == card.CardId);
    private IReadOnlyList<TangTangEntity> ZexingTakeCandidates(ProgramSkillFrame f) => f.TangTang is { Shown: { } shown, Peer: >= 0 } r ?
        TangTangTransferCards(f.OwnerSeat, r.Peer).Where(c => GetProgramEffectiveSuit(_players[r.Peer], c) != shown.Suit).Select(c => TangTangEntityAt(r.Peer, c.Id)).ToArray() : [];
    private bool ZexingTakeAllowed(ProgramSkillFrame f) => f.TangTang is { Peer: >= 0 } r && _players[f.OwnerSeat].IsAlive && _players[r.Peer].IsAlive && GetHand(_players[r.Peer]).Count >= 2 && ZexingTakeCandidates(f).Count >= 2;
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 27530, SkillProgramEffectOp.ResolveZexingShownCardChain)]
    private bool CanActivateZexing(ProgramActivationLegalityContext c) => c.Owner.IsAlive && _phase == TurnPhase.Play && c.OwnerSeat == _currentSeat &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && phase.ActorSeat == c.OwnerSeat && IsRecordedActualDiscardRecoveryPhase(phase) &&
        TangTangTransferCards(c.OwnerSeat, c.OwnerSeat).Count > 0 && _players.Any(p => p.IsAlive && p.Seat != c.OwnerSeat && !ZexingVisited(c.OwnerSeat, p.Seat));
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 27530)]
    private void CaptureZexingHpLoss(ProgramSkillHpLostEvent e)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { TangTang: { Stage: TangTangStage.HpIssuing, Paid: true } r } f || e.FrameId != f.Id || e.SkillId != f.SkillId || e.TargetSeat != r.Peer || e.Amount != 1) return;
        ReplaceRuntimeTop(f with { TangTang = r with { Stage = TangTangStage.HpChildren } });
        AdvanceEventRulesAndQueueFact(new ZexingHpLossIssuedEvent(f.Id, r.Peer, e.RemainingHp + e.Amount, e.RemainingHp));
    }
}
