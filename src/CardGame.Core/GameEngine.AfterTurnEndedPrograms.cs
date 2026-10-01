namespace CardGame.Core;

public sealed record AfterTurnEndedProgramChild(long FrameId, ProgramTriggerCandidate Candidate);
public sealed record AfterTurnEndedWindow(IReadOnlyList<TurnEndingBoundaryItem> Items,
    int ItemIndex = 0, AfterTurnEndedProgramChild? CurrentChild = null);

public sealed partial class GameEngine
{
    private bool HasAfterTurnEndedPrograms =>
        _contentRegistry.ProgramDependencies.HasTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded);

    private bool IsActualAfterTurnEndedParent(DeferredTurnEndFrame parent) =>
        HasAfterTurnEndedPrograms && parent.OwnerSeat == _currentSeat && parent.TurnNumber == _turnNumber &&
        _phase == TurnPhase.Finished && parent.Prelude is { Completed: true } && parent.Current is null && parent.ItemIndex == parent.DueIds.Count &&
        CompleteProgramEventHistory().OfType<TurnEndedEvent>().Any(e => e.TurnNumber == parent.TurnNumber && e.ActorSeat == parent.OwnerSeat);

    private ProgramTriggerCandidate AfterTurnEndedCandidate(DeferredTurnEndFrame parent)
    {
        if (!IsActualAfterTurnEndedParent(parent) || parent.AfterTurnEnded is not { } window ||
            window.ItemIndex < 0 || window.ItemIndex >= window.Items.Count ||
            window.Items[window.ItemIndex] is not { Kind: TurnEndingBoundaryItemKind.Program, Candidate: { } candidate, Facts: not null })
            throw new InvalidOperationException("An after-turn-ended window lost its actual end or frozen candidate.");
        return candidate;
    }

    private ProgramSkillWindowContext CreateAfterTurnEndedContext(DeferredTurnEndFrame parent, ProgramTriggerCandidate candidate) =>
        new(SkillProgramTriggerWindow.AfterTurnEnded, parent.Id, candidate.OwnerSeat,
            SourceSeat: parent.OwnerSeat, TargetSeat: parent.OwnerSeat, OccurrenceIndex: candidate.OccurrenceIndex,
            Facts: parent.AfterTurnEnded!.Items[parent.AfterTurnEnded.ItemIndex].Facts);

    private bool CanRunAfterTurnEndedCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context, SkillProgramTrigger trigger)
    {
        var parent = _resolutionStack.OfType<DeferredTurnEndFrame>().LastOrDefault();
        return context.Window == SkillProgramTriggerWindow.AfterTurnEnded && trigger.Window == context.Window &&
            parent is not null && IsActualAfterTurnEndedParent(parent) && parent.Id == context.ParentFrameId &&
            AfterTurnEndedCandidate(parent) == candidate && context.OwnerSeat == candidate.OwnerSeat &&
            context.SourceSeat == parent.OwnerSeat && context.TargetSeat == parent.OwnerSeat &&
            context.OccurrenceIndex == candidate.OccurrenceIndex && context.Facts == parent.AfterTurnEnded!.Items[parent.AfterTurnEnded.ItemIndex].Facts &&
            (trigger.TurnOwnerScope == SkillProgramTurnOwnerScope.Own ? candidate.OwnerSeat == parent.OwnerSeat :
                candidate.OwnerSeat != parent.OwnerSeat && _players[parent.OwnerSeat].IsAlive);
    }

    private bool ContinueAfterTurnEndedPrograms(DeferredTurnEndFrame parent)
    {
        if (!HasAfterTurnEndedPrograms) return false;
        if (!IsActualAfterTurnEndedParent(parent))
            throw new InvalidOperationException("After-turn-ended programs require the completed actual turn and prior due chain.");
        if (parent.AfterTurnEnded is null)
        {
            var items = _players.Where(p => p.IsAlive).SelectMany(owner =>
            {
                var facts = CaptureProgramTriggerFacts(owner);
                return CollectEligibleProgramTriggerCandidates(owner, SkillProgramTriggerWindow.AfterTurnEnded, facts)
                    .Where(c => GetProgramTrigger(c).TurnOwnerScope == (owner.Seat == parent.OwnerSeat ?
                        SkillProgramTurnOwnerScope.Own : SkillProgramTurnOwnerScope.OtherLiving))
                    .Select(c => new TurnEndingBoundaryItem(TurnEndingBoundaryItemKind.Program, c.Priority,
                        $"program:{c.SkillId}:{c.BindingId}:{c.SkillInstanceId}", c, facts));
            }).OrderBy(i => (i.Candidate!.OwnerSeat - parent.OwnerSeat + _players.Count) % _players.Count)
                .ThenByDescending(i => i.Priority).ThenBy(i => i.StableIdentity, StringComparer.Ordinal).ToArray();
            ReplaceRuntimeTop(parent = parent with { AfterTurnEnded = new(Array.AsReadOnly(items)) });
        }
        while (parent.AfterTurnEnded!.ItemIndex < parent.AfterTurnEnded.Items.Count)
        {
            if (parent.AfterTurnEnded.CurrentChild is not null)
                throw new InvalidOperationException("An after-turn-ended cursor retained its completed child.");
            var candidate = AfterTurnEndedCandidate(parent);
            var context = CreateAfterTurnEndedContext(parent, candidate);
            if (!CanRunProgramTrigger(candidate, context))
            {
                AdvanceAfterTurnEndedCandidate(parent, candidate, activated: false, completed: false);
                parent = (DeferredTurnEndFrame)_resolutionStack[^1];
                continue;
            }
            if (GetProgramTrigger(candidate).Optional)
            {
                ReplaceRuntimeTop(parent with { Step = ResolutionFrameStep.AwaitingResponse });
                ExposeProgramTriggerDecision(candidate, context);
                return true;
            }
            BeginProgramBinding(candidate, context);
            return true;
        }
        return false;
    }

    private void AttachAfterTurnEndedChild(ProgramSkillFrame child, ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.AfterTurnEnded) return;
        if (_resolutionStack.LastOrDefault() is not DeferredTurnEndFrame parent ||
            parent.AfterTurnEnded?.CurrentChild is not null || !CanRunAfterTurnEndedCandidate(candidate, context, GetProgramTrigger(candidate)))
            throw new InvalidOperationException("An after-turn-ended child requires its exact active candidate and immediate parent.");
        ReplaceRuntimeTop(parent with { Step = ResolutionFrameStep.ResolvingEffect,
            AfterTurnEnded = parent.AfterTurnEnded! with { CurrentChild = new(child.Id, candidate) } });
    }

    private void AdvanceAfterTurnEndedCandidate(DeferredTurnEndFrame parent, ProgramTriggerCandidate candidate, bool activated, bool completed)
    {
        if (_resolutionStack.LastOrDefault()?.Id != parent.Id || AfterTurnEndedCandidate(parent) != candidate || parent.AfterTurnEnded!.CurrentChild is not null)
            throw new InvalidOperationException("An after-turn-ended candidate cannot advance another item or a live child.");
        AdvanceEventRulesAndQueueFact(new ProgramBindingResolvedEvent(parent.Id, candidate.SkillId, candidate.BindingId,
            candidate.SkillInstanceId, candidate.OwnerSeat, SkillProgramTriggerWindow.AfterTurnEnded, activated, completed));
        ReplaceRuntimeTop(parent with { Step = ResolutionFrameStep.ResolvingEffect,
            AfterTurnEnded = parent.AfterTurnEnded with { ItemIndex = parent.AfterTurnEnded.ItemIndex + 1 } });
    }

    private bool MatchesAfterTurnEndedChild(DeferredTurnEndFrame parent, ProgramSkillFrame child, ProgramSkillWindowContext context)
    {
        if (!IsActualAfterTurnEndedParent(parent) || parent.AfterTurnEnded?.CurrentChild is not { } current) return false;
        var candidate = AfterTurnEndedCandidate(parent);
        return current.FrameId == child.Id && current.Candidate == candidate && context.Window == SkillProgramTriggerWindow.AfterTurnEnded &&
            context.ParentFrameId == parent.Id && context.OwnerSeat == candidate.OwnerSeat && context.SourceSeat == parent.OwnerSeat &&
            context.TargetSeat == parent.OwnerSeat && context.OccurrenceIndex == candidate.OccurrenceIndex &&
            context.Facts == parent.AfterTurnEnded.Items[parent.AfterTurnEnded.ItemIndex].Facts && child.OwnerSeat == candidate.OwnerSeat &&
            child.SkillId == candidate.SkillId && child.SkillInstanceId == candidate.SkillInstanceId && child.TriggerId == candidate.BindingId &&
            child.ActivationId == candidate.BindingId && child.GameplayHash == candidate.GameplayHash;
    }

    private bool CompleteAfterTurnEndedBinding(DeferredTurnEndFrame parent, ProgramSkillFrame child, ProgramSkillWindowContext context)
    {
        if (!MatchesAfterTurnEndedChild(parent, child, context))
            throw new InvalidOperationException("A finished after-turn-ended binding lost its exact child, candidate, parent or turn.");
        ReplaceRuntimeTop(parent with { Step = ResolutionFrameStep.ResolvingEffect,
            AfterTurnEnded = parent.AfterTurnEnded! with { CurrentChild = null, ItemIndex = parent.AfterTurnEnded.ItemIndex + 1 } });
        ContinueDeferredTurnEnd();
        return true;
    }

    private bool HasAfterTurnEndedBoundaryFrame(DeferredTurnEndFrame parent)
    {
        var candidate = AfterTurnEndedCandidate(parent);
        if (_resolutionStack.Count == 1 && parent.Step == ResolutionFrameStep.AwaitingResponse && parent.AfterTurnEnded!.CurrentChild is null &&
            GetProgramTrigger(candidate).Optional && _pendingDecision is { Kind: DecisionKind.ProgramTrigger } decision &&
            decision.PlayerSeat == candidate.OwnerSeat && decision.SourceSeat == parent.OwnerSeat && decision.TargetSeat == parent.OwnerSeat &&
            decision.Choices.Count == 2 && decision.Choices.All(choice =>
                choice.Parameters.GetValueOrDefault("skill-id") == candidate.SkillId &&
                choice.Parameters.GetValueOrDefault("binding-id") == candidate.BindingId &&
                choice.Parameters.GetValueOrDefault("skill-instance-id") == candidate.SkillInstanceId) &&
            decision.Choices.Select(choice => choice.Parameters.GetValueOrDefault("program-action")).Order(StringComparer.Ordinal).SequenceEqual(["activate", "skip"]))
            return true;
        if (_resolutionStack.Count < 2 || _resolutionStack[1] is not ProgramSkillFrame child || child.WindowContext is not { } context ||
            !MatchesAfterTurnEndedChild(parent, child, context))
            throw new InvalidOperationException("An after-turn-ended boundary lost its exact prompt or owning child.");
        return true;
    }
}
