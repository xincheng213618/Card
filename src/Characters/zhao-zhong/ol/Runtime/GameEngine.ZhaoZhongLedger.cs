namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasZhaoZhongCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveDiciGiftRecoveryAndNeighborChain) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolvePengbiFirstActualTurn) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveIssuedDiciNextChainChoice) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveYintianFirstRecovery) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveBiriFirstNonDrawGain);
    private static CardConversionSource ZhaoZhongSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool ZhaoZhongActualTurnOpen => _turnNumber > 0 && _turnNumber == _turnProgression.TurnNumber && !ProgramEventHistory<ActualTurnCursorReturnedEvent>().Any(e => e.ActualTurn == _turnNumber && e.OwnerSeat == _turnProgression.OwnerSeat);
    private bool IsPengbiTrigger(SkillProgramTrigger t) => t.Effects is [{ Op: SkillProgramEffectOp.ResolvePengbiFirstActualTurn }];
    private bool IsBiriTrigger(SkillProgramTrigger t) => t.Effects is [{ Op: SkillProgramEffectOp.ResolveBiriFirstNonDrawGain }];
    private bool IsIssuedDiciChainTrigger(SkillProgramTrigger t) => t.Effects is [{ Op: SkillProgramEffectOp.ResolveIssuedDiciNextChainChoice }];
    private int? FirstPengbiActualTurn(int owner) => ProgramEventHistory<ActualDiscardRecoveryPhaseStartedEvent>()
        .Where(e => e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Preparation && e.Phase.ActorSeat == owner && e.Phase.ActualTurnOwnerSeat == owner)
        .Select(e => (int?)e.Phase.TurnNumber).Distinct().Order().FirstOrDefault();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 28110)]
    private bool CanOfferZhaoZhong(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context)
    {
        if (IsPengbiTrigger(t)) return c.OwnerSeat == _turnProgression.OwnerSeat && context.OwnerSeat == c.OwnerSeat && context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
            FirstPengbiActualTurn(c.OwnerSeat) == _turnNumber && !ProgramEventHistory<PengbiCommittedEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat);
        if (IsBiriTrigger(t)) return IsIssuedBiriCandidate(c, context);
        if (IsIssuedDiciChainTrigger(t)) return IsIssuedDiciChainCandidate(c, context);
        return true;
    }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 28111, SkillProgramEffectOp.ResolveDiciGiftRecoveryAndNeighborChain)]
    private bool CanActivateDici(ProgramActivationLegalityContext c) => _phase == TurnPhase.Play && _currentSeat == c.OwnerSeat && c.Owner.IsAlive && CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase &&
        phase.ActorSeat == c.OwnerSeat && !ProgramEventHistory<DiciAcceptedEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat && e.Phase == phase) && _players.Any(p => p.IsAlive && p.Seat != c.OwnerSeat && GetHand(p).Count > 0);
    private void CaptureBiriPotentialGainBatch(CardMovementBatchContext batch)
    {
        if (!HasZhaoZhongCapability || !ZhaoZhongActualTurnOpen || batch.TurnNumber != _turnNumber) return;
        var gains = batch.Movements.Where(m => m.To.Zone == CardZoneKind.Hand && m.To.OwnerSeat is { } owner && IsValidPlayerSeat(owner) && ActualHandGainOriginalSource(m, batch) != CardLocation.Hand(owner))
            .GroupBy(m => (m.CardId, m.To.OwnerSeat)).Select(g => g.OrderBy(m => m.Sequence).Last()).OrderBy(m => m.Sequence).ToArray();
        if (gains.Length == 0) return;
        foreach (var m in gains) AdvanceEventRulesAndQueueFact(new BiriPotentialGainEvent(batch.Id, m.Sequence, m.CardId, m.To.OwnerSeat!.Value, batch.TurnNumber, _turnProgression.OwnerSeat));
        foreach (var owner in _players.Where(p => p.IsAlive).OrderBy(p => (p.Seat - _turnProgression.OwnerSeat + _players.Count) % _players.Count))
        {
            var source = GetSkillBindingShard(owner).ProgramInstances.SelectMany(i => i.Program.Triggers.Where(IsBiriTrigger).Select(t => (Source: new CardConversionSource(i.SkillId, t.Id, owner.Seat, i.SkillInstanceId), Hash: i.Program.GameplayHash)))
                .OrderBy(s => s.Source.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault();
            if (source.Source is not null) AdvanceEventRulesAndQueueFact(new BiriPotentialHolderEvent(batch.Id, source.Source, source.Hash));
        }
    }
    private BiriPotentialGainEvent? FirstBiriGain(int turn, int owner) => ProgramEventHistory<BiriPotentialGainEvent>().Where(e => e.ActualTurn == turn && e.TurnOwner == owner).OrderBy(e => e.Sequence).FirstOrDefault(e =>
        _cardMovements.SingleOrDefault(m => m.Sequence == e.Sequence) is { } m && m.CardId == e.CardId && m.To == CardLocation.Hand(e.RecipientSeat) && !IsNativeDrawGainMovement(m));
    private IReadOnlyList<ProgramTriggerCandidate> CollectBiriMovementCandidates(CardMovementBatchContext batch)
    {
        var potential = ProgramEventHistory<BiriPotentialGainEvent>().FirstOrDefault(e => e.BatchId == batch.Id);
        if (potential is null || FirstBiriGain(potential.ActualTurn, potential.TurnOwner) is not { } first || first.BatchId != batch.Id) return [];
        return Array.AsReadOnly(ProgramEventHistory<BiriPotentialHolderEvent>().Where(e => e.BatchId == batch.Id && _players[e.Source.OwnerSeat].IsAlive &&
            !ProgramEventHistory<BiriDrawIssuedEvent>().Any(p => p.Source.OwnerSeat == e.Source.OwnerSeat && p.ActualTurn == first.ActualTurn && p.TurnOwner == first.TurnOwner))
            .Select(e => new ProgramTriggerCandidate(e.Source.OwnerSeat, e.Source.SkillId, e.Source.BindingId, e.Source.SkillInstanceId, e.GameplayHash, GetProgramTrigger(new ProgramTriggerCandidate(e.Source.OwnerSeat, e.Source.SkillId, e.Source.BindingId, e.Source.SkillInstanceId, e.GameplayHash, 0)).Priority)).ToArray());
    }
    private ProgramSkillWindowContext CreateBiriMovementContext(CardsMovedTriggerWindowFrame w, ProgramTriggerCandidate c) => new(SkillProgramTriggerWindow.CardsGained, w.Id, c.OwnerSeat, SourceSeat: w.Batch.OriginOwnerSeat, TargetSeat: c.OwnerSeat, Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]), MovementBatch: w.Batch, OccurrenceIndex: c.OccurrenceIndex);
    private bool IsIssuedBiriCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (!IsBiriTrigger(GetProgramTrigger(c)) || context.Window != SkillProgramTriggerWindow.CardsGained || context.OwnerSeat != c.OwnerSeat || context.MovementBatch is not { } batch || context.ParentFrameId != batch.Id || !_players[c.OwnerSeat].IsAlive ||
            _resolutionStack.SingleOrDefault(x => x.Id == batch.Id) is not CardsMovedTriggerWindowFrame w || w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || w.Candidates[w.CandidateIndex] != c ||
            !ProgramEventHistory<BiriPotentialHolderEvent>().Any(e => e.BatchId == batch.Id && e.Source == new CardConversionSource(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId) && e.GameplayHash == c.GameplayHash)) return false;
        var potential = ProgramEventHistory<BiriPotentialGainEvent>().FirstOrDefault(e => e.BatchId == batch.Id);
        return potential is not null && FirstBiriGain(potential.ActualTurn, potential.TurnOwner) is { } first && first.BatchId == batch.Id &&
            !ProgramEventHistory<BiriDrawIssuedEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat && e.ActualTurn == first.ActualTurn && e.TurnOwner == first.TurnOwner);
    }
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
