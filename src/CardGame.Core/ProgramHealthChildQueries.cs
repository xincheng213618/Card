namespace CardGame.Core;

// Native adjacency queries shared by custom operation runtimes. The operation
// proves its first child; this port reuses existing native observer/rescue
// proofs for the suffix and the common frame-owned recovery invoices.
internal interface IProgramHealthChildQueries : IProgramSelectionQuery
{
    DyingFrame? ActiveDying { get; }
    HpChangeContext? LastPendingLoss(long parentFrameId, int targetSeat);
    bool ProgramRecoveryChild(ProgramSkillFrame parent, ResolutionFrame child);
    bool PaidObserverSuffix(long programFrameId);
}
internal interface IProgramHealthChildOperations : IProgramRuntimeOperations
{
    bool AdvanceObserverSubtree();
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class ProgramHealthChildQueries(GameEngine engine) : IProgramHealthChildQueries
    {
        public DyingFrame? ActiveDying => engine.ActiveDying;
        public HpChangeContext? LastPendingLoss(long parentFrameId, int targetSeat) => engine._pendingHpChanges
            .LastOrDefault(c => c.ParentFrameId == parentFrameId && c.TargetSeat == targetSeat && c.Kind == HpChangeKind.Loss);
        public bool ProgramRecoveryChild(ProgramSkillFrame parent, ResolutionFrame child) => child switch
        {
            RecoveryReplacementFrame recovery => engine.ProgramRecoveryReplacementReceiptMatches(parent, recovery,
                engine.CompleteProgramEventHistory().ToArray()),
            HpChangedTriggerWindowFrame hp => engine.ProgramRecoveryReceiptMatches(parent, hp,
                engine.CompleteProgramEventHistory().ToArray()),
            _ => false
        };
        public bool PaidObserverSuffix(long programFrameId)
        {
            var index = engine._resolutionStack.FindIndex(f => f.Id == programFrameId);
            if (index < 0 || index + 1 >= engine._resolutionStack.Count) return false;
            if (engine._resolutionStack[index + 1] is DyingFrame dying && index + 2 < engine._resolutionStack.Count &&
                engine.IsSuspendedOrPaidRescueObserverSuffix(index + 1, dying)) return true;
            return engine.HasPaidDamageObserverSuffix(index);
        }
    }
    [ProgramCapability]
    private sealed class ProgramHealthChildOperations(GameEngine engine) : IProgramHealthChildOperations
    {
        public bool AdvanceObserverSubtree() => engine._pendingDecision is null && engine.TryAdvanceObserverSubtree(true);
    }
}
