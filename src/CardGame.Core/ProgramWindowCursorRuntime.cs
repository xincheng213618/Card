namespace CardGame.Core;

[ProgramRuntimeModule, ProgramCapability]
internal sealed class ProgramWindowCursorRuntime(IProgramRuntimeFrames frames, IProgramRuntimeEvents events) : IProgramWindowCursors
{
    public void AdvanceTurnEndingBoundaryCursor(TurnEndingBoundaryFrame frame)
    {
        if (frames.Top is not TurnEndingBoundaryFrame current ||
            current.Id != frame.Id || current.ItemIndex != frame.ItemIndex)
            throw new InvalidOperationException("The turn-ending item cursor is no longer current.");
        frames.ReplaceTop(current with
        {
            ItemIndex = current.ItemIndex + 1,
            Step = ResolutionFrameStep.ResolvingEffect
        });
    }

    public void AdvancePlayPhaseStartingCursor(PlayPhaseStartingBoundaryFrame frame)
    {
        if (frames.Top is not PlayPhaseStartingBoundaryFrame current ||
            current.Id != frame.Id || current.ItemIndex != frame.ItemIndex)
            throw new InvalidOperationException("The play-phase-starting item cursor is no longer current.");
        frames.ReplaceTop(current with
        {
            ItemIndex = current.ItemIndex + 1,
            Step = ResolutionFrameStep.ResolvingEffect
        });
    }

    public void AdvanceProgramLifecycleCursor(ProgramLifecycleTriggerWindowFrame frame)
    {
        if (frames.Top is not ProgramLifecycleTriggerWindowFrame current ||
            current.Id != frame.Id || current.CandidateIndex != frame.CandidateIndex)
            throw new InvalidOperationException("The lifecycle trigger cursor is no longer current.");
        frames.ReplaceTop(current with { CandidateIndex = current.CandidateIndex + 1 });
    }

    public void AdvanceCardsMovedProgramCursor(CardsMovedTriggerWindowFrame frame)
    {
        if (frames.Top is not CardsMovedTriggerWindowFrame current ||
            current.Id != frame.Id || current.CandidateIndex != frame.CandidateIndex)
            throw new InvalidOperationException("The cards-moved trigger cursor is no longer current.");
        frames.ReplaceTop(current with
        {
            CandidateIndex = current.CandidateIndex + 1,
            Step = ResolutionFrameStep.ResolvingEffect
        });
    }

    public void AdvanceHpChangedProgramCursor(HpChangedTriggerWindowFrame frame)
    {
        if (frames.Top != frame)
            throw new InvalidOperationException("The HP-change trigger cursor is no longer current.");
        frames.ReplaceTop(frame with { CandidateIndex = frame.CandidateIndex + 1, Step = ResolutionFrameStep.ResolvingEffect });
    }

    public void AdvanceOwnerDiedProgramCursor(ProgramDeathTriggerWindowFrame frame)
    {
        if (frames.Top is not ProgramDeathTriggerWindowFrame current ||
            current.Id != frame.Id)
            throw new InvalidOperationException("The owner-death program cursor is not active.");
        frames.ReplaceTop(current with { CandidateIndex = current.CandidateIndex + 1 });
    }

    public void AdvanceBeforeDamageProgramCandidate(
        BeforeDamageProgramWindowFrame frame,
        bool activated,
        bool completed)
    {
        if (frames.Top is not BeforeDamageProgramWindowFrame current ||
            current.Id != frame.Id || current.CandidateIndex != frame.CandidateIndex)
            throw new InvalidOperationException("The before-damage program cursor is no longer current.");
        var candidate = current.Candidates[current.CandidateIndex].Candidate;
        events.AdvanceFact(new ProgramBindingResolvedEvent(
            current.Id,
            candidate.SkillId,
            candidate.BindingId,
            candidate.SkillInstanceId,
            candidate.OwnerSeat,
            SkillProgramTriggerWindow.BeforeDamageApplied,
            activated,
            completed));
        frames.ReplaceTop(current with
        {
            CandidateIndex = current.CandidateIndex + 1,
            Step = ResolutionFrameStep.ResolvingEffect
        });
    }

    public void AdvanceProgramCardCandidate(ProgramCardTriggerWindowFrame frame)
    {
        var candidate = frame.Candidates[frame.CandidateIndex];
        events.AdvanceFact(new ProgramCardTriggerResolvedEvent(frame.Id, candidate.SkillId, candidate.TriggerId,
            candidate.OwnerSeat, candidate.OpponentSeat, frame.Activated));
        frames.ReplaceTop(frame with
        {
            CandidateIndex = frame.CandidateIndex + 1,
            Activated = false
        });
    }

    public void AdvanceProgramJudgmentCandidate(ProgramJudgmentTriggerWindowFrame frame)
    {
        var candidate = frame.Candidates[frame.CandidateIndex];
        events.AdvanceFact(new ProgramJudgmentTriggerResolvedEvent(
            frame.Id,
            frame.Judgment.JudgmentFrameId,
            candidate.SkillId,
            candidate.TriggerId,
            candidate.OwnerSeat,
            frame.Activated));
        frames.ReplaceTop(frame with
        {
            CandidateIndex = frame.CandidateIndex + 1,
            Activated = false
        });
    }
}
