namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Only a derived lookup cache. Its complete source is the immutable committed/pending rule facts.
    private int _duXiSuitProjectionEventCount = -1;
    private IGameEvent? _duXiSuitProjectionLastFact;
    private IReadOnlyDictionary<int, int> _duXiSuitProjection = new Dictionary<int, int>();
    private static int DuXiSuitBit(Suit suit) => suit switch { Suit.Spade => 1, Suit.Heart => 2, Suit.Club => 4, Suit.Diamond => 8, _ => 0 };
    private IEnumerable<CardMovementRecord> DuXiBixiongMovements(CardMovementBatchContext batch, int owner)
    {
        if (batch.DiscardRecoveryPhase is not { Kind: ActualDiscardRecoveryPhaseKind.Discard } phase ||
            phase.ActorSeat != owner || phase.ActualTurnOwnerSeat != owner || phase.TurnNumber != batch.TurnNumber ||
            batch.OriginOwnerSeat is { } producer && producer != owner) return [];
        return batch.Movements.Where(m => m.From == CardLocation.Hand(owner) && m.To == CardLocation.DiscardPile &&
            IsDiscardMovementReason(m.Reason));
    }
    private void CaptureDuXiBixiongBatch(CardMovementBatchContext batch)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RecordBixiongActualDiscard) ||
            batch.DiscardRecoveryPhase is not { Kind: ActualDiscardRecoveryPhaseKind.Discard } phase) return;
        var owner = phase.ActorSeat;
        if (!IsValidPlayerSeat(owner) || !_players[owner].IsAlive) return;
        var moves = DuXiBixiongMovements(batch, owner).ToArray();
        if (moves.Length == 0) return;
        foreach (var candidate in CollectProgramTriggerCandidates(_players[owner], SkillProgramTriggerWindow.CardsMoved)
            .Where(c => GetProgramTrigger(c).Effects.Any(e => e.Op == SkillProgramEffectOp.RecordBixiongActualDiscard)))
        {
            var source = new CardConversionSource(candidate.SkillId, candidate.BindingId, owner, candidate.SkillInstanceId);
            foreach (var move in moves)
            {
                if (ProgramEventHistory<DuXiBixiongSuitRecordedEvent>().Any(e => e.Source == source && e.MovementSequence == move.Sequence)) continue;
                var suit = EffectiveSuit(_players[owner], GetAdvancedCard(move.CardId));
                if (DuXiSuitBit(suit) == 0) continue;
                AdvanceEventRulesAndQueueFact(new DuXiBixiongSuitRecordedEvent(batch.Id, source, owner, batch.TurnNumber,
                    phase.Token, move.CardId, move.Sequence, suit));
            }
        }
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 26300)]
    private void ExpireDuXiBixiong(IGameEvent payload)
    {
        if (payload is TurnStartedEvent turn && ProgramEventHistory<DuXiBixiongSuitRecordedEvent>().Any(e => e.OwnerSeat == turn.ActorSeat))
            AdvanceEventRulesAndQueueFact(new DuXiBixiongExpiredEvent(turn.ActorSeat, turn.TurnNumber));
    }
    private IReadOnlyDictionary<int, int> DuXiSuitProjection()
    {
        var count = _events.Count + _pendingEvents.Count;
        var last = _pendingEvents.LastOrDefault() ?? _events.LastOrDefault()?.Payload;
        if (_duXiSuitProjectionEventCount == count && ReferenceEquals(_duXiSuitProjectionLastFact, last)) return _duXiSuitProjection;
        var facts = CompleteProgramEventHistory().ToArray();
        var expired = facts.OfType<DuXiBixiongExpiredEvent>().GroupBy(e => e.OwnerSeat).ToDictionary(g => g.Key, g => g.Max(e => e.ActualTurnNumber));
        var masks = new Dictionary<int, int>();
        foreach (var fact in facts.OfType<DuXiBixiongSuitRecordedEvent>())
            if (fact.ActualTurnNumber >= expired.GetValueOrDefault(fact.OwnerSeat))
                masks[fact.OwnerSeat] = masks.GetValueOrDefault(fact.OwnerSeat) | DuXiSuitBit(fact.Suit);
        _duXiSuitProjection = new System.Collections.ObjectModel.ReadOnlyDictionary<int, int>(masks);
        _duXiSuitProjectionEventCount = count;
        _duXiSuitProjectionLastFact = last;
        return _duXiSuitProjection;
    }
    private bool HasAnyDuXiSuitProhibition() => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RecordBixiongActualDiscard) && DuXiSuitProjection().Count != 0;
    private bool IsDuXiSuitTargetProhibited(int sourceSeat, int targetSeat, Suit effectiveSuit) => sourceSeat != targetSeat &&
        DuXiSuitBit(effectiveSuit) != 0 && HasAnyDuXiSuitProhibition() && (DuXiSuitProjection().GetValueOrDefault(targetSeat) & DuXiSuitBit(effectiveSuit)) != 0;
}
