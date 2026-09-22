namespace CardGame.Core;

/// <summary>Generic post-movement program host. It contains no skill ids.</summary>
public sealed partial class GameEngine
{
    private sealed class CardMovementBatchBuilder(
        long id,
        long? parentFrameId,
        long? parentBatchId,
        int turnNumber,
        IReadOnlyDictionary<CardLocation, int> sourceCountsBefore)
    {
        public long Id { get; } = id;
        public long? ParentFrameId { get; } = parentFrameId;
        public long? ParentBatchId { get; } = parentBatchId;
        public int TurnNumber { get; } = turnNumber;
        public IReadOnlyDictionary<CardLocation, int> SourceCountsBefore { get; } = sourceCountsBefore;
    }

    private CardMovementBatchBuilder BeginCardMovementBatch(IEnumerable<CardLocation> sourceLocations)
    {
        var sources = sourceLocations.Distinct().ToDictionary(
            location => location,
            location => _cardZones.Count(location));
        var batch = new CardMovementBatchBuilder(
            ++_resolutionSequence,
            _resolutionStack.LastOrDefault()?.Id,
            _activeCardMovementBatchIds.TryPeek(out var parentBatchId) ? parentBatchId : null,
            _turnNumber,
            sources);
        _activeCardMovementBatchIds.Push(batch.Id);
        return batch;
    }

    private void CompleteCardMovementBatch(
        CardMovementBatchBuilder batch,
        IReadOnlyList<CardMovementRecord> movements,
        bool committed)
    {
        if (!_activeCardMovementBatchIds.TryPop(out var activeBatchId) || activeBatchId != batch.Id)
            throw new InvalidOperationException("The atomic card-movement batch nesting changed unexpectedly.");
        if (!committed || movements.Count == 0 || !_setupComplete ||
            _winner != Winner.None || _status == EngineStatus.Completed)
            return;
        var sourceCounts = batch.SourceCountsBefore
            .OrderBy(item => item.Key.Zone)
            .ThenBy(item => item.Key.OwnerSeat)
            .Select(item => new CardMovementSourceCount(
                item.Key,
                item.Value,
                _cardZones.Count(item.Key)))
            .ToArray();
        _pendingCardsMovedBatches.Add(new CardMovementBatchContext(
            batch.Id,
            batch.ParentFrameId,
            batch.ParentBatchId,
            batch.TurnNumber,
            Array.AsReadOnly(movements.ToArray()),
            Array.AsReadOnly(sourceCounts)));
    }

    private bool HasCardsMovedProgramBoundaryFrame()
    {
        if (_resolutionStack.FirstOrDefault() is not CardsMovedTriggerWindowFrame frame)
            return false;
        if (frame.CandidateIndex < 0 || frame.CandidateIndex >= frame.Candidates.Count ||
            frame.Batch.Id != frame.Id || frame.Batch.Movements.Count == 0)
            throw new InvalidOperationException("The cards-moved boundary lost its batch or candidate cursor.");
        if (_resolutionStack.Count == 1 && frame.Step == ResolutionFrameStep.AwaitingResponse &&
            _pendingDecision?.Kind != DecisionKind.ProgramTrigger)
            throw new InvalidOperationException("The cards-moved boundary is waiting without its program prompt.");
        return true;
    }

    private bool TryBeginCardsMovedProgramWindow()
    {
        if (_pendingDecision is not null || _resolutionStack.Count != 0 ||
            _winner != Winner.None || _status == EngineStatus.Completed)
            return false;

        while (_pendingCardsMovedBatches.Count > 0)
        {
            var batch = _pendingCardsMovedBatches
                .OrderBy(item => item.Id)
                .First();
            _pendingCardsMovedBatches.Remove(batch);
            var candidates = CollectCardsMovedProgramCandidates(batch);
            if (candidates.Count == 0) continue;
            _resolutionStack.Add(new CardsMovedTriggerWindowFrame(
                batch.Id,
                batch,
                candidates));
            ContinueCardsMovedProgramWindow();
            return true;
        }
        return false;
    }

