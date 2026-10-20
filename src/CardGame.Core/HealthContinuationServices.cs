namespace CardGame.Core;

// Indexed facts retain the original first/last search and stack traversal order.
// No mutable frame, movement or HP-journal collection crosses this boundary.
internal interface IHealthContinuationFacts : IProgramSelectionQuery
{
    int FrameCount { get; }
    ResolutionFrame FrameAt(int index);
    int FrameIndex(long id);
    int PlayerCount { get; }
    bool IsValidPlayerSeat(int seat);
    int MovementCount { get; }
    CardMovementRecord MovementAt(int index);
    int PendingHpChangeCount { get; }
    HpChangeContext PendingHpChangeAt(int index);
    DyingFrame? ActiveDying { get; }
    DamageTriggerWindowFrame? ActiveDamageTrigger { get; }
    SkillProgram? Program(string skillId);
    SkillProgram RequireProgram(string skillId);
    IEnumerable<IGameEvent> CompleteProgramEventHistory();
    IReadOnlyList<T> ProgramEventHistory<T>() where T : IGameEvent;
    int ResolveProgramParticipant(ProgramSkillFrame frame, ProgramParticipantReference reference);
}

// These are existing neighboring protocols: native payment/response returns,
// ordinary post-event edges, and recovery amount support. Cursor ownership and
// recovery receipts are evaluated by HealthContinuationRuntime itself.
internal interface IHealthContinuationProtocols : IProgramRuntimeOperations
{
    bool IsPaidCardUseMovementReturn(CardUseFrame use, CardsMovedTriggerWindowFrame payment);
    bool HasExactAcceptedActualHandGainUse(CardUseFrame use, CardActionContext action);
    bool ValidResponseCompletionWindow(ProgramCardTriggerWindowFrame window);
    bool IsResponseCompletionHealthChild(ProgramCardTriggerWindowFrame window, ResolutionFrame child);
    bool IsWenhePoisonDyingChild(ResolutionFrame? parent, DyingFrame child);
    bool TypedDamagePostEventEdge(ResolutionFrame parent, ResolutionFrame child);
    bool TypedDamageObserverCandidateMatches(ProgramSkillFrame observer, ProgramTriggerCandidate candidate);
    void AssertEquipmentPlacementAndHpPair(ProgramSkillFrame producer, ProgramExecutionPlan plan);
    int SupportedRecoveryAmount(RecoveryReplacementFrame recovery, int baseAmount);
    int SupportedRecoveryHpBefore(RecoveryReplacementFrame recovery, int fallback);
    bool IsRecoveryParent(ProgramRecoveryParentPhase phase, ProgramSkillFrame parent, RecoveryReplacementFrame child);
    bool NativeCardRecoveryReturnMatches(CardUseFrame use, HpChangedTriggerWindowFrame hp, IReadOnlyList<IGameEvent> history);
    bool ExactPaidNativeRecoveryUseEdge(ResolutionFrame parent, ResolutionFrame child, IReadOnlyList<IGameEvent> history);
    bool ExactPaidNativeRecoveryReplacementEdge(ResolutionFrame parent, ResolutionFrame child, IReadOnlyList<IGameEvent> history);
    bool DamageProgramMatchesWindow(ProgramSkillFrame root, DamageTriggerWindowFrame window);
    bool IsAmountSupportHealthChild(RecoveryReplacementFrame recovery, HpChangedTriggerWindowFrame hp);
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class HealthContinuationFacts(GameEngine engine) : IHealthContinuationFacts
    {
        public int FrameCount => engine._resolutionStack.Count;
        public ResolutionFrame FrameAt(int index) => engine._resolutionStack[index];
        public int FrameIndex(long id) => engine._resolutionStack.FindIndex(frame => frame.Id == id);
        public int PlayerCount => engine._players.Count;
        public bool IsValidPlayerSeat(int seat) => engine.IsValidPlayerSeat(seat);
        public int MovementCount => engine._cardMovements.Count;
        public CardMovementRecord MovementAt(int index) => engine._cardMovements[index];
        public int PendingHpChangeCount => engine._pendingHpChanges.Count;
        public HpChangeContext PendingHpChangeAt(int index) => engine._pendingHpChanges[index];
        public DyingFrame? ActiveDying => engine.ActiveDying;
        public DamageTriggerWindowFrame? ActiveDamageTrigger => engine.ActiveDamageTrigger;
        public SkillProgram? Program(string skillId) => engine._contentRegistry.Skills.TryGetValue(skillId, out var skill)
            ? skill.Program : null;
        public SkillProgram RequireProgram(string skillId) => engine._contentRegistry.GetSkill(skillId).Program!;
        public IEnumerable<IGameEvent> CompleteProgramEventHistory() => engine.CompleteProgramEventHistory();
        public IReadOnlyList<T> ProgramEventHistory<T>() where T : IGameEvent => engine.ProgramEventHistory<T>();
        public int ResolveProgramParticipant(ProgramSkillFrame frame, ProgramParticipantReference reference) =>
            engine.ResolveProgramParticipant(frame, reference);
    }

