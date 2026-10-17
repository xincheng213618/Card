namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 28132)]
    private void CaptureDouyuFinalResult(IGameEvent payload)
    {
        if (!HasDouyuCapability || payload is not PindianResultDeterminedEvent result ||
            _resolutionStack.LastOrDefault() is not { DouyuContest: { Paid: true } r } parent || parent.Id != result.FrameId) return;
        int comparison;
        if (parent is PindianFrame pair)
        {
            if (result.SkillId != pair.SkillId || !IsDouyuPairAssignment(pair, result.Result) ||
                (pair.Result is { } final ? final != result.Result :
                 result.Result.SourceRank != r.Assignments[0].Rank || result.Result.OpponentRank != r.Assignments[1].Rank))
                throw new InvalidOperationException("Douyu cannot qualify a fabricated native pair result.");
            comparison = 0;
        }
        else if (parent is ProgramSkillFrame { SimultaneousPindian: { ComparisonIssued: true } simultaneous } f)
        {
            if (result.SkillId != f.SkillId || SimultaneousResult(f) != result.Result) throw new InvalidOperationException("Douyu cannot qualify a different native common-group comparison.");
            comparison = simultaneous.ComparisonIndex;
        }
        else return;
        if (ProgramEventHistory<DouyuResultRecordedEvent>().Any(e => e.ContestId == parent.Id && e.ComparisonIndex == comparison))
            throw new InvalidOperationException("Douyu cannot repeat a settled native comparison.");
        AdvanceEventRulesAndQueueFact(new DouyuResultRecordedEvent(parent.Id, comparison, result.Result));
    }
    private bool TryBeginDouyuPairWinDraws(PindianFrame pair) => pair.DouyuContest is not null && pair.Result is { } result &&
        TryBeginDouyuWinWindow(pair, result, 0);
    private bool TryBeginDouyuSimultaneousWinDraws(ProgramSkillFrame f, PindianResult result) => f.DouyuContest is not null &&
        f.SimultaneousPindian is { } r && TryBeginDouyuWinWindow(f, result, r.ComparisonIndex);
    private bool DouyuSwappedPair(DouyuContestReceipt r, DouyuSwap swap, PindianResult result) =>
        result.SourceSeat == r.SourceSeat &&
        result.OpponentSeat == (swap.Source.OwnerSeat == r.SourceSeat ? swap.OpponentSeat : swap.Source.OwnerSeat);
    private bool TryBeginDouyuWinWindow(ResolutionFrame parent, PindianResult result, int comparison)
    {
        var r = parent.DouyuContest!;
        if (!r.Paid || _resolutionStack.LastOrDefault()?.Id != parent.Id || _pendingDecision is not null ||
            ProgramEventHistory<DouyuResultRecordedEvent>().Count(e => e.ContestId == parent.Id && e.ComparisonIndex == comparison && e.Result == result) != 1 ||
            ProgramEventHistory<PindianResultDeterminedEvent>().Count(e => e.FrameId == parent.Id && e.Result == result) != 1)
            throw new InvalidOperationException("Douyu win draws require the exact settled native result before its claims.");
        var entries = new List<DouyuWindowEntry>();
        foreach (var swap in r.Swaps.Where(s => DouyuSwappedPair(r, s, result)))
        {
            if (ProgramEventHistory<DouyuRewardClosedEvent>().Any(e => e.ContestId == parent.Id && e.SwapFrameId == swap.FrameId)) continue;
            if (!result.WonBy(swap.Source.OwnerSeat) || !_players[swap.Source.OwnerSeat].IsAlive)
            { AdvanceEventRulesAndQueueFact(new DouyuRewardClosedEvent(parent.Id, comparison, swap.FrameId, false)); continue; }
            var program = _contentRegistry.GetSkill(swap.Source.SkillId).Program!;
            var trigger = program.Triggers.Single(t => t.Id == "swapped-win");
            entries.Add(new(new(swap.Source.OwnerSeat, swap.Source.SkillId, trigger.Id, swap.Source.SkillInstanceId,
                swap.GameplayHash, trigger.Priority, swap.Ordinal), swap.FrameId));
        }
        if (entries.Count == 0) return false;
        PushRuntimeFrame(new DouyuWindowFrame(++_resolutionSequence, parent.Id, DouyuWindowStage.WinDraw, comparison, entries));
        ContinueDouyuWindow(_resolutionStack[^1].Id); return true;
    }
    private SkillProgramStepOutcome BeginDouyuWinDraw(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.Douyu!;
        if (r.Stage != DouyuStage.DrawWaiting || r.DrawStarted || r.SwapFrameId is not { } swap || !_players[f.OwnerSeat].IsAlive ||
            ProgramEventHistory<DouyuWinDrawStartedEvent>().Any(e => e.SwapFrameId == swap))
            throw new InvalidOperationException("Douyu cannot repeat or invent a swapped-pair win draw.");
        r = r with { DrawStarted = true, Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, Douyu = r });
        AdvanceEventRulesAndQueueFact(new DouyuWinDrawStartedEvent(f.Id, r.ContestId, r.ComparisonIndex, swap,
            r.IssuedSource, f.OwnerSeat, r.Before));
        return BeginOperationProgramDraw(f, f.OwnerSeat, 2, new(DouyuDrawReason));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveDouyuPreRevealSwapAndWinDraw)]
    private void ReturnDouyuWinDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.Douyu is not { DrawStarted: true, Stage: DouyuStage.DrawWaiting, SwapFrameId: { } swap } r ||
            f.NativeDraw is not null || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 2 || outcome.SequenceBefore < r.Before ||
            outcome.SequenceAfter < outcome.SequenceBefore || outcome.SequenceAfter > _movementSequence ||
            outcome.ActualDrawIds.Count > 2 || outcome.ActualDrawIds.Distinct().Count() != outcome.ActualDrawIds.Count ||
            outcome.ReplacementGainedIds.Distinct().Count() != outcome.ReplacementGainedIds.Count ||
            outcome.Replacement is not null && outcome.ActualDrawIds.Count != 0 ||
            ProgramEventHistory<DouyuWinDrawStartedEvent>().Count(e => e.FrameId == f.Id && e.ContestId == r.ContestId &&
                e.ComparisonIndex == r.ComparisonIndex && e.SwapFrameId == swap && e.Source == r.IssuedSource && e.RecipientSeat == f.OwnerSeat && e.Before == r.Before) != 1 ||
            ProgramEventHistory<DouyuWinDrawReturnedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Douyu lost its one actual native draw or replacement return.");
        if (outcome.AttemptFrameId is { } attempt && ProgramEventHistory<NativeDrawAttemptDecidedEvent>().Count(e =>
            e.AttemptFrameId == attempt && e.ParentFrameId == f.Id && e.RecipientSeat == f.OwnerSeat && e.RequestedCount == 2 &&
            e.Replaced == (outcome.Replacement is not null)) != 1)
            throw new InvalidOperationException("Douyu cannot accept another native draw attempt.");
        ReplaceRuntimeTop(f = f with { Douyu = r with { Stage = DouyuStage.DrawChildren, Before = outcome.SequenceBefore,
            After = outcome.SequenceAfter, AttemptFrameId = outcome.AttemptFrameId, ActualDrawIds = outcome.ActualDrawIds,
            ReplacementGainedIds = outcome.ReplacementGainedIds }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        for (var i = 0; i < outcome.ActualDrawIds.Count; i++) AdvanceEventRulesAndQueueFact(new DouyuWinDrawEntityEvent(f.Id, i, outcome.ActualDrawIds[i], false));
        for (var i = 0; i < outcome.ReplacementGainedIds.Count; i++) AdvanceEventRulesAndQueueFact(new DouyuWinDrawEntityEvent(f.Id, i, outcome.ReplacementGainedIds[i], true));
        AdvanceEventRulesAndQueueFact(new DouyuWinDrawReturnedEvent(f.Id, r.ContestId, r.ComparisonIndex, swap,
            f.OwnerSeat, 2, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        ContinueDouyuProgram(f.Id);
    }
    private bool DrainDouyu(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(f.Id) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id);
    private void ContinueDouyuProgram(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Douyu: { } r } f || f.Id != id || _pendingDecision is not null) return;
        AssertDouyuProgram(f);
        if (r.Stage == DouyuStage.OpponentChoice) { PublishDouyu(f); return; }
        if (f.NativeDraw is not null || r.Stage == DouyuStage.DrawWaiting) return;
        if (DrainDouyu(f)) return;
        AdvanceEventRulesAndQueueFact(new DouyuRewardClosedEvent(r.ContestId, r.ComparisonIndex, r.SwapFrameId!.Value, true));
        ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { Douyu = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 28132)]
    private bool ResumeDouyu(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Douyu: not null } f || f.Id != id || f.InstructionIndex == 0 || f.NativeDraw is not null) return false; ContinueDouyuProgram(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28132)]
    private bool ReturnDouyuMovement(ProgramSkillFrame f)
    { if (f.Douyu is null) return false; ContinueDouyuProgram(f.Id); return true; }
}
