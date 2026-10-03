namespace CardGame.Core;

public enum DrawPhaseObligationStage { Starting, Drawing, DrawChildren, EndObservers, SkipObservers, ConsumeMarker, Finished }
public sealed record DrawPhaseObligationFrame(long Id, int OwnerSeat, int ActualTurnNumber,
    bool IsExtra, int DelayedEffects, DrawPhaseObligationStage Stage, long? ParentFrameId = null,
    bool NormalDrawReplaced = false, int DrawAdjustment = 0, int? FrozenBaseDrawCount = null,
    bool Skipped = false, int DrawCount = 0, long? ActiveWindowFrameId = null)
    : ResolutionFrame(Id, ResolutionFrameKind.DrawPhaseObligation, ResolutionFrameStep.ResolvingEffect)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<long>? InheritedMovementBatchIds { get; init; }
}
public sealed record DrawPhaseSkippedEvent(long FrameId, int OwnerSeat, int ActualTurnNumber, bool IsExtra) : IGameEvent;
public sealed record ExtraDrawPhaseStartedEvent(long FrameId, long ParentFrameId, int OwnerSeat,
    int ActualTurnNumber, PlayerMarkerKind Marker, int SourceSeat, string SkillId, string SkillInstanceId) : IGameEvent;
public sealed record ActualDrawPhaseCompletedEvent(long FrameId, int OwnerSeat, int ActualTurnNumber,
    bool IsExtra, bool Skipped, int DrawCount) : IGameEvent;

