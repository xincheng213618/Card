namespace CardGame.Core;

public sealed partial class GameEngine
{
    private readonly List<HpChangeContext> _pendingHpChanges = [];

    private void RecordHpChange(long? parentFrameId, int? sourceSeat, int targetSeat,
        int before, int after, HpChangeKind kind)
    {
        var amount = kind == HpChangeKind.Loss ? before - after : after - before;
        if (!_setupComplete || amount <= 0 || _winner != Winner.None || _status == EngineStatus.Completed) return;
        _pendingHpChanges.Add(new(++_resolutionSequence, parentFrameId, sourceSeat, targetSeat, kind, amount, before, after));
    }

    private bool TryBeginHpChangedProgramWindow(long? resumeFrameId = null,
        PostEventContinuation continuation = PostEventContinuation.Boundary,
        int? cardId = null, CardKind? cardKind = null)
    {
        if (_pendingDecision is not null || _winner != Winner.None || _status == EngineStatus.Completed ||
            (resumeFrameId is null ? _resolutionStack.Count != 0 : _resolutionStack.LastOrDefault()?.Id != resumeFrameId))
            return false;
        while (_pendingHpChanges.FirstOrDefault(change => resumeFrameId is null || change.ParentFrameId == resumeFrameId) is { } change)
        {
            _pendingHpChanges.Remove(change);
            var owner = _players[change.TargetSeat];
            if (!owner.IsAlive) continue;
            var windows = change.Kind switch
            {
                HpChangeKind.Loss => new[] { SkillProgramTriggerWindow.AfterHpLost, SkillProgramTriggerWindow.AfterHealthChanged },
                HpChangeKind.Recovery => new[] { SkillProgramTriggerWindow.AfterHpRecovered, SkillProgramTriggerWindow.AfterHealthChanged },
                _ => new[] { SkillProgramTriggerWindow.AfterHealthChanged }
            };
            var facts = CaptureProgramTriggerFacts(owner) with
            {
                HpChangeAmount = change.Amount, HpBeforeChange = change.HpBefore, HpAfterChange = change.HpAfter
            };
            var candidates = windows.SelectMany(window => CollectProgramTriggerCandidates(owner, window)).Where(candidate =>
                _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers.Single(trigger => trigger.Id == candidate.BindingId)
                    .Condition.Evaluate(facts, candidate.SkillId, candidate.SkillInstanceId))
                .SelectMany(candidate => Enumerable.Range(0,
                    _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers.Single(trigger => trigger.Id == candidate.BindingId)
                        .HpChangeOccurrence == SkillProgramHpChangeOccurrence.PerPoint ? change.Amount : 1)
                    .Select(index => candidate with { OccurrenceIndex = index })).ToArray();
            if (candidates.Length == 0) continue;
            var contexts = candidates.Select(candidate => new ProgramSkillWindowContext(GetProgramTrigger(candidate).Window, change.Id,
                owner.Seat, SourceSeat: change.SourceSeat, TargetSeat: owner.Seat, Amount: change.Amount,
                OccurrenceIndex: candidate.OccurrenceIndex, Facts: facts, HpChange: change)).ToArray();
            _resolutionStack.Add(new HpChangedTriggerWindowFrame(change.Id, change, candidates, contexts,
                continuation, resumeFrameId, cardId, cardKind));
            ContinueHpChangedProgramWindow();
            return true;
        }
        return false;
    }

