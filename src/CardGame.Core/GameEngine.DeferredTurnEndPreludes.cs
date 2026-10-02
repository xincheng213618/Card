namespace CardGame.Core;

public sealed record DeferredTurnEndPreludeChild(long FrameId, CardMovementBatchContext Batch);
public sealed record DeferredTurnEndPrelude(bool Completed = false, DeferredTurnEndPreludeChild? CurrentChild = null);
public sealed record DeferredTurnEndPreludeReturn(long ParentFrameId, int OwnerSeat, int TurnNumber, long BatchId);

public sealed partial class GameEngine
{
    private CardsMovedTriggerWindowFrame? CreateCardsMovedProgramWindow(CardMovementBatchContext batch, long? resumeProgramFrameId = null)
    {
        var candidates = CollectCardsMovedProgramCandidates(batch).Concat(CollectDiscardPileReceivedCandidates(batch))
            .Concat(CollectFirstDomainCandidates(batch)).ToArray();
        if (candidates.Length == 0) return null;
        var window = new CardsMovedTriggerWindowFrame(batch.Id, batch, Array.AsReadOnly(candidates), ResumeProgramFrameId: resumeProgramFrameId);
        return window with { Contexts = Array.AsReadOnly(candidates.Select(c => CreateCardsMovedProgramContext(window, c)).ToArray()) };
    }

    private bool IsActualDeferredTurnEndPrelude(DeferredTurnEndFrame parent) =>
        HasActualTurnEndMovementPrelude && parent.OwnerSeat == _currentSeat && parent.TurnNumber == _turnNumber &&
        _phase == TurnPhase.Finished && parent.Current is null && parent.ItemIndex == 0 && parent.AfterTurnEnded is null &&
        parent.Prelude is { Completed: false } && CompleteProgramEventHistory().OfType<TurnEndedEvent>()
            .Any(e => e.TurnNumber == parent.TurnNumber && e.ActorSeat == parent.OwnerSeat);

    private bool ContinueDeferredTurnEndPrelude(DeferredTurnEndFrame parent)
    {
        if (!IsActualDeferredTurnEndPrelude(parent) || parent.Prelude!.CurrentChild is not null || _resolutionStack[^1] != parent)
            throw new InvalidOperationException("The end prelude lost its actual turn or retained an unfinished movement child.");
        while (_pendingCardsMovedBatches.Any(b => b.AwaitingProgramFrameId is null))
        {
            var pending = _pendingCardsMovedBatches.Where(b => b.AwaitingProgramFrameId is null).OrderBy(b => b.Id).First();
            _pendingCardsMovedBatches.Remove(pending);
            var batch = pending with { Movements = Array.AsReadOnly(pending.Movements.ToArray()),
                SourceCounts = Array.AsReadOnly(pending.SourceCounts.ToArray()),
                DestinationCounts = pending.DestinationCounts is null ? null : Array.AsReadOnly(pending.DestinationCounts.ToArray()) };
            var window = CreateCardsMovedProgramWindow(batch);
            if (window is null) continue;
            ReplaceRuntimeTop(parent with { Prelude = parent.Prelude with { CurrentChild = new(window.Id, batch) } });
            PushRuntimeFrame(window with { DeferredTurnEndReturn = new(parent.Id, parent.OwnerSeat, parent.TurnNumber, batch.Id) });
            AdvanceRuntimeTop<CardsMovedTriggerWindowFrame>();
            return true;
        }
        ReplaceRuntimeTop(parent with { Prelude = parent.Prelude with { Completed = true } });
        return false;
    }

