namespace CardGame.Core;

public sealed partial class GameEngine
{
    // These adapters exist only for the current call. All cursor and pending state stays on its owning frame.
    private ProgramTriggerWindowHost? CurrentDrivenProgramTriggerWindow() => _resolutionStack.LastOrDefault() switch
    {
        ProgramLifecycleTriggerWindowFrame => new LifecycleTriggerWindowHost(this),
        PlayPhaseStartingBoundaryFrame => new PlayStartingTriggerWindowHost(this),
        CardsMovedTriggerWindowFrame => new CardsMovedTriggerWindowHost(this),
        HpChangedTriggerWindowFrame => new HpChangedTriggerWindowHost(this),
        ProgramCardTriggerWindowFrame => new CardActionTriggerWindowHost(this),
        _ => null
    };

    private bool TryGetDrivenProgramTriggerCandidate(out ProgramTriggerCandidate candidate, out ProgramSkillWindowContext context)
    {
        if (CurrentDrivenProgramTriggerWindow() is { } host && host.TryReadPending(out var entry))
        { candidate = entry.Candidate; context = entry.Context; return true; }
        candidate = null!;
        context = null!;
        return false;
    }

    private bool TrySkipDrivenProgramCandidate(ProgramTriggerCandidate candidate) =>
        CurrentDrivenProgramTriggerWindow()?.TrySkip(candidate) == true;

    private bool TryResolveDrivenProgramTriggerGroupChoice(ProgramTriggerCandidate candidate,
        ProgramSkillWindowContext context, PromptChoice selected, string? action) =>
        CurrentDrivenProgramTriggerWindow()?.TryResolveGroup(candidate, context, selected, action) == true;

    private abstract class ProgramTriggerWindowHost(GameEngine engine)
    {
        protected GameEngine Engine { get; } = engine;
        internal abstract bool TryReadPending(out ProgramTriggerWindowEntry entry);
        internal abstract bool TrySkip(ProgramTriggerCandidate candidate);
        internal abstract bool TryResolveGroup(ProgramTriggerCandidate first, ProgramSkillWindowContext context,
            PromptChoice selected, string? action);
    }