    [ProgramCapability]
    private sealed class HealthContinuationProtocols(GameEngine engine) : IHealthContinuationProtocols
    {
        public bool IsPaidCardUseMovementReturn(CardUseFrame use, CardsMovedTriggerWindowFrame payment) =>
            engine.IsPaidCardUseMovementReturn(use, payment);
        public bool HasExactAcceptedActualHandGainUse(CardUseFrame use, CardActionContext action) =>
            engine.HasExactAcceptedActualHandGainUse(use, action);
        public bool ValidResponseCompletionWindow(ProgramCardTriggerWindowFrame window) =>
            engine.ValidResponseCompletionWindow(window);
        public bool IsResponseCompletionHealthChild(ProgramCardTriggerWindowFrame window, ResolutionFrame child) =>
            engine.IsResponseCompletionHealthChild(window, child);
        public bool IsWenhePoisonDyingChild(ResolutionFrame? parent, DyingFrame child) =>
            engine.IsWenhePoisonDyingChild(parent, child);
        public bool TypedDamagePostEventEdge(ResolutionFrame parent, ResolutionFrame child) =>
            engine.TypedDamagePostEventEdge(parent, child);
        public bool TypedDamageObserverCandidateMatches(ProgramSkillFrame observer, ProgramTriggerCandidate candidate) =>
            engine.TypedDamageObserverCandidateMatches(observer, candidate);
        public void AssertEquipmentPlacementAndHpPair(ProgramSkillFrame producer, ProgramExecutionPlan plan) =>
            engine.AssertEquipmentPlacementAndHpPair(producer, plan);
        public int SupportedRecoveryAmount(RecoveryReplacementFrame recovery, int baseAmount) =>
            engine.SupportedRecoveryAmount(recovery, baseAmount);
        public int SupportedRecoveryHpBefore(RecoveryReplacementFrame recovery, int fallback) =>
            engine.SupportedRecoveryHpBefore(recovery, fallback);
        public bool IsRecoveryParent(ProgramRecoveryParentPhase phase, ProgramSkillFrame parent, RecoveryReplacementFrame child) =>
            engine.RuntimeHooks.IsRecoveryParent(phase, parent, child);
        public bool NativeCardRecoveryReturnMatches(CardUseFrame use, HpChangedTriggerWindowFrame hp, IReadOnlyList<IGameEvent> history) =>
            engine.NativeCardRecoveryReturnMatches(use, hp, history);
        public bool ExactPaidNativeRecoveryUseEdge(ResolutionFrame parent, ResolutionFrame child, IReadOnlyList<IGameEvent> history) =>
            engine.ExactPaidNativeRecoveryUseEdge(parent, child, history);
        public bool ExactPaidNativeRecoveryReplacementEdge(ResolutionFrame parent, ResolutionFrame child, IReadOnlyList<IGameEvent> history) =>
            engine.ExactPaidNativeRecoveryReplacementEdge(parent, child, history);
        public bool DamageProgramMatchesWindow(ProgramSkillFrame root, DamageTriggerWindowFrame window) =>
            engine.DamageProgramMatchesWindow(root, window);
        public bool IsAmountSupportHealthChild(RecoveryReplacementFrame recovery, HpChangedTriggerWindowFrame hp) =>
            engine.IsAmountSupportHealthChild(recovery, hp);
    }
}
