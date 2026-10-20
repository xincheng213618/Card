namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsIssuedDiciChainTrigger(SkillProgramTrigger t) => t.Effects is [{ Op: SkillProgramEffectOp.ResolveIssuedDiciNextChainChoice }];
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 28111, SkillProgramEffectOp.ResolveDiciGiftRecoveryAndNeighborChain)]
    private bool CanActivateDici(ProgramActivationLegalityContext c) => _phase == TurnPhase.Play && _currentSeat == c.OwnerSeat && c.Owner.IsAlive && CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase &&
        phase.ActorSeat == c.OwnerSeat && !ProgramEventHistory<DiciAcceptedEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat && e.Phase == phase) && _players.Any(p => p.IsAlive && p.Seat != c.OwnerSeat && GetHand(p).Count > 0);
    private bool DiciLeaseSpent(long id) => ProgramEventHistory<DiciLeaseStartedEvent>().Any(e => e.LeaseId == id) || ProgramEventHistory<DiciLeaseCancelledEvent>().Any(e => e.LeaseId == id);
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 28112)]
    private void ObserveDiciEntry(IGameEvent payload)
    {
        if (!HasZhaoZhongCapability || payload is not CharacterStateChangedEvent { Change.Window: SkillProgramTriggerWindow.CharacterEnteredChain } entered) return;
        foreach (var lease in ProgramEventHistory<DiciLeaseIssuedEvent>().Where(e => e.TargetSeat == entered.Change.TargetSeat && !DiciLeaseSpent(e.LeaseId) && !ProgramEventHistory<DiciLeaseDueEvent>().Any(d => d.LeaseId == e.LeaseId)).OrderBy(e => e.Ordinal).ToArray())
            AdvanceEventRulesAndQueueFact(new DiciLeaseDueEvent(lease.LeaseId, entered.Change.Id, lease.TargetSeat));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 28115)]
    private void CancelDiciDeadTarget(PlayerDiedEvent died)
    {
        if (!HasZhaoZhongCapability) return;
        foreach (var lease in ProgramEventHistory<DiciLeaseIssuedEvent>().Where(e => e.TargetSeat == died.VictimSeat && !DiciLeaseSpent(e.LeaseId)).ToArray()) AdvanceEventRulesAndQueueFact(new DiciLeaseCancelledEvent(lease.LeaseId, lease.TargetSeat));
    }
    private IReadOnlyList<ProgramTriggerCandidate> CollectIssuedDiciChainCandidates(CharacterStateChangeContext change)
    {
        if (change.Window != SkillProgramTriggerWindow.CharacterEnteredChain || !_players[change.TargetSeat].IsAlive) return [];
        return Array.AsReadOnly(ProgramEventHistory<DiciLeaseIssuedEvent>().Where(e => e.TargetSeat == change.TargetSeat && !DiciLeaseSpent(e.LeaseId) && ProgramEventHistory<DiciLeaseDueEvent>().Any(d => d.LeaseId == e.LeaseId && d.ChangeId == change.Id))
            .OrderBy(e => e.Ordinal).Select(e => new ProgramTriggerCandidate(e.IssuerSeat, e.Source.SkillId, "issued-next-chain", e.Source.SkillInstanceId, e.GameplayHash, 0, e.Ordinal)).ToArray());
    }
    private DiciLeaseIssuedEvent? DiciCandidateLease(ProgramTriggerCandidate c) => ProgramEventHistory<DiciLeaseIssuedEvent>().SingleOrDefault(e => e.Ordinal == c.OccurrenceIndex && e.IssuerSeat == c.OwnerSeat && e.Source.SkillId == c.SkillId && e.Source.SkillInstanceId == c.SkillInstanceId && e.GameplayHash == c.GameplayHash);
    private ProgramSkillWindowContext CreateIssuedDiciChainContext(ProgramLifecycleTriggerWindowFrame w, ProgramTriggerCandidate c, ProgramSkillWindowContext ordinary)
    {
        var lease = DiciCandidateLease(c) ?? throw new InvalidOperationException("Dici lost its original issued chain lease.");
        return new(SkillProgramTriggerWindow.CharacterEnteredChain, w.Id, c.OwnerSeat, SourceSeat: lease.IssuerSeat, TargetSeat: lease.TargetSeat, OccurrenceIndex: c.OccurrenceIndex, Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]));
    }
    private bool IsIssuedDiciChainCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        c.BindingId == "issued-next-chain" && IsIssuedDiciChainTrigger(GetProgramTrigger(c)) && DiciCandidateLease(c) is { } lease && !DiciLeaseSpent(lease.LeaseId) && _players[lease.TargetSeat].IsAlive &&
        context.Window == SkillProgramTriggerWindow.CharacterEnteredChain && context.OwnerSeat == c.OwnerSeat && context.TargetSeat == lease.TargetSeat && context.SourceSeat == lease.IssuerSeat && context.OccurrenceIndex == lease.Ordinal &&
        _resolutionStack.SingleOrDefault(x => x.Id == context.ParentFrameId) is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.CharacterEnteredChain } w && w.OwnerSeat == lease.TargetSeat && w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && w.Candidates[w.CandidateIndex] == c &&
        ProgramEventHistory<DiciLeaseDueEvent>().Count(e => e.LeaseId == lease.LeaseId && e.ChangeId == w.Id && e.TargetSeat == lease.TargetSeat) == 1 && ProgramEventHistory<CharacterStateChangedEvent>().Count(e => e.Change.Id == w.Id && e.Change.Window == SkillProgramTriggerWindow.CharacterEnteredChain && e.Change.TargetSeat == lease.TargetSeat) == 1;
}
