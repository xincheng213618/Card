namespace CardGame.Core;

internal interface INativeFrameAutomaticFacts : IProgramSelectionQuery
{
    bool HasPendingDecision { get; }
    int StepCount(NativeFrameAutomaticPhase phase);
    NativeFrameAutomaticStepMetadata StepAt(NativeFrameAutomaticPhase phase, int index);
    bool Matches(NativeFrameAutomaticPhase phase, int index, ResolutionFrame frame);
}

internal interface INativeFrameAutomaticRules : IProgramRuntimeOperations
{
    void AdvanceRulesAndPublishState();
}

[ProgramRuntimeModule]
internal sealed class NativeFrameAutomaticRuntime(INativeFrameAutomaticFacts facts,
    IProgramRuntimeFrames frames, INativeFrameAutomaticRules rules)
{
    internal bool TryAdvance(NativeFrameAutomaticPhase phase)
    {
        for (var index = 0; index < facts.StepCount(phase); index++)
        {
            var step = facts.StepAt(phase, index);
            if (step.Pending == NativeFrameAutomaticPending.RequireNoneBeforeFrame && facts.HasPendingDecision) continue;
            var frame = frames.Top;
            if (frame is null || !facts.Matches(phase, index, frame)) continue;
            if (step.Pending == NativeFrameAutomaticPending.RequireNoneAfterFrame && facts.HasPendingDecision) continue;
            // Selection admits an automatic step; explicit routing still chooses
            // the first matching handler, including earlier CardUse receipts.
            frames.Advance(frame.Id);
            if (step.Publish == NativeFrameAutomaticPublish.AfterAdvance) rules.AdvanceRulesAndPublishState();
            return true;
        }
        return false;
    }
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class NativeFrameAutomaticFacts(GameEngine engine) : INativeFrameAutomaticFacts
    {
        public bool HasPendingDecision => engine._pendingDecision is not null;
        public int StepCount(NativeFrameAutomaticPhase phase) =>
            NativeFrameAutomaticStepCatalog.Default.Registrations[phase].Count;
        public NativeFrameAutomaticStepMetadata StepAt(NativeFrameAutomaticPhase phase, int index) =>
            NativeFrameAutomaticStepCatalog.Default.Registrations[phase][index].Step;
        public bool Matches(NativeFrameAutomaticPhase phase, int index, ResolutionFrame frame) =>
            NativeFrameAutomaticStepCatalog.Default.Registrations[phase][index].Matches(engine, frame);
    }

    [ProgramCapability]
    private sealed class NativeFrameAutomaticRules(GameEngine engine) : INativeFrameAutomaticRules
    {
        public void AdvanceRulesAndPublishState() => engine.AdvanceRulesAndPublishState();
    }
}