    private IReadOnlyList<ProgramTriggerCandidate> CollectCardsMovedProgramCandidates(
        CardMovementBatchContext batch)
    {
        var candidates = new List<ProgramTriggerCandidate>();
        foreach (var sourceCount in batch.SourceCounts.Where(item => item.Location.OwnerSeat is not null))
        {
            var ownerSeat = sourceCount.Location.OwnerSeat!.Value;
            if (!IsValidPlayerSeat(ownerSeat)) continue;
            var owner = _players[ownerSeat];
            foreach (var candidate in CollectProgramTriggerCandidates(
                         owner, SkillProgramTriggerWindow.CardsMoved))
            {
                var trigger = _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers
                    .Single(item => item.Id == candidate.BindingId);
                if (!trigger.SourceZones.Contains(sourceCount.Location.Zone)) continue;
                var matchingIndexes = batch.Movements
                    .Select((movement, index) => (movement, index))
                    .Where(item => item.movement.From == sourceCount.Location)
                    .Select(item => item.index)
                    .ToArray();
                if (matchingIndexes.Length == 0) continue;
                var facts = CaptureCardsMovedTriggerFacts(owner, matchingIndexes.Length, sourceCount);
                if (!trigger.Condition.Evaluate(facts)) continue;
                var occurrenceIndexes = trigger.MovementOccurrence switch
                {
                    SkillProgramMovementOccurrence.PerBatch => [0],
                    SkillProgramMovementOccurrence.PerCard => matchingIndexes,
                    _ => throw new InvalidOperationException(
                        "A cards-moved trigger lost its occurrence policy.")
                };
                candidates.AddRange(occurrenceIndexes.Select(index => candidate with
                {
                    OccurrenceIndex = index
                }));
            }
        }
        return candidates
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.SkillId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.BindingId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.SkillInstanceId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.OccurrenceIndex)
            .ToArray();
    }

    private SkillProgramTriggerFacts CaptureCardsMovedTriggerFacts(
        CharacterState owner,
        int movedCardCount,
        CardMovementSourceCount sourceCount)
    {
        var facts = CaptureProgramTriggerFacts(owner);
        return facts with
        {
            MovedCardCount = movedCardCount,
            SourceZoneCountBefore = sourceCount.CountBefore,
            SourceZoneCountAfter = sourceCount.CountAfter
        };
    }

    private ProgramSkillWindowContext CreateCardsMovedProgramContext(
        CardsMovedTriggerWindowFrame frame,
        ProgramTriggerCandidate candidate)
    {
        var trigger = _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers
            .Single(item => item.Id == candidate.BindingId);
        var sourceZone = trigger.SourceZones.Single();
        var sourceLocation = new CardLocation(sourceZone, candidate.OwnerSeat);
        var sourceCount = frame.Batch.SourceCounts.Single(item => item.Location == sourceLocation);
        var matchingIndexes = frame.Batch.Movements
            .Select((movement, index) => (movement, index))
            .Where(item => item.movement.From == sourceLocation)
            .Select(item => item.index)
            .ToArray();
        return new ProgramSkillWindowContext(
            SkillProgramTriggerWindow.CardsMoved,
            frame.Id,
            candidate.OwnerSeat,
            SourceSeat: candidate.OwnerSeat,
            TargetSeat: candidate.OwnerSeat,
            OccurrenceIndex: candidate.OccurrenceIndex,
            Facts: CaptureCardsMovedTriggerFacts(
                _players[candidate.OwnerSeat], matchingIndexes.Length, sourceCount),
            MovementBatch: frame.Batch,
            MovementIndex: trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerCard
                ? candidate.OccurrenceIndex
                : null);
    }

    private void ContinueCardsMovedProgramWindow()
    {
        while (_resolutionStack.LastOrDefault() is CardsMovedTriggerWindowFrame frame)
        {
            if (frame.CandidateIndex >= frame.Candidates.Count)
            {
                PopResolutionFrame(frame.Id, ResolutionFrameKind.CardsMovedTriggerWindow);
                TryBeginCardsMovedProgramWindow();
                return;
            }
            var candidate = frame.Candidates[frame.CandidateIndex];
            var context = CreateCardsMovedProgramContext(frame, candidate);
            if (!CanRunProgramTrigger(candidate, context))
            {
                AdvanceCardsMovedProgramCandidate(frame, candidate, activated: false, completed: false);
                continue;
            }
            var trigger = _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers
                .Single(item => item.Id == candidate.BindingId);
            if (trigger.Optional)
            {
                _resolutionStack[^1] = frame with { Step = ResolutionFrameStep.AwaitingResponse };
                ExposeProgramTriggerDecision(candidate, context);
                return;
            }
            BeginProgramBinding(candidate, context);
            return;
        }
    }

    private void AdvanceCardsMovedProgramCandidate(
        CardsMovedTriggerWindowFrame frame,
        ProgramTriggerCandidate candidate,
        bool activated,
        bool completed)
    {
        QueueGameEvent(new ProgramBindingResolvedEvent(
            frame.Id, candidate.SkillId, candidate.BindingId, candidate.SkillInstanceId,
            candidate.OwnerSeat, SkillProgramTriggerWindow.CardsMoved, activated, completed));
        AdvanceCardsMovedProgramCursor(frame);
    }

    private void AdvanceCardsMovedProgramCursor(CardsMovedTriggerWindowFrame frame)
    {
        if (_resolutionStack.LastOrDefault() is not CardsMovedTriggerWindowFrame current ||
            current.Id != frame.Id || current.CandidateIndex != frame.CandidateIndex)
            throw new InvalidOperationException("The cards-moved trigger cursor is no longer current.");
        _resolutionStack[^1] = current with
        {
            CandidateIndex = current.CandidateIndex + 1,
            Step = ResolutionFrameStep.ResolvingEffect
        };
    }
}
