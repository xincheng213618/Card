namespace CardGame.Core;

internal enum ProgramTriggerWindowFlow { Ready, Retry, Yield }

internal readonly record struct ProgramTriggerWindowPreparation<TFrame>(ProgramTriggerWindowFlow Flow, TFrame Frame);

internal readonly record struct ProgramTriggerWindowEntry(
    ProgramTriggerCandidate Candidate,
    ProgramSkillWindowContext Context,
    SkillProgramTrigger? Trigger = null);

/// <summary>Access to one typed, currently active window. It never owns pending state.</summary>
internal interface IProgramTriggerWindowHost<TFrame> where TFrame : ResolutionFrame
{
    bool TryGetFrame(out TFrame frame);
    ProgramTriggerWindowPreparation<TFrame> Prepare(TFrame frame);
    bool IsComplete(TFrame frame);
    ProgramTriggerWindowFlow Close(TFrame frame);
    ProgramTriggerWindowEntry Read(TFrame frame);
    bool CanRun(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context);
    SkillProgramTrigger ResolveTrigger(ProgramTriggerCandidate candidate);
    void Skip(TFrame frame, ProgramTriggerCandidate candidate);
    IReadOnlyList<ProgramTriggerCandidate>? ChoiceGroup(TFrame frame, ProgramTriggerCandidate first, string group);
    void BeforePrompt(TFrame frame);
    void Expose(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context);
    void ExposeGroup(IReadOnlyList<ProgramTriggerCandidate> members, ProgramTriggerCandidate first,
        ProgramSkillWindowContext context, string group);
    void Begin(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context);
    int CandidateIndex(TFrame frame);
    void RecordGroupSkip(TFrame frame, ProgramTriggerCandidate candidate, ProgramSkillWindowContext context);
    void MovePastGroup(TFrame frame, int nextIndex);
    void Continue();
}

/// <summary>The shared candidate algorithm; typed hosts retain preparation, cursors and closing returns.</summary>
internal static class ProgramTriggerWindowDriver
{
    internal static void Run<TFrame>(IProgramTriggerWindowHost<TFrame> host) where TFrame : ResolutionFrame
    {
        while (host.TryGetFrame(out var frame))
        {
            var preparation = host.Prepare(frame);
            if (preparation.Flow == ProgramTriggerWindowFlow.Retry) continue;
            if (preparation.Flow == ProgramTriggerWindowFlow.Yield) return;
            frame = preparation.Frame;
            if (host.IsComplete(frame))
            {
                if (host.Close(frame) == ProgramTriggerWindowFlow.Retry) continue;
                // Closing may synchronously start another window. Its caller owns that continuation.
                return;
            }
            var entry = host.Read(frame);
            if (!host.CanRun(entry.Candidate, entry.Context))
            {
                host.Skip(frame, entry.Candidate);
                continue;
            }
            // Card-action windows resolve and check their definition before eligibility;
            // the other windows intentionally resolve the trigger after eligibility.
            var trigger = entry.Trigger ?? host.ResolveTrigger(entry.Candidate);
            if (trigger.ChoiceGroup is { } group &&
                host.ChoiceGroup(frame, entry.Candidate, group) is { } members)
            {
                if (members.Count > 1 || trigger.Optional)
                {
                    host.BeforePrompt(frame);
                    host.ExposeGroup(members, entry.Candidate, entry.Context, group);
                }
                else host.Begin(entry.Candidate, entry.Context);
                return;
            }
            if (trigger.Optional)
            {
                host.BeforePrompt(frame);
                host.Expose(entry.Candidate, entry.Context);
                return;
            }
            host.Begin(entry.Candidate, entry.Context);
            return;
        }
    }

    internal static void ResolveChoiceGroup<TFrame>(IProgramTriggerWindowHost<TFrame> host, TFrame frame,
        ProgramTriggerCandidate first, ProgramSkillWindowContext context, string group,
        PromptChoice selected, string? action, Action clearDecision) where TFrame : ResolutionFrame
    {
        var members = host.ChoiceGroup(frame, first, group) ??
            throw new InvalidOperationException("The active window does not support program choice groups.");
        if (selected.Parameters.GetValueOrDefault("choice-group") != group ||
            selected.Cards.Count != 0 || selected.Targets.Count != 0)
            throw new InvalidOperationException("The program choice group identity changed.");
        clearDecision();
        if (action == "skip")
        {
            if (!host.ResolveTrigger(first).Optional)
                throw new InvalidOperationException("A mandatory program choice group cannot be skipped.");
            foreach (var member in members) host.RecordGroupSkip(frame, member, context);
            host.MovePastGroup(frame, host.CandidateIndex(frame) + members.Count);
            host.Continue();
            return;
        }
        if (action != "activate")
            throw new InvalidOperationException("Unsupported program choice-group action.");
        var candidate = members.SingleOrDefault(member =>
            selected.Parameters.GetValueOrDefault("skill-id") == member.SkillId &&
            selected.Parameters.GetValueOrDefault("binding-id") == member.BindingId &&
            selected.Parameters.GetValueOrDefault("skill-instance-id") == member.SkillInstanceId) ??
            throw new InvalidOperationException("The selected program branch is not in the pending choice group.");
        foreach (var member in members.Where(member => member != candidate))
            host.RecordGroupSkip(frame, member, context);
        var nextIndex = host.CandidateIndex(frame) + members.Count;
        var selectedContext = context with
        {
            OccurrenceIndex = candidate.OccurrenceIndex,
            ResumeCandidateIndex = nextIndex
        };
        if (!host.CanRun(candidate, selectedContext))
        {
            host.RecordGroupSkip(frame, candidate, context);
            host.MovePastGroup(frame, nextIndex);
            host.Continue();
            return;
        }
        host.Begin(candidate, selectedContext);
    }
}
