namespace CardGame.Core;

/// <summary>Generic post-movement program host. It contains no skill ids.</summary>
public sealed partial class GameEngine
{
    private sealed class CardMovementBatchBuilder(
        long id,
        long? parentFrameId,
        long? parentBatchId,
        long? awaitingProgramFrameId,
        int turnNumber,
        IReadOnlyDictionary<CardLocation, int> sourceCountsBefore,
        IReadOnlyDictionary<CardLocation, int> destinationCountsBefore,
        ProgramSkillFrame? originProgram)
    {
        public long Id { get; } = id;
        public long? ParentFrameId { get; } = parentFrameId;
        public long? ParentBatchId { get; } = parentBatchId;
        public long? AwaitingProgramFrameId { get; } = awaitingProgramFrameId;
        public int TurnNumber { get; } = turnNumber;
        public IReadOnlyDictionary<CardLocation, int> SourceCountsBefore { get; } = sourceCountsBefore;
        public IReadOnlyDictionary<CardLocation, int> DestinationCountsBefore { get; } = destinationCountsBefore;
        public ProgramSkillFrame? OriginProgram { get; } = originProgram;
    }

    private CardMovementBatchBuilder BeginCardMovementBatch(IEnumerable<CardLocation> sourceLocations, IEnumerable<CardLocation> destinationLocations)
    {
        var sources = sourceLocations.Distinct().ToDictionary(
            location => location,
            location => _cardZones.Count(location));
        var batch = new CardMovementBatchBuilder(
            ++_resolutionSequence,
            _resolutionStack.LastOrDefault()?.Id,
            _activeCardMovementBatchIds.TryPeek(out var parentBatchId) ? parentBatchId : null,
            _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault() is
                { PendingMovementContinuation: not null } awaited ? awaited.Id : null,
            _turnNumber,
            sources,
            destinationLocations.Distinct().ToDictionary(location => location, location => _cardZones.Count(location)),
            _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault());
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
            Array.AsReadOnly(sourceCounts),
            batch.AwaitingProgramFrameId,
            batch.DestinationCountsBefore.OrderBy(item => item.Key.Zone).ThenBy(item => item.Key.OwnerSeat)
                .Select(item => new CardMovementSourceCount(item.Key, item.Value, _cardZones.Count(item.Key))).ToArray(),
            batch.OriginProgram?.SkillId, batch.OriginProgram?.SkillInstanceId, batch.OriginProgram?.OwnerSeat));
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

    private bool TryBeginCardsMovedProgramWindow(long? instructionFrameId = null)
    {
        var awaitingFrame = _resolutionStack.LastOrDefault() is ProgramSkillFrame program &&
            (program.PendingMovementContinuation is not null || program.Id == instructionFrameId) ? program : null;
        bool Eligible(CardMovementBatchContext batch) => awaitingFrame is null
            ? batch.AwaitingProgramFrameId is null
            : batch.AwaitingProgramFrameId == awaitingFrame.Id ||
              batch.AwaitingProgramFrameId is null && batch.ParentFrameId == awaitingFrame.Id;
        if (_pendingDecision is not null ||
            (_resolutionStack.Count != 0 && awaitingFrame is null) ||
            _winner != Winner.None || _status == EngineStatus.Completed)
            return false;

        // Equipment removal may have completed a nested recovery before this movement finished.
        if (awaitingFrame?.PendingMovementContinuation is not null &&
            TryBeginHpChangedProgramWindow(awaitingFrame.Id, PostEventContinuation.AwaitedProgramMovement)) return true;

        while (_pendingCardsMovedBatches.Any(Eligible))
        {
            var batch = _pendingCardsMovedBatches.Where(Eligible).OrderBy(item => item.Id).First();
            _pendingCardsMovedBatches.Remove(batch);
            var candidates = CollectCardsMovedProgramCandidates(batch);
            if (candidates.Count == 0) continue;
            var window = new CardsMovedTriggerWindowFrame(batch.Id, batch, candidates,
                ResumeProgramFrameId: awaitingFrame?.PendingMovementContinuation is null ? awaitingFrame?.Id : null);
            window = window with { Contexts = candidates.Select(candidate => CreateCardsMovedProgramContext(window, candidate)).ToArray() };
            _resolutionStack.Add(window);
            ContinueCardsMovedProgramWindow();
            return true;
        }
        return false;
    }

    private IReadOnlyList<ProgramTriggerCandidate> CollectCardsMovedProgramCandidates(
        CardMovementBatchContext batch)
    {
        var candidates = new List<ProgramTriggerCandidate>();
        foreach (var window in new[] { SkillProgramTriggerWindow.CardsMoved, SkillProgramTriggerWindow.CardsGained })
        foreach (var count in (window == SkillProgramTriggerWindow.CardsMoved ? batch.SourceCounts : batch.DestinationCounts ?? [])
                     .Where(item => item.Location.OwnerSeat is not null))
        {
            var ownerSeat = count.Location.OwnerSeat!.Value;
            if (!IsValidPlayerSeat(ownerSeat)) continue;
            foreach (var candidate in CollectProgramTriggerCandidates(_players[ownerSeat], window))
            {
                var trigger = _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers.Single(item => item.Id == candidate.BindingId);
                if (!(window == SkillProgramTriggerWindow.CardsMoved ? trigger.SourceZones : trigger.DestinationZones).Contains(count.Location.Zone)) continue;
                var indexes = MatchingMovementIndexes(batch, candidate, trigger, count.Location);
                if (indexes.Length == 0) continue;
                var facts = CaptureCardsMovedTriggerFacts(_players[ownerSeat], indexes.Length, count, window);
                if (!trigger.Condition.Evaluate(facts, candidate.SkillId, candidate.SkillInstanceId)) continue;
                var occurrences = trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch ? [0] : indexes;
                candidates.AddRange(occurrences.Select(index => candidate with { OccurrenceIndex = index }));
            }
        }
        return candidates
            .OrderBy(candidate => (candidate.OwnerSeat - _currentSeat + _players.Count) % _players.Count)
            .ThenByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.SkillId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.BindingId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.SkillInstanceId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.OccurrenceIndex)
            .ToArray();
    }

    private static int[] MatchingMovementIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate candidate,
        SkillProgramTrigger trigger, CardLocation location)
    {
        if (trigger.IgnoreOwnSkillMovements && batch.OriginOwnerSeat == candidate.OwnerSeat &&
            batch.OriginSkillId == candidate.SkillId &&
            batch.OriginSkillInstanceId == candidate.SkillInstanceId) return [];
        return batch.Movements.Select((movement, index) => (movement, index))
            .Where(item => (trigger.Window == SkillProgramTriggerWindow.CardsGained ? item.movement.To : item.movement.From) == location &&
                item.movement.From != item.movement.To &&
                (trigger.MovementReasons.Count == 0 || trigger.MovementReasons.Contains(item.movement.Reason.Value)) &&
                !trigger.ExcludedMovementReasons.Contains(item.movement.Reason.Value))
            .Select(item => item.index).ToArray();
    }

    private SkillProgramTriggerFacts CaptureCardsMovedTriggerFacts(
        CharacterState owner,
        int movedCardCount,
        CardMovementSourceCount sourceCount,
        SkillProgramTriggerWindow window)
    {
        var facts = CaptureProgramTriggerFacts(owner);
        return facts with
        {
            MovedCardCount = movedCardCount,
            SourceZoneCountBefore = window == SkillProgramTriggerWindow.CardsMoved ? sourceCount.CountBefore : 0,
            SourceZoneCountAfter = window == SkillProgramTriggerWindow.CardsMoved ? sourceCount.CountAfter : 0,
            DestinationZoneCountBefore = window == SkillProgramTriggerWindow.CardsGained ? sourceCount.CountBefore : 0,
            DestinationZoneCountAfter = window == SkillProgramTriggerWindow.CardsGained ? sourceCount.CountAfter : 0
        };
    }

    private ProgramSkillWindowContext CreateCardsMovedProgramContext(
        CardsMovedTriggerWindowFrame frame,
        ProgramTriggerCandidate candidate)
    {
        if (frame.Contexts is { } contexts) return contexts[frame.CandidateIndex];
        var trigger = _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers.Single(item => item.Id == candidate.BindingId);
        var gained = trigger.Window == SkillProgramTriggerWindow.CardsGained;
        var location = new CardLocation((gained ? trigger.DestinationZones : trigger.SourceZones).Single(), candidate.OwnerSeat);
        var count = (gained ? frame.Batch.DestinationCounts! : frame.Batch.SourceCounts).Single(item => item.Location == location);
        var matchingIndexes = MatchingMovementIndexes(frame.Batch, candidate, trigger, location);
        return new ProgramSkillWindowContext(
            trigger.Window,
            frame.Id,
            candidate.OwnerSeat,
            SourceSeat: gained ? frame.Batch.OriginOwnerSeat : candidate.OwnerSeat,
            TargetSeat: candidate.OwnerSeat,
            OccurrenceIndex: candidate.OccurrenceIndex,
            Facts: CaptureCardsMovedTriggerFacts(
                _players[candidate.OwnerSeat], matchingIndexes.Length, count, trigger.Window),
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
                if (frame.ResumeProgramFrameId is { } resume)
                {
                    ContinueProgramSkill(resume);
                    return;
                }
                if (!TryBeginCardsMovedProgramWindow() &&
                    _resolutionStack.LastOrDefault() is ProgramSkillFrame
                        { PendingMovementContinuation: not null } awaited)
                    CompleteAwaitedProgramMovement(awaited.Id);
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
            candidate.OwnerSeat, CreateCardsMovedProgramContext(frame, candidate).Window, activated, completed));
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