    private static bool SameDeferredTurnEndBatch(CardMovementBatchContext left, CardMovementBatchContext right) =>
        left.Id == right.Id && left.ParentFrameId == right.ParentFrameId && left.ParentBatchId == right.ParentBatchId &&
        left.TurnNumber == right.TurnNumber && left.AwaitingProgramFrameId == right.AwaitingProgramFrameId &&
        left.OriginSkillId == right.OriginSkillId && left.OriginSkillInstanceId == right.OriginSkillInstanceId && left.OriginOwnerSeat == right.OriginOwnerSeat &&
        left.Movements.SequenceEqual(right.Movements) && left.SourceCounts.SequenceEqual(right.SourceCounts) &&
        (left.DestinationCounts is null ? right.DestinationCounts is null : right.DestinationCounts is not null && left.DestinationCounts.SequenceEqual(right.DestinationCounts));

    private bool MatchesDeferredTurnEndPreludeChild(DeferredTurnEndFrame parent, CardsMovedTriggerWindowFrame child) =>
        IsActualDeferredTurnEndPrelude(parent) && parent.Prelude!.CurrentChild is { } current && current.FrameId == child.Id &&
        child.ResumeProgramFrameId is null && child.DeferredTurnEndReturn is { } receipt && receipt.ParentFrameId == parent.Id &&
        receipt.OwnerSeat == parent.OwnerSeat && receipt.TurnNumber == parent.TurnNumber && receipt.BatchId == current.Batch.Id &&
        child.Id == child.Batch.Id && child.Batch.TurnNumber == parent.TurnNumber && child.Batch.AwaitingProgramFrameId is null &&
        SameDeferredTurnEndBatch(current.Batch, child.Batch);

    private void ReturnDeferredTurnEndPrelude(CardsMovedTriggerWindowFrame child)
    {
        if (_resolutionStack.LastOrDefault() is not DeferredTurnEndFrame parent ||
            !MatchesDeferredTurnEndPreludeChild(parent, child) || child.CandidateIndex != child.Candidates.Count)
            throw new InvalidOperationException("A completed movement batch lost its exact owning end prelude or full batch receipt.");
        ReplaceRuntimeTop(parent with { Prelude = parent.Prelude! with { CurrentChild = null } });
        ContinueDeferredTurnEnd();
    }

    private bool HasDeferredTurnEndPreludeBoundary(DeferredTurnEndFrame parent)
    {
        if (_resolutionStack.Count < 2 || _resolutionStack[1] is not CardsMovedTriggerWindowFrame window ||
            !MatchesDeferredTurnEndPreludeChild(parent, window) || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count)
            throw new InvalidOperationException("The end prelude lost its exact movement batch child.");
        var candidate = window.Candidates[window.CandidateIndex];
        var context = CreateCardsMovedProgramContext(window, candidate);
        if (_resolutionStack.Count == 2 && window.Step == ResolutionFrameStep.AwaitingResponse && GetProgramTrigger(candidate).Optional &&
            _pendingDecision is { Kind: DecisionKind.ProgramTrigger } decision && decision.PlayerSeat == candidate.OwnerSeat &&
            decision.SourceSeat == context.SourceSeat && decision.TargetSeat == context.TargetSeat && decision.Choices.Count == 2 &&
            decision.Choices.All(c => c.Parameters.GetValueOrDefault("skill-id") == candidate.SkillId &&
                c.Parameters.GetValueOrDefault("binding-id") == candidate.BindingId && c.Parameters.GetValueOrDefault("skill-instance-id") == candidate.SkillInstanceId) &&
            decision.Choices.Select(c => c.Parameters.GetValueOrDefault("program-action")).Order(StringComparer.Ordinal).SequenceEqual(["activate", "skip"])) return true;
        if (_resolutionStack.Count < 3 || _resolutionStack[2] is not ProgramSkillFrame child || child.WindowContext != context ||
            child.OwnerSeat != candidate.OwnerSeat || child.SkillId != candidate.SkillId || child.SkillInstanceId != candidate.SkillInstanceId ||
            child.TriggerId != candidate.BindingId || child.ActivationId != candidate.BindingId || child.GameplayHash != candidate.GameplayHash)
            throw new InvalidOperationException("The end prelude movement window lost its exact candidate, prompt or program child.");
        return true;
    }
}
