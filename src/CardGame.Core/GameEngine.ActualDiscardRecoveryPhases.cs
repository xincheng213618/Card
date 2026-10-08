namespace CardGame.Core;

public sealed record ActualDiscardRecoveryPhaseRestoredEvent(long ProducerFrameId, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record ActualDiscardRecoveryPhaseClearedEvent(int TurnNumber) : IGameEvent;
public sealed partial class GameEngine
{
    private bool TracksActualDiscardRecoveryPhases =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RestoreActualDiscardBatch) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GiveAfterBatchGain) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawAfterActualOutsideDraw) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DiscardAfterActualOutsideDiscard);

    private ActualDiscardRecoveryPhaseKey? CurrentActualDiscardRecoveryPhase()
    {
        if (!TracksActualDiscardRecoveryPhases || _turnNumber < 1 || _phase is TurnPhase.NotStarted or TurnPhase.Finished) return null;
        var last = CompleteProgramEventHistory().LastOrDefault(e => e is ActualDiscardRecoveryPhaseStartedEvent or
            ActualDiscardRecoveryPhaseRestoredEvent or ActualDiscardRecoveryPhaseClearedEvent);
        var phase = last switch
        {
            ActualDiscardRecoveryPhaseStartedEvent e => e.Phase,
            ActualDiscardRecoveryPhaseRestoredEvent e => e.Phase,
            _ => null
        };
        return phase is not null && phase.TurnNumber == _turnNumber && phase.ActualTurnOwnerSeat == _turnProgression.OwnerSeat ? phase : null;
    }

    private void StartActualDiscardRecoveryPhase(ActualDiscardRecoveryPhaseKind kind, int actor)
    {
        if (!TracksActualDiscardRecoveryPhases || _turnNumber < 1 || _winner != Winner.None || !_players[actor].IsAlive) return;
        var token = CompleteProgramEventHistory().OfType<ActualDiscardRecoveryPhaseStartedEvent>()
            .Select(e => e.Phase.Token).DefaultIfEmpty().Max() + 1;
        AdvanceEventRulesAndQueueFact(new ActualDiscardRecoveryPhaseStartedEvent(new(token, _turnNumber,
            _turnProgression.OwnerSeat, actor, kind)));
    }

    private bool IsRecordedActualDiscardRecoveryPhase(ActualDiscardRecoveryPhaseKey phase) =>
        phase.Token>0 &&phase.TurnNumber>0 &&IsValidPlayerSeat(phase.ActualTurnOwnerSeat) &&IsValidPlayerSeat(phase.ActorSeat) &&
        Enum.IsDefined(phase.Kind) &&CompleteProgramEventHistory().OfType<ActualDiscardRecoveryPhaseStartedEvent>().Count(e=>e.Phase==phase)==1;

    private void ClearActualDiscardRecoveryPhase()
    {
        if (TracksActualDiscardRecoveryPhases && CurrentActualDiscardRecoveryPhase() is not null)
            AdvanceEventRulesAndQueueFact(new ActualDiscardRecoveryPhaseClearedEvent(_turnNumber));
    }

    private void RestoreActualDiscardRecoveryPhase(ProgramPhaseSchedule schedule)
    {
        if (!TracksActualDiscardRecoveryPhases || schedule.DiscardRecoveryReturnPhase is not { } phase) return;
        if (phase.Kind != ActualDiscardRecoveryPhaseKind.Preparation ||
            phase.TurnNumber != _turnNumber || phase.ActualTurnOwnerSeat != _turnProgression.OwnerSeat ||
            !IsRecordedActualDiscardRecoveryPhase(phase) ||
            schedule.ParentFrame.OwnerSeat != phase.ActorSeat)
            throw new InvalidOperationException("An inserted phase lost its exact original actual-phase return token.");
        AdvanceEventRulesAndQueueFact(new ActualDiscardRecoveryPhaseRestoredEvent(schedule.Frame.Id, phase));
    }

    private void RestoreActualDiscardRecoveryPhase(DrawPhaseObligationFrame parent)
    {
        if(!TracksActualDiscardRecoveryPhases ||parent.DiscardRecoveryPhase is not { } phase)return;
        if(parent.Stage!=DrawPhaseObligationStage.Finished ||parent.ActiveWindowFrameId is not null ||
            _resolutionStack.LastOrDefault()?.Id!=parent.Id ||parent.Skipped ||phase.Kind!=ActualDiscardRecoveryPhaseKind.Draw ||
            phase.ActorSeat!=parent.OwnerSeat ||phase.ActualTurnOwnerSeat!=_turnProgression.OwnerSeat ||
            phase.TurnNumber!=parent.ActualTurnNumber ||phase.TurnNumber!=_turnNumber ||!IsRecordedActualDiscardRecoveryPhase(phase))
            throw new InvalidOperationException("An extra Draw child lost its exact finished original Draw-phase token.");
        if(_winner!=Winner.None ||!_players[parent.OwnerSeat].IsAlive){ClearActualDiscardRecoveryPhase();return;}
        AdvanceEventRulesAndQueueFact(new ActualDiscardRecoveryPhaseRestoredEvent(parent.Id,phase));
    }

    private IReadOnlyList<DiscardRecoveryEntity> ActualDiscardRecoveryEntities(CardMovementBatchContext batch, int source) =>
        Array.AsReadOnly(batch.Movements.Where(m => GetProgramDiscardSource(m) is { OwnerSeat: var owner } && owner == source)
            .GroupBy(m => m.CardId).Select(g => g.OrderBy(m => m.Sequence).First())
            .OrderBy(m => m.Sequence).Select(m => new DiscardRecoveryEntity(m.CardId,m.Sequence,GetProgramDiscardSource(m)!.Value)).ToArray());

    private bool ActualDiscardEntityStillAvailable(DiscardRecoveryEntity entity) =>
        _cardZones.GetLocation(entity.CardId) == CardLocation.DiscardPile &&
        !_cardMovements.Any(m => m.CardId == entity.CardId && m.Sequence > entity.MovementSequence && m.From == CardLocation.DiscardPile);

    private void CaptureActualDiscardRecoveryBatch(long batchId, ActualDiscardRecoveryPhaseKey? phase,
        IReadOnlyList<CardMovementRecord> movements)
    {
        if (!TracksActualDiscardRecoveryPhases || phase is null) return;
        foreach (var group in movements.Select(m => (Move:m,From:GetProgramDiscardSource(m)))
            .Where(x => x.From is { OwnerSeat:not null }).GroupBy(x => x.From!.Value.OwnerSeat!.Value))
        {
            var count = group.Select(x => x.Move.CardId).Distinct().Count();
            if (count >= 2) AdvanceEventRulesAndQueueFact(new ActualDiscardRecoveryBatchQualifiedEvent(batchId,phase,group.Key,count));
        }
    }

    private ActualDiscardRecoveryBatchQualifiedEvent? ActualDiscardRecoveryQualification(CardMovementBatchContext batch, int source) =>
        CompleteProgramEventHistory().OfType<ActualDiscardRecoveryBatchQualifiedEvent>().SingleOrDefault(e =>
            e.BatchId == batch.Id && e.DiscardOwnerSeat == source && e.Phase == batch.DiscardRecoveryPhase);

    private bool ActualDiscardRecoveryPhaseUsed(int owner, string skill, string stateId, ActualDiscardRecoveryPhaseKey phase) =>
        CompleteProgramEventHistory().OfType<ActualDiscardRecoveryReturnedEvent>().Any(e => e.Source.OwnerSeat == owner &&
            e.Source.SkillId == skill && e.StateId == stateId && e.Phase.Token == phase.Token);

    private int[] MatchingActualDiscardRecoveryIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate candidate)
    {
        if (batch.DiscardRecoveryPhase is null) return [];
        var op = GetProgramTrigger(candidate).Effects.Single(e => e.Op == SkillProgramEffectOp.RestoreActualDiscardBatch);
        if (ActualDiscardRecoveryPhaseUsed(candidate.OwnerSeat,candidate.SkillId,op.StateId!,batch.DiscardRecoveryPhase)) return [];
        return batch.Movements.Select((m,i) => (Move:m,Index:i,From:GetProgramDiscardSource(m)))
            .Where(x => x.From is { OwnerSeat:{ } source } && source != candidate.OwnerSeat && _players[source].IsAlive)
            .GroupBy(x => x.From!.Value.OwnerSeat!.Value)
            .Where(g => ActualDiscardRecoveryQualification(batch,g.Key) is { OriginalCount:>=2 } &&
                ActualDiscardRecoveryEntities(batch,g.Key).Any(ActualDiscardEntityStillAvailable))
            .Select(g => g.OrderBy(x => x.Move.Sequence).First().Index).ToArray();
    }

    private ProgramSkillWindowContext CreateActualDiscardRecoveryContext(CardsMovedTriggerWindowFrame window, ProgramTriggerCandidate candidate)
    {
        var source = GetProgramDiscardSource(window.Batch.Movements[candidate.OccurrenceIndex])!.Value.OwnerSeat!.Value;
        var fact = ActualDiscardRecoveryQualification(window.Batch,source) ?? throw new InvalidOperationException("Discard recovery lost its frozen batch qualification.");
        return new(SkillProgramTriggerWindow.DiscardPileReceived,window.Id,candidate.OwnerSeat,SourceSeat:source,
            TargetSeat:candidate.OwnerSeat,OccurrenceIndex:candidate.OccurrenceIndex,
            Facts:CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]) with { MovedCardCount = fact.OriginalCount },
            MovementBatch:window.Batch,MovementIndex:candidate.OccurrenceIndex);
    }
}