public sealed partial class GameEngine
{
    private bool HasDrawPhaseObligationBoundaryFrame()
    {
        if (_resolutionStack.FirstOrDefault() is not DrawPhaseObligationFrame) return false;
        for (var index = 0; index < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not DrawPhaseObligationFrame frame) continue;
            if (frame.OwnerSeat != _currentSeat || frame.ActualTurnNumber != _turnNumber || _phase != TurnPhase.Draw ||
                frame.DrawCount < 0 || frame.InheritedMovementBatchIds is { } inherited &&
                    (inherited.Distinct().Count() != inherited.Count || inherited is not System.Collections.IList { IsReadOnly: true }))
                throw new InvalidOperationException("An actual draw boundary lost its turn or frozen movement provenance.");
            if (index == 0 ? frame.ParentFrameId is not null || frame.IsExtra :
                !frame.IsExtra || _resolutionStack[index - 1] is not DrawPhaseObligationFrame parent ||
                parent.Id != frame.ParentFrameId || parent.Stage != DrawPhaseObligationStage.Finished)
                throw new InvalidOperationException("An extra draw boundary lost its exact finished parent phase.");
            if (frame.ActiveWindowFrameId is not { } childId) continue;
            if (index + 1 >= _resolutionStack.Count || _resolutionStack[index + 1] is not ProgramLifecycleTriggerWindowFrame child ||
                child.Id != childId || child.ResumeDrawPhaseObligationFrameId != frame.Id || child.OwnerSeat != frame.OwnerSeat ||
                !(child.Window == SkillProgramTriggerWindow.DrawPhaseStarting && frame.Stage == DrawPhaseObligationStage.Drawing && child.Continuation == ProgramLifecycleContinuation.CompleteDrawPhase ||
                  child.Window == SkillProgramTriggerWindow.DrawPhaseEnded && frame.Stage == DrawPhaseObligationStage.ConsumeMarker && child.Continuation == ProgramLifecycleContinuation.CompleteDrawPhaseEnded ||
                  child.Window == SkillProgramTriggerWindow.DrawPhaseSkipped && frame.Stage == DrawPhaseObligationStage.Finished && frame.Skipped && child.Continuation == ProgramLifecycleContinuation.ResumeDrawPhaseObligation))
                throw new InvalidOperationException("An actual draw boundary lost its exact active lifecycle return.");
        }
        return true;
    }

    private bool IsActualSkippedDrawContext(ProgramSkillWindowContext context) =>
        _phase == TurnPhase.Draw && context.SourceSeat == _currentSeat && context.TargetSeat == _currentSeat &&
        _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().LastOrDefault() is
            { Window: SkillProgramTriggerWindow.DrawPhaseSkipped, Continuation: ProgramLifecycleContinuation.ResumeDrawPhaseObligation,
              ResumeDrawPhaseObligationFrameId: { } ownerId } child && child.Id == context.ParentFrameId &&
        _resolutionStack.OfType<DrawPhaseObligationFrame>().SingleOrDefault(f => f.Id == ownerId) is
            { Stage: DrawPhaseObligationStage.Finished, Skipped: true } parent && parent.ActiveWindowFrameId == child.Id &&
        parent.OwnerSeat == _currentSeat && parent.ActualTurnNumber == _turnNumber;

    private bool TryBeginActualDrawCompletion(CharacterState owner, DelayedTurnEffects delayedEffects)
    {
        var skipped = delayedEffects.HasFlag(DelayedTurnEffects.SkipDrawPhase);
        if (_resolutionStack.Count != 0 || _pendingDecision is not null || !owner.IsAlive || _winner != Winner.None ||
            !(skipped && _contentRegistry.ProgramDependencies.HasTriggerWindow(SkillProgramTriggerWindow.DrawPhaseSkipped) ||
              !skipped && HasIssuedExtraDrawMarker(owner.Seat))) return false;
        var inherited = Array.AsReadOnly(_pendingCardsMovedBatches.Where(batch => batch.ParentFrameId is null &&
            batch.TurnNumber == _turnNumber && batch.Movements.Count > 0 && batch.Movements.All(m =>
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(owner.Seat) && m.Reason == CardMoveReasons.Draw))
            .Select(batch => batch.Id).ToArray());
        var frame = new DrawPhaseObligationFrame(++_resolutionSequence, owner.Seat, _turnNumber, false,
            (int)delayedEffects, skipped ? DrawPhaseObligationStage.SkipObservers : DrawPhaseObligationStage.EndObservers,
            Skipped: skipped, DrawCount: _pendingCardsMovedBatches.Where(batch => inherited.Contains(batch.Id)).Sum(batch => batch.Movements.Count))
        { InheritedMovementBatchIds = inherited };
        PushRuntimeFrame(frame); AdvanceRuntimeFrame(frame.Id); return true;
    }

    private void ContinueDrawPhaseObligation(long frameId)
    {
        while (_resolutionStack.LastOrDefault() is DrawPhaseObligationFrame frame && frame.Id == frameId)
        {
            if (frame.OwnerSeat != _currentSeat || frame.ActualTurnNumber != _turnNumber || _phase != TurnPhase.Draw ||
                frame.ActiveWindowFrameId is not null)
                throw new InvalidOperationException("An actual draw phase lost its owner, turn or exact active child.");
            if (TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.DrawPhaseObligation) ||
                TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.DrawPhaseObligation) ||
                TryBeginCardsMovedProgramWindow(frame.Id)) return;
            if (_winner != Winner.None || !_players[frame.OwnerSeat].IsAlive)
            { ReplaceRuntimeTop(frame with { Stage = DrawPhaseObligationStage.Finished }); frame = (DrawPhaseObligationFrame)_resolutionStack.Last(); }
            switch (frame.Stage)
            {
                case DrawPhaseObligationStage.Starting:
                    ReplaceRuntimeTop(frame with { Stage = DrawPhaseObligationStage.Drawing });
                    if (TryBeginOwnedDrawPhaseWindow(frame.Id, SkillProgramTriggerWindow.DrawPhaseStarting)) return;
                    break;
                case DrawPhaseObligationStage.Drawing:
                    if (frame.Skipped)
                    { ReplaceRuntimeTop(frame with { Stage = DrawPhaseObligationStage.SkipObservers }); break; }
                    var count = frame.NormalDrawReplaced ? 0 : Math.Max(0, checked((frame.FrozenBaseDrawCount ?? GetTurnDrawCount(_players[frame.OwnerSeat])) + frame.DrawAdjustment));
                    ReplaceRuntimeTop(frame with { Stage = DrawPhaseObligationStage.DrawChildren, DrawCount = 0 });
                    if (!frame.NormalDrawReplaced)
                    {
                        var actual = DrawCards(_players[frame.OwnerSeat], count, true, new("program.extra-draw-phase.draw"));
                        var paidDraw = (DrawPhaseObligationFrame)_resolutionStack.Last();
                        ReplaceRuntimeTop(paidDraw with { DrawCount = actual.Count });
                    }
                    break;
                case DrawPhaseObligationStage.DrawChildren:
                    // The next dispatcher iteration has drained the actual gain/HP children.
                    ReplaceRuntimeTop(frame with { Stage = DrawPhaseObligationStage.EndObservers });
                    break;
                case DrawPhaseObligationStage.EndObservers:
                    ReplaceRuntimeTop(frame with { Stage = DrawPhaseObligationStage.ConsumeMarker });
                    if (TryBeginOwnedDrawPhaseWindow(frame.Id, SkillProgramTriggerWindow.DrawPhaseEnded)) return;
                    break;
                case DrawPhaseObligationStage.SkipObservers:
                    ReplaceRuntimeTop(frame with { Stage = DrawPhaseObligationStage.Finished });
                    if (_contentRegistry.ProgramDependencies.HasTriggerWindow(SkillProgramTriggerWindow.DrawPhaseSkipped))
                    {
                        AdvanceEventRulesAndQueueFact(new DrawPhaseSkippedEvent(frame.Id, frame.OwnerSeat, frame.ActualTurnNumber, frame.IsExtra));
                        if (TryBeginOwnedDrawPhaseWindow(frame.Id, SkillProgramTriggerWindow.DrawPhaseSkipped)) return;
                    }
                    break;
                case DrawPhaseObligationStage.ConsumeMarker:
                    var policy = ConsumeIssuedExtraDrawMarker(frame);
                    if (policy is null)
                    { ReplaceRuntimeTop(frame with { Stage = DrawPhaseObligationStage.Finished }); break; }
                    var child = new DrawPhaseObligationFrame(++_resolutionSequence, frame.OwnerSeat, frame.ActualTurnNumber,
                        true, 0, DrawPhaseObligationStage.Starting, ParentFrameId: frame.Id);
                    // This phase's ended boundary has consumed its obligation. A
                    // marker issued inside the child cannot borrow this old boundary.
                    ReplaceRuntimeTop(frame with { Stage = DrawPhaseObligationStage.Finished });
                    PushRuntimeFrame(child);
                    AdvanceEventRulesAndQueueFact(new ExtraDrawPhaseStartedEvent(child.Id, frame.Id, frame.OwnerSeat,
                        frame.ActualTurnNumber, policy.Marker, policy.SourceSeat, policy.SkillId, policy.SkillInstanceId));
                    AdvanceRuntimeFrame(child.Id); return;
                case DrawPhaseObligationStage.Finished:
                    CompleteOwnedDrawPhase(frame); return;
                default: throw new InvalidOperationException("Unknown actual draw phase stage.");
            }
        }
    }

    private bool TryBeginOwnedDrawPhaseWindow(long frameId, SkillProgramTriggerWindow window)
    {
        if (_resolutionStack.LastOrDefault() is not DrawPhaseObligationFrame frame || frame.Id != frameId ||
            frame.ActiveWindowFrameId is not null || _pendingDecision is not null)
            throw new InvalidOperationException("A draw phase cannot replace a still-active observer window.");
        var owner = _players[frame.OwnerSeat];
        IReadOnlyDictionary<int, SkillProgramTriggerFacts>? participants = null;
        var facts = CaptureProgramTriggerFacts(owner);
        IReadOnlyList<ProgramTriggerCandidate> candidates;
        if (window == SkillProgramTriggerWindow.DrawPhaseSkipped)
        {
            var participantFacts = _players.Where(p => p.IsAlive).ToDictionary(p => p.Seat, CaptureProgramTriggerFacts);
            participants = new System.Collections.ObjectModel.ReadOnlyDictionary<int, SkillProgramTriggerFacts>(participantFacts);
            candidates = Array.AsReadOnly(_players.Where(p => p.IsAlive).SelectMany(p =>
                CollectEligibleProgramTriggerCandidates(p, window, participantFacts[p.Seat]))
                .OrderBy(c => (c.OwnerSeat - frame.OwnerSeat + _playerCount) % _playerCount).ThenByDescending(c => c.Priority)
                .ThenBy(c => c.SkillId, StringComparer.Ordinal).ThenBy(c => c.BindingId, StringComparer.Ordinal).ToArray());
        }
        else
        {
            if (window == SkillProgramTriggerWindow.DrawPhaseStarting && HasAttributedEventOperations())
                facts = facts with { EventTargetMarkerCounts = new Dictionary<PlayerMarkerKind, int>(owner.Markers) };
            candidates = CollectEligibleProgramTriggerCandidates(owner, window, facts);
            if (window == SkillProgramTriggerWindow.DrawPhaseStarting) candidates = IncludeAttributedDrawObservers(owner, candidates);
        }
        if (candidates.Count == 0) return false;
        var continuation = window switch
        {
            SkillProgramTriggerWindow.DrawPhaseStarting => ProgramLifecycleContinuation.CompleteDrawPhase,
            SkillProgramTriggerWindow.DrawPhaseEnded => ProgramLifecycleContinuation.CompleteDrawPhaseEnded,
            SkillProgramTriggerWindow.DrawPhaseSkipped => ProgramLifecycleContinuation.ResumeDrawPhaseObligation,
            _ => throw new InvalidOperationException("The owned draw phase requested an unrelated window.")
        };
        var child = new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence, owner.Seat, window,
            candidates, continuation, facts)
        { ResumeDrawPhaseObligationFrameId = frame.Id, ParticipantFacts = participants,
          DrawPhaseEndedDelayedEffects = window == SkillProgramTriggerWindow.DrawPhaseEnded ? frame.DelayedEffects : null };
        ReplaceRuntimeTop(frame with { ActiveWindowFrameId = child.Id });
        PushRuntimeFrame(child); AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>(); return true;
    }

    private void ReturnDrawPhaseObligationWindow(ProgramLifecycleTriggerWindowFrame completed)
    {
        if (_resolutionStack.LastOrDefault() is not DrawPhaseObligationFrame frame ||
            frame.Id != completed.ResumeDrawPhaseObligationFrameId || frame.ActiveWindowFrameId != completed.Id ||
            frame.OwnerSeat != completed.OwnerSeat || frame.ActualTurnNumber != _turnNumber)
            throw new InvalidOperationException("The actual draw window lost its typed producer return.");
        var stage = completed.Window switch
        {
            SkillProgramTriggerWindow.DrawPhaseStarting => DrawPhaseObligationStage.Drawing,
            SkillProgramTriggerWindow.DrawPhaseEnded => DrawPhaseObligationStage.ConsumeMarker,
            SkillProgramTriggerWindow.DrawPhaseSkipped => DrawPhaseObligationStage.Finished,
            _ => throw new InvalidOperationException("A draw producer received an unrelated window return.")
        };
        if (frame.Stage != stage) throw new InvalidOperationException("The actual draw cursor was advanced before its child returned.");
        var skipped = completed.Window == SkillProgramTriggerWindow.DrawPhaseStarting && completed.ActualPhaseSubstitution is
            { SkippedPhase: SkillProgramTurnPhase.Draw } substitution && substitution.ParentFrameId == completed.Id &&
            substitution.OwnerSeat == frame.OwnerSeat && substitution.ActualTurnNumber == frame.ActualTurnNumber;
        ReplaceRuntimeTop(frame with { ActiveWindowFrameId = null, Stage = stage,
            NormalDrawReplaced = completed.Window == SkillProgramTriggerWindow.DrawPhaseStarting ? completed.NormalDrawReplaced : frame.NormalDrawReplaced,
            DrawAdjustment = completed.Window == SkillProgramTriggerWindow.DrawPhaseStarting ? completed.NormalDrawAdjustment : frame.DrawAdjustment,
            FrozenBaseDrawCount = completed.Window == SkillProgramTriggerWindow.DrawPhaseStarting ? completed.FrozenBaseDrawCount : frame.FrozenBaseDrawCount,
            Skipped = frame.Skipped || skipped });
        AdvanceRuntimeFrame(frame.Id);
    }

    private void CompleteOwnedDrawPhase(DrawPhaseObligationFrame frame)
    {
        if (frame.ActiveWindowFrameId is not null) throw new InvalidOperationException("A draw phase cannot finish before its observer returns.");
        AdvanceEventRulesAndQueueFact(new ActualDrawPhaseCompletedEvent(frame.Id, frame.OwnerSeat, frame.ActualTurnNumber,
            frame.IsExtra, frame.Skipped, frame.DrawCount));
        PopResolutionFrame(frame.Id, ResolutionFrameKind.DrawPhaseObligation);
        if (frame.ParentFrameId is { } parentId)
        {
            if (_resolutionStack.LastOrDefault() is not DrawPhaseObligationFrame parent || parent.Id != parentId ||
                parent.OwnerSeat != frame.OwnerSeat || parent.ActualTurnNumber != frame.ActualTurnNumber ||
                parent.Stage != DrawPhaseObligationStage.Finished)
                throw new InvalidOperationException("An inserted draw phase lost its exact parent phase.");
            AdvanceRuntimeFrame(parent.Id); return;
        }
        if (_winner != Winner.None) { AdvanceRulesAndPublishState(); return; }
        if (!_players[frame.OwnerSeat].IsAlive) { EndTurn(); return; }
        CompleteTurnStartAfterDraw(_players[frame.OwnerSeat], (DelayedTurnEffects)frame.DelayedEffects,
            drawPhaseEndedProgramsCompleted: true, actualDrawCompletionCompleted: true);
    }
}
