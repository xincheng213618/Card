namespace CardGame.Core;

internal sealed partial class MouDanchiRuntime
{
    private ProgramSkillFrame? MovementRoot()
    {
        foreach (var root in frames.OfType<ProgramSkillFrame>())
        {
            if (root.MouDanchi is not { Stage: MouDanchiStage.Children } r || !Valid(root) || root.PendingMovementContinuation is null ||
                frames.ChildOf(root.Id) is not { } child) continue;
            var first = child is CardsMovedTriggerWindowFrame moved && r.Batches.Contains(moved.Batch.Id) &&
                RuntimeSkills.RuntimeRules_Core_CommonRules.ProgramMovementReturnMatches(root, moved) ||
                child is ProgramLifecycleTriggerWindowFrame skills && RuntimeSkills.RuntimeRules_Core_ObserverSubtrees.IsObserverSkillsChangedEdge(root, skills);
            if (first && health.PaidObserverSuffix(root.Id)) return root;
        }
        return null;
    }
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 34101)]
    private bool DamageObserver(long windowId)
    {
        if (frames.Find(windowId) is not DamageTriggerWindowFrame || MovementRoot() is not { } root) return false;
        for (ResolutionFrame? current = root; current is not null; current = frames.ParentOf(current.Id))
            if (current.Id == windowId) return true;
        return false;
    }
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 34101, group: 34101)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 34101)]
    private bool DyingObserver()
    {
        if (health.ActiveDying is not { } dying || MovementRoot() is not { } root) return false;
        for (ResolutionFrame? current = dying; current is not null; current = frames.ParentOf(current.Id))
            if (current.Id == root.Id) return true;
        return false;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 34101)]
    private bool AdvanceMovementChildren() => MovementRoot() is not null && healthOperations.AdvanceObserverSubtree();
}