    private abstract class ProgramTriggerWindowHost<TFrame>(GameEngine engine) : ProgramTriggerWindowHost(engine),
        IProgramTriggerWindowHost<TFrame> where TFrame : ResolutionFrame
    {
        public bool TryGetFrame(out TFrame frame)
        {
            if (Engine._resolutionStack.LastOrDefault() is TFrame current) { frame = current; return true; }
            frame = null!; return false;
        }
        public virtual ProgramTriggerWindowPreparation<TFrame> Prepare(TFrame frame) => new(ProgramTriggerWindowFlow.Ready, frame);
        public abstract bool IsComplete(TFrame frame);
        public abstract ProgramTriggerWindowFlow Close(TFrame frame);
        protected abstract ProgramTriggerCandidate Candidate(TFrame frame);
        protected virtual bool MatchesSkipCandidate(TFrame frame, ProgramTriggerCandidate candidate) => Candidate(frame) == candidate;
        public abstract ProgramTriggerWindowEntry Read(TFrame frame);
        protected virtual bool CanReadPending(TFrame frame) => true;
        protected virtual ProgramTriggerWindowEntry ReadPending(TFrame frame) => Read(frame);
        public bool CanRun(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) => Engine.CanRunProgramTrigger(candidate, context);
        public SkillProgramTrigger ResolveTrigger(ProgramTriggerCandidate candidate) => Engine.GetProgramTrigger(candidate);
        public abstract void Skip(TFrame frame, ProgramTriggerCandidate candidate);
        public virtual IReadOnlyList<ProgramTriggerCandidate>? ChoiceGroup(TFrame frame, ProgramTriggerCandidate first, string group) => null;
        protected virtual bool SupportsChoiceGroups => false;
        public virtual void BeforePrompt(TFrame frame) { }
        public void Expose(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) => Engine.ExposeProgramTriggerDecision(candidate, context);
        public void ExposeGroup(IReadOnlyList<ProgramTriggerCandidate> members, ProgramTriggerCandidate first,
            ProgramSkillWindowContext context, string group) => Engine.ExposeProgramTriggerGroupDecision(members, first, context, group);
        public void Begin(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) => Engine.BeginProgramBinding(candidate, context);
        public virtual int CandidateIndex(TFrame frame) => throw new InvalidOperationException("This window has no program choice-group cursor.");
        public void RecordGroupSkip(TFrame frame, ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
            Engine.AdvanceEventRulesAndQueueFact(new ProgramBindingResolvedEvent(frame.Id, candidate.SkillId,
                candidate.BindingId, candidate.SkillInstanceId, candidate.OwnerSeat, context.Window, Activated: false, Completed: false));
        public virtual void MovePastGroup(TFrame frame, int nextIndex) => throw new InvalidOperationException("This window has no program choice-group cursor.");
        public void Continue() => Engine.AdvanceRuntimeTop<TFrame>();
        internal override bool TryReadPending(out ProgramTriggerWindowEntry entry)
        {
            if (TryGetFrame(out var frame) && CanReadPending(frame)) { entry = ReadPending(frame); return true; }
            entry = default; return false;
        }
        internal override bool TrySkip(ProgramTriggerCandidate candidate)
        {
            if (!TryGetFrame(out var frame) || !MatchesSkipCandidate(frame, candidate)) return false;
            Skip(frame, candidate);
            Continue();
            return true;
        }
        internal override bool TryResolveGroup(ProgramTriggerCandidate first, ProgramSkillWindowContext context,
            PromptChoice selected, string? action)
        {
            if (!TryGetFrame(out var frame) || !SupportsChoiceGroups || ResolveTrigger(first).ChoiceGroup is not { } group) return false;
            ProgramTriggerWindowDriver.ResolveChoiceGroup(this, frame, first, context, group, selected, action, Engine.ClearPendingDecision);
            return true;
        }
    }

    private sealed class LifecycleTriggerWindowHost(GameEngine engine) : ProgramTriggerWindowHost<ProgramLifecycleTriggerWindowFrame>(engine)
    {
        protected override bool SupportsChoiceGroups => true;
        public override bool IsComplete(ProgramLifecycleTriggerWindowFrame frame) => frame.CandidateIndex >= frame.Candidates.Count;
        public override ProgramTriggerWindowFlow Close(ProgramLifecycleTriggerWindowFrame frame)
        { Engine.CompleteProgramLifecycleWindow(frame); return ProgramTriggerWindowFlow.Yield; }
        protected override ProgramTriggerCandidate Candidate(ProgramLifecycleTriggerWindowFrame frame) => frame.Candidates[frame.CandidateIndex];
        // The original lifecycle skip selected its current cursor without comparing the supplied candidate.
        protected override bool MatchesSkipCandidate(ProgramLifecycleTriggerWindowFrame frame, ProgramTriggerCandidate candidate) => true;
        public override ProgramTriggerWindowEntry Read(ProgramLifecycleTriggerWindowFrame frame) =>
            new(Candidate(frame), Engine.CreateLifecycleDrivingProgramContext(frame, Candidate(frame)));
        protected override ProgramTriggerWindowEntry ReadPending(ProgramLifecycleTriggerWindowFrame frame) =>
            new(Candidate(frame), Engine.CreateLifecyclePendingProgramContext(frame, Candidate(frame)));
        public override void Skip(ProgramLifecycleTriggerWindowFrame frame, ProgramTriggerCandidate candidate) =>
            Engine.AdvanceProgramLifecycleCandidate(frame, activated: false, completed: false);
        public override IReadOnlyList<ProgramTriggerCandidate> ChoiceGroup(ProgramLifecycleTriggerWindowFrame frame, ProgramTriggerCandidate first, string group) =>
            Engine.GetProgramTriggerChoiceGroup(frame, first, group);
        public override int CandidateIndex(ProgramLifecycleTriggerWindowFrame frame) => frame.CandidateIndex;
        public override void MovePastGroup(ProgramLifecycleTriggerWindowFrame frame, int nextIndex) => Engine.ReplaceRuntimeTop(frame with { CandidateIndex = nextIndex });
    }

    private sealed class PlayStartingTriggerWindowHost(GameEngine engine) : ProgramTriggerWindowHost<PlayPhaseStartingBoundaryFrame>(engine)
    {
        protected override bool SupportsChoiceGroups => true;
        public override bool IsComplete(PlayPhaseStartingBoundaryFrame frame) => frame.ItemIndex >= frame.Items.Count;
        public override ProgramTriggerWindowFlow Close(PlayPhaseStartingBoundaryFrame frame)
        { Engine.PopResolutionFrame(frame.Id, ResolutionFrameKind.PlayPhaseStartingBoundary); Engine.AdvanceRulesAndPublishState(); return ProgramTriggerWindowFlow.Yield; }
        protected override ProgramTriggerCandidate Candidate(PlayPhaseStartingBoundaryFrame frame) => frame.Items[frame.ItemIndex].Candidate ??
            throw new InvalidOperationException("The play-phase-starting program item lost its candidate.");
        protected override bool MatchesSkipCandidate(PlayPhaseStartingBoundaryFrame frame, ProgramTriggerCandidate candidate) =>
            frame.Items[frame.ItemIndex].Candidate == candidate;
        public override ProgramTriggerWindowEntry Read(PlayPhaseStartingBoundaryFrame frame)
        {
            var item = frame.Items[frame.ItemIndex];
            if (item.Kind != TurnEndingBoundaryItemKind.Program) throw new InvalidOperationException("Unsupported play-phase-starting boundary item.");
            var candidate = item.Candidate ?? throw new InvalidOperationException("A program play-phase-starting item lost its candidate.");
            return new(candidate, Engine.CreatePlayPhaseStartingProgramContext(frame, candidate, item.Facts));
        }
        protected override ProgramTriggerWindowEntry ReadPending(PlayPhaseStartingBoundaryFrame frame) =>
            new(Candidate(frame), Engine.CreatePlayPhaseStartingProgramContext(frame, Candidate(frame), frame.Items[frame.ItemIndex].Facts));
        public override void Skip(PlayPhaseStartingBoundaryFrame frame, ProgramTriggerCandidate candidate) =>
            Engine.AdvancePlayPhaseStartingCandidate(frame, candidate, activated: false, completed: false);
        public override IReadOnlyList<ProgramTriggerCandidate> ChoiceGroup(PlayPhaseStartingBoundaryFrame frame, ProgramTriggerCandidate first, string group) =>
            Engine.GetPlayPhaseStartingChoiceGroup(frame, first, group);
        public override void BeforePrompt(PlayPhaseStartingBoundaryFrame frame) => Engine.ReplaceRuntimeTop(frame with { Step = ResolutionFrameStep.AwaitingResponse });
        public override int CandidateIndex(PlayPhaseStartingBoundaryFrame frame) => frame.ItemIndex;
        public override void MovePastGroup(PlayPhaseStartingBoundaryFrame frame, int nextIndex) => Engine.ReplaceRuntimeTop(frame with { ItemIndex = nextIndex });
    }

    private sealed class CardsMovedTriggerWindowHost(GameEngine engine) : ProgramTriggerWindowHost<CardsMovedTriggerWindowFrame>(engine)
    {
        public override bool IsComplete(CardsMovedTriggerWindowFrame frame) => frame.CandidateIndex >= frame.Candidates.Count;
        public override ProgramTriggerWindowFlow Close(CardsMovedTriggerWindowFrame frame)
        { Engine.CompleteCardsMovedProgramWindow(frame); return ProgramTriggerWindowFlow.Yield; }
        protected override ProgramTriggerCandidate Candidate(CardsMovedTriggerWindowFrame frame) => frame.Candidates[frame.CandidateIndex];
        public override ProgramTriggerWindowEntry Read(CardsMovedTriggerWindowFrame frame) =>
            new(Candidate(frame), Engine.CreateCardsMovedProgramContext(frame, Candidate(frame)));
        public override void Skip(CardsMovedTriggerWindowFrame frame, ProgramTriggerCandidate candidate) =>
            Engine.AdvanceCardsMovedProgramCandidate(frame, candidate, activated: false, completed: false);
        public override void BeforePrompt(CardsMovedTriggerWindowFrame frame) => Engine.ReplaceRuntimeTop(frame with { Step = ResolutionFrameStep.AwaitingResponse });
    }

    private sealed class HpChangedTriggerWindowHost(GameEngine engine) : ProgramTriggerWindowHost<HpChangedTriggerWindowFrame>(engine)
    {
        public override bool IsComplete(HpChangedTriggerWindowFrame frame) => frame.CandidateIndex >= frame.Candidates.Count;
        public override ProgramTriggerWindowFlow Close(HpChangedTriggerWindowFrame frame)
        { Engine.CompleteHpChangedProgramWindow(frame); return ProgramTriggerWindowFlow.Yield; }
        protected override ProgramTriggerCandidate Candidate(HpChangedTriggerWindowFrame frame) => frame.Candidates[frame.CandidateIndex];
        public override ProgramTriggerWindowEntry Read(HpChangedTriggerWindowFrame frame) => new(Candidate(frame), frame.Contexts[frame.CandidateIndex]);
        public override void Skip(HpChangedTriggerWindowFrame frame, ProgramTriggerCandidate candidate) =>
            Engine.AdvanceHpChangedProgramCandidate(frame, activated: false, completed: false);
        public override void BeforePrompt(HpChangedTriggerWindowFrame frame) => Engine.ReplaceRuntimeTop(frame with { Step = ResolutionFrameStep.AwaitingResponse });
    }

    private sealed class CardActionTriggerWindowHost(GameEngine engine) : ProgramTriggerWindowHost<ProgramCardTriggerWindowFrame>(engine)
    {
        public override ProgramTriggerWindowPreparation<ProgramCardTriggerWindowFrame> Prepare(ProgramCardTriggerWindowFrame frame) => Engine.PrepareProgramCardWindow(frame);
        public override bool IsComplete(ProgramCardTriggerWindowFrame frame) => frame.CandidateIndex == frame.Candidates.Count;
        public override ProgramTriggerWindowFlow Close(ProgramCardTriggerWindowFrame frame) => Engine.CompleteProgramCardWindow(frame);
        protected override ProgramTriggerCandidate Candidate(ProgramCardTriggerWindowFrame frame) => ToSharedCandidate(frame.Candidates[frame.CandidateIndex]);
        protected override bool MatchesSkipCandidate(ProgramCardTriggerWindowFrame frame, ProgramTriggerCandidate candidate) =>
            frame.Candidates[frame.CandidateIndex] is { } current && ToSharedCandidate(current) == candidate;
        protected override bool CanReadPending(ProgramCardTriggerWindowFrame frame) => frame.Candidates[frame.CandidateIndex] is not null;
        public override ProgramTriggerWindowEntry Read(ProgramCardTriggerWindowFrame frame)
        {
            var candidate = frame.Candidates[frame.CandidateIndex];
            var program = Engine._contentRegistry.Skills[candidate.SkillId].Program!;
            if (program.GameplayHash != candidate.GameplayHash) throw new InvalidOperationException("A running card trigger definition changed.");
            var trigger = ProgramInstructionResolver.Default.Resolve(program, ProgramInstructionSourceKind.Trigger, candidate.TriggerId).Trigger!;
            return new(ToSharedCandidate(candidate), Engine.CreateCardActionProgramContext(frame, candidate), trigger);
        }
        // Prompt restoration originally reads only the frozen action context; it does not resolve the program again.
        protected override ProgramTriggerWindowEntry ReadPending(ProgramCardTriggerWindowFrame frame) =>
            new(Candidate(frame), Engine.CreateCardActionProgramContext(frame, frame.Candidates[frame.CandidateIndex]));
        public override void Skip(ProgramCardTriggerWindowFrame frame, ProgramTriggerCandidate candidate) => Engine.AdvanceProgramCardCandidate(frame);
    }
}
