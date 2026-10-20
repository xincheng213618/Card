namespace CardGame.Core;

// Reads stay indexed so each validator preserves its original first/last frame
// search, short-circuit order and lazy event-history capture.
internal interface INativeHealthRecoveryFacts : IProgramSelectionQuery
{
    int FrameCount { get; }
    ResolutionFrame FrameAt(int index);
    int FrameIndex(long id);
    int FrameLastIndex(long id);
    IEnumerable<IGameEvent> CompleteProgramEventHistory();
    int ResolveProgramParticipant(ProgramSkillFrame frame, ProgramParticipantReference reference);
}

// Adjacent mechanisms retain their own exact receipt and return validators.
internal interface INativeHealthRecoveryProtocols : IProgramRuntimeOperations
{
    bool HulaoActivityFrameRidesOn(ResolutionFrame child, ResolutionFrame parent);
    bool RecoveryReplacementFrameRidesOn(ResolutionFrame child, ResolutionFrame parent);
    void ValidateHulaoPeachUse(CardUseFrame use);
    bool TypedRecoveryApplicationBoundsMatch(RecoveryReplacementFrame recovery, IReadOnlyList<IGameEvent> history);
    bool TypedDamagePostEventEdge(ResolutionFrame parent, ResolutionFrame child);
    bool DamageProgramMatchesWindow(ProgramSkillFrame root, DamageTriggerWindowFrame window);
    bool ProgramRecoveryReplacementReceiptMatches(ProgramSkillFrame producer, RecoveryReplacementFrame recovery,
        IReadOnlyList<IGameEvent> history);
    bool TypedRecoveryHpReturnMatches(RecoveryReplacementFrame recovery, HpChangedTriggerWindowFrame hp,
        IReadOnlyList<IGameEvent> history);
    bool ProgramRecoveryReceiptMatches(ProgramSkillFrame producer, HpChangedTriggerWindowFrame hp,
        IReadOnlyList<IGameEvent> history);
    SkillProgramEffect? TypedRecoveryPausedEffect(ProgramSkillFrame producer);
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class NativeHealthRecoveryFacts(GameEngine engine) : INativeHealthRecoveryFacts
    {
        public int FrameCount => engine._resolutionStack.Count;
        public ResolutionFrame FrameAt(int index) => engine._resolutionStack[index];
        public int FrameIndex(long id) => engine._resolutionStack.FindIndex(frame => frame.Id == id);
        public int FrameLastIndex(long id) => engine._resolutionStack.FindLastIndex(frame => frame.Id == id);
        public IEnumerable<IGameEvent> CompleteProgramEventHistory() => engine.CompleteProgramEventHistory();
        public int ResolveProgramParticipant(ProgramSkillFrame frame, ProgramParticipantReference reference) =>
            engine.ResolveProgramParticipant(frame, reference);
    }

    [ProgramCapability]
    private sealed class NativeHealthRecoveryProtocols(GameEngine engine) : INativeHealthRecoveryProtocols
    {
        public bool HulaoActivityFrameRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
            engine.HulaoActivityFrameRidesOn(child, parent);
        public bool RecoveryReplacementFrameRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
            engine.RecoveryReplacementFrameRidesOn(child, parent);
        public void ValidateHulaoPeachUse(CardUseFrame use) => engine.ValidateHulaoPeachUse(use);
        public bool TypedRecoveryApplicationBoundsMatch(RecoveryReplacementFrame recovery, IReadOnlyList<IGameEvent> history) =>
            engine.TypedRecoveryApplicationBoundsMatch(recovery, history);
        public bool TypedDamagePostEventEdge(ResolutionFrame parent, ResolutionFrame child) =>
            engine.TypedDamagePostEventEdge(parent, child);
        public bool DamageProgramMatchesWindow(ProgramSkillFrame root, DamageTriggerWindowFrame window) =>
            engine.DamageProgramMatchesWindow(root, window);
        public bool ProgramRecoveryReplacementReceiptMatches(ProgramSkillFrame producer, RecoveryReplacementFrame recovery,
            IReadOnlyList<IGameEvent> history) => engine.ProgramRecoveryReplacementReceiptMatches(producer, recovery, history);
        public bool TypedRecoveryHpReturnMatches(RecoveryReplacementFrame recovery, HpChangedTriggerWindowFrame hp,
            IReadOnlyList<IGameEvent> history) => engine.TypedRecoveryHpReturnMatches(recovery, hp, history);
        public bool ProgramRecoveryReceiptMatches(ProgramSkillFrame producer, HpChangedTriggerWindowFrame hp,
            IReadOnlyList<IGameEvent> history) => engine.ProgramRecoveryReceiptMatches(producer, hp, history);
        public SkillProgramEffect? TypedRecoveryPausedEffect(ProgramSkillFrame producer) => engine.TypedRecoveryPausedEffect(producer);
    }
}
