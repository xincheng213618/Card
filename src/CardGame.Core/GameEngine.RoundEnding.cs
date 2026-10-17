namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksRoundEnding => IsDragonBoatActivity || _contentRegistry.ProgramDependencies.HasTriggerWindow(SkillProgramTriggerWindow.RoundEnding);

    private bool TryBeginRoundEndingBoundary(CharacterState previous, bool skipped)
    {
        // Games without this boundary retain their existing event/sequence stream.
        if (!TracksRoundEnding || _roundNumber == 0 || _winner != Winner.None ||
            ProgramEventHistory<RoundEndedEvent>().Any(e => e.RoundNumber == _roundNumber)) return false;
        var progress = _turnProgression;
        if (progress.PendingBeneficiarySeat is { } beneficiary && _players[beneficiary].IsAlive) return false;
        var resume = progress.NormalResumeSeat ?? FindNextAliveSeat(previous.Seat);
        var next = _players[resume].IsAlive ? resume : FindNextAliveSeat(resume);
        if (!_roundTurnSeats.Contains(next)) return false;
        if (_pendingDecision is not null || _resolutionStack.Count != 0 || _phase != TurnPhase.Finished ||
            progress.OwnerSeat != previous.Seat || previous.Seat != _currentSeat || progress.TurnNumber != _turnNumber)
            throw new InvalidOperationException("A round end lost its clean old actual turn.");
        var inherited = Array.AsReadOnly(_pendingCardsMovedBatches.Where(b => b.TurnNumber == _turnNumber &&
            b.AwaitingProgramFrameId is null && (b.ParentFrameId is null || !_resolutionStack.Any(f => f.Id == b.ParentFrameId)))
            .Select(b => b.Id).ToArray());
        var frame = new RoundEndingBoundaryFrame(++_resolutionSequence, _roundNumber, _turnNumber,
            previous.Seat, next, skipped, progress.Kind, progress.NormalResumeSeat, inherited);
        PushRuntimeFrame(frame);
        AdvanceEventRulesAndQueueFact(new RoundEndingStartedEvent(frame.Id, frame.RoundNumber,
            frame.ActualTurnNumber, frame.OwnerSeat, frame.NextNormalSeat));
        AdvanceRuntimeFrame(frame.Id); return true;
    }

    private bool IsRoundEndingMovement(RoundEndingBoundaryFrame parent, CardMovementBatchContext batch) =>
        batch.TurnNumber == parent.ActualTurnNumber && batch.AwaitingProgramFrameId is null &&
        (batch.ParentFrameId == parent.Id || parent.InheritedMovementBatchIds.Contains(batch.Id));

    private void ContinueRoundEndingBoundary(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not RoundEndingBoundaryFrame frame || frame.Id != frameId ||
            frame.ActiveWindowFrameId is not null || _pendingDecision is not null)
            throw new InvalidOperationException("A round end cannot advance before its exact child returns.");
        if (frame.Stage == RoundEndingBoundaryStage.ReturningHolds)
        {
            // Commit the cursor before returning old round holds: their real movements may open children.
            ReplaceRuntimeTop(frame = frame with { Stage = RoundEndingBoundaryStage.Programs });
            ReturnDemonRoundHolds();
        }
        if (TryBeginCardsMovedProgramWindow(frame.Id)) return;
        if (frame.Stage == RoundEndingBoundaryStage.Programs)
        {
            ReplaceRuntimeTop(frame = frame with { Stage = RoundEndingBoundaryStage.Completing });
            if (_winner == Winner.None)
            {
                var participants = _players.Where(p => p.IsAlive).ToArray();
                var facts = new System.Collections.ObjectModel.ReadOnlyDictionary<int, SkillProgramTriggerFacts>(
                    participants.ToDictionary(p => p.Seat, CaptureProgramTriggerFacts));
                var candidates = Array.AsReadOnly(participants.SelectMany(p =>
                        CollectEligibleProgramTriggerCandidates(p, SkillProgramTriggerWindow.RoundEnding, facts[p.Seat]))
                    .OrderBy(c => (c.OwnerSeat - frame.OwnerSeat + _players.Count) % _players.Count)
                    .ThenByDescending(c => c.Priority).ThenBy(c => c.SkillId, StringComparer.Ordinal)
                    .ThenBy(c => c.BindingId, StringComparer.Ordinal).ToArray());
                if (candidates.Count != 0)
                {
                    var child = new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence, frame.OwnerSeat,
                        SkillProgramTriggerWindow.RoundEnding, candidates, ProgramLifecycleContinuation.CompleteRoundEndingBoundary,
                        CaptureProgramTriggerFacts(_players[frame.OwnerSeat]))
                    { RoundEndingBoundaryId = frame.Id, ParticipantFacts = facts };
                    ReplaceRuntimeTop(frame with { ActiveWindowFrameId = child.Id });
                    PushRuntimeFrame(child); AdvanceRuntimeFrame(child.Id); return;
                }
            }
        }
        AdvanceEventRulesAndQueueFact(new RoundEndedEvent(frame.Id, frame.RoundNumber, frame.ActualTurnNumber, frame.OwnerSeat));
        PopResolutionFrame(frame.Id, ResolutionFrameKind.RoundEndingBoundary);
        CompleteActualTurnCursorAfterRoundEnding(_players[frame.OwnerSeat], frame.SkippedTurn);
    }

    private bool IsExactRoundEndingCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.RoundEnding ||
            _resolutionStack.FirstOrDefault() is not RoundEndingBoundaryFrame root ||
            _resolutionStack.Count < 2 || _resolutionStack[1] is not ProgramLifecycleTriggerWindowFrame child ||
            child.Id != context.ParentFrameId || child.Id != root.ActiveWindowFrameId || child.RoundEndingBoundaryId != root.Id ||
            child.Window != context.Window || child.Continuation != ProgramLifecycleContinuation.CompleteRoundEndingBoundary ||
            root.Stage != RoundEndingBoundaryStage.Completing || root.RoundNumber != _roundNumber ||
            root.ActualTurnNumber != _turnNumber || root.OwnerSeat != _currentSeat ||
            context.OwnerSeat != candidate.OwnerSeat || context.SourceSeat != root.OwnerSeat || context.TargetSeat != root.OwnerSeat ||
            child.CandidateIndex < 0 || child.CandidateIndex >= child.Candidates.Count || child.Candidates[child.CandidateIndex] != candidate)
            return false;
        return true;
    }

    private void ReturnRoundEndingLifecycle(ProgramLifecycleTriggerWindowFrame child)
    {
        if (_resolutionStack.LastOrDefault() is not RoundEndingBoundaryFrame root ||
            root.ActiveWindowFrameId != child.Id || child.RoundEndingBoundaryId != root.Id ||
            child.Window != SkillProgramTriggerWindow.RoundEnding || child.CandidateIndex != child.Candidates.Count ||
            root.Stage != RoundEndingBoundaryStage.Completing)
            throw new InvalidOperationException("A round end lost its exact lifecycle return.");
        ReplaceRuntimeTop(root with { ActiveWindowFrameId = null }); AdvanceRuntimeFrame(root.Id);
    }

    private void ReturnRoundEndingMovement(CardsMovedTriggerWindowFrame child)
    {
        if (_resolutionStack.LastOrDefault() is not RoundEndingBoundaryFrame root ||
            root.Id != child.ResumeRoundEndingBoundaryId || root.ActiveWindowFrameId != child.Id ||
            !IsRoundEndingMovement(root, child.Batch))
            throw new InvalidOperationException("A round end lost its exact movement return.");
        ReplaceRuntimeTop(root with { ActiveWindowFrameId = null }); AdvanceRuntimeFrame(root.Id);
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.RoundEnding)]
    private void ReturnRoundEndingProgram(ProgramSkillFrame frame, bool completed)
    {
        if (frame.WindowContext is not { } context ||
            _resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame child ||
            child.CandidateIndex >= child.Candidates.Count || !MountObserverCandidateMatches(frame, child.Candidates[child.CandidateIndex]) ||
            !IsExactRoundEndingCandidate(child.Candidates[child.CandidateIndex], context))
            throw new InvalidOperationException("A round-end program lost its exact current binding.");
        AdvanceProgramLifecycleCursor(child); AdvanceRuntimeFrame(child.Id);
    }

    private bool HasRoundEndingBoundaryFrame()
    {
        if (_resolutionStack.FirstOrDefault() is not RoundEndingBoundaryFrame root) return false;
        if (!TracksRoundEnding || root.RoundNumber != _roundNumber || root.ActualTurnNumber != _turnNumber ||
            root.OwnerSeat != _currentSeat || root.OwnerSeat != _turnProgression.OwnerSeat || _phase != TurnPhase.Finished ||
            root.OriginalActualTurnKind != _turnProgression.Kind || root.NormalResumeSeat != _turnProgression.NormalResumeSeat ||
            !Enum.IsDefined(root.Stage) || root.InheritedMovementBatchIds is not System.Collections.IList { IsReadOnly: true } ||
            ProgramEventHistory<RoundEndingStartedEvent>().Count(e => e == new RoundEndingStartedEvent(root.Id,
                root.RoundNumber, root.ActualTurnNumber, root.OwnerSeat, root.NextNormalSeat)) != 1 ||
            ProgramEventHistory<RoundEndedEvent>().Any(e => e.RoundNumber == root.RoundNumber))
            throw new InvalidOperationException("A round-end root lost its old round, actual turn or once-only issuance.");
        if (root.ActiveWindowFrameId is { } id)
        {
            if (_resolutionStack.Count < 2 || _resolutionStack[1].Id != id ||
                (_resolutionStack[1] is not ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.RoundEnding } lifecycle ||
                 lifecycle.RoundEndingBoundaryId != root.Id || root.Stage != RoundEndingBoundaryStage.Completing) &&
                (_resolutionStack[1] is not CardsMovedTriggerWindowFrame movement ||
                 movement.ResumeRoundEndingBoundaryId != root.Id || !IsRoundEndingMovement(root, movement.Batch)))
                throw new InvalidOperationException("A round end retained an unissued first child.");
        }
        else if (_resolutionStack.Count != 1)
            throw new InvalidOperationException("A round end has an unowned child.");
        return true;
    }
}