    private void ContinueHpChangedProgramWindow()
    {
        while (_resolutionStack.LastOrDefault() is HpChangedTriggerWindowFrame frame)
        {
            if (frame.CandidateIndex >= frame.Candidates.Count)
            {
                PopResolutionFrame(frame.Id, ResolutionFrameKind.HpChangedTriggerWindow);
                if (TryBeginHpChangedProgramWindow(frame.ResumeFrameId, frame.Continuation, frame.CardId, frame.CardKind)) return;
                switch (frame.Continuation)
                {
                    case PostEventContinuation.Program:
                        ContinueProgramSkill(frame.ResumeFrameId!.Value);
                        break;
                    case PostEventContinuation.AwaitedProgramMovement:
                        if (!TryBeginCardsMovedProgramWindow()) CompleteAwaitedProgramMovement(frame.ResumeFrameId!.Value);
                        break;
                    case PostEventContinuation.CardUse:
                        var card = _cardZones.CardsAt(_cardZones.GetLocation(frame.CardId!.Value))
                            .Single(item => item.Id == frame.CardId);
                        FinishCardUse(frame.ResumeFrameId!.Value, card, frame.CardKind);
                        break;
                    case PostEventContinuation.GroupRecovery:
                        CompleteGroupRecoveryTarget();
                        break;
                    case PostEventContinuation.Boundary:
                        TryBeginCardsMovedProgramWindow();
                        break;
                }
                return;
            }
            var candidate = frame.Candidates[frame.CandidateIndex];
            var context = frame.Contexts[frame.CandidateIndex];
            if (!CanRunProgramTrigger(candidate, context))
            {
                AdvanceHpChangedProgramCandidate(frame, activated: false, completed: false);
                continue;
            }
            var trigger = _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers.Single(item => item.Id == candidate.BindingId);
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

    private void AssertPostEventProgramInvariants()
    {
        for (var index = 0; index < _resolutionStack.Count; index++)
        {
            var parent = index > 0 ? _resolutionStack[index - 1] : null;
            if (_resolutionStack[index] is CardsMovedTriggerWindowFrame movement)
            {
                if (movement.Contexts is not { } contexts || contexts.Count != movement.Candidates.Count ||
                    movement.CandidateIndex < 0 || movement.CandidateIndex >= contexts.Count ||
                    contexts.Any(context => context.ParentFrameId != movement.Id || context.MovementBatch != movement.Batch))
                    throw new InvalidOperationException("A movement window lost its frozen contexts.");
                if (movement.ResumeProgramFrameId is { } resume && (parent is not ProgramSkillFrame || parent.Id != resume))
                    throw new InvalidOperationException("A movement window lost its instruction continuation.");
            }
            if (_resolutionStack[index] is ProgramLifecycleTriggerWindowFrame { ResumeDyingFrameId: { } dyingId } entry &&
                (entry.Window != SkillProgramTriggerWindow.DyingEntering || entry.Continuation != ProgramLifecycleContinuation.ResumeDyingEntry ||
                 parent is not DyingFrame || parent.Id != dyingId || _pendingDying?.FrameId != dyingId ||
                 entry.OwnerSeat != _pendingDying.VictimSeat || entry.CandidateIndex < 0 || entry.CandidateIndex >= entry.Candidates.Count))
                throw new InvalidOperationException("Dying entry lost its frozen parent or cursor.");
            if (_resolutionStack[index] is not HpChangedTriggerWindowFrame hp) continue;
            var window = hp.Change.Kind == HpChangeKind.Loss
                ? SkillProgramTriggerWindow.AfterHpLost : hp.Change.Kind == HpChangeKind.Recovery
                    ? SkillProgramTriggerWindow.AfterHpRecovered : SkillProgramTriggerWindow.AfterHealthChanged;
            if (hp.Change.Id != hp.Id || hp.Change.Amount <= 0 || hp.Candidates.Count != hp.Contexts.Count ||
                hp.CandidateIndex < 0 || hp.CandidateIndex >= hp.Candidates.Count ||
                hp.Contexts.Where((context, cursor) => context.Window != window && context.Window != SkillProgramTriggerWindow.AfterHealthChanged || context.ParentFrameId != hp.Id ||
                    context.OwnerSeat != hp.Candidates[cursor].OwnerSeat || context.HpChange != hp.Change).Any())
                throw new InvalidOperationException("An HP-change window lost its frozen event or cursor.");
            var parentMatches = hp.Continuation switch
            {
                PostEventContinuation.Boundary => parent is null,
                PostEventContinuation.Program => parent is ProgramSkillFrame program && program.Id == hp.ResumeFrameId,
                PostEventContinuation.AwaitedProgramMovement => parent is ProgramSkillFrame awaited &&
                    awaited.Id == hp.ResumeFrameId && awaited.PendingMovementContinuation is not null,
                PostEventContinuation.CardUse => parent is CardUseFrame use && use.Id == hp.ResumeFrameId && hp.CardId is not null,
                PostEventContinuation.GroupRecovery => parent is CardUseFrame group && group.Id == hp.ResumeFrameId &&
                    _pendingGroupCard is { Effect: GroupCardEffect.Recovery } pending && pending.ResolutionId == group.Id,
                _ => false
            };
            if (!parentMatches) throw new InvalidOperationException("An HP-change window lost its parent continuation.");
        }
    }

    private void AdvanceHpChangedProgramCandidate(HpChangedTriggerWindowFrame frame, bool activated, bool completed)
    {
        var candidate = frame.Candidates[frame.CandidateIndex];
        QueueGameEvent(new ProgramBindingResolvedEvent(frame.Id, candidate.SkillId, candidate.BindingId,
            candidate.SkillInstanceId, candidate.OwnerSeat, frame.Contexts[frame.CandidateIndex].Window, activated, completed));
        AdvanceHpChangedProgramCursor(frame);
    }

    private void AdvanceHpChangedProgramCursor(HpChangedTriggerWindowFrame frame)
    {
        if (_resolutionStack.LastOrDefault() != frame)
            throw new InvalidOperationException("The HP-change trigger cursor is no longer current.");
        _resolutionStack[^1] = frame with { CandidateIndex = frame.CandidateIndex + 1, Step = ResolutionFrameStep.ResolvingEffect };
    }
}
