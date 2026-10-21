namespace CardGame.Core;

internal sealed partial class MoSimaYiRuntime
{
    private ProgramSkillFrame? MovementObserverRoot()
    {
        foreach (var root in frames.OfType<ProgramSkillFrame>())
        {
            if (!Valid(root) || root.PendingMovementContinuation is null || frames.ChildOf(root.Id) is not { } child) continue;
            var owned = child is CardsMovedTriggerWindowFrame movement &&
                RuntimeSkills.RuntimeRules_Core_CommonRules.ProgramMovementReturnMatches(root, movement) ||
                child is ProgramLifecycleTriggerWindowFrame skills &&
                RuntimeSkills.RuntimeRules_Core_ObserverSubtrees.IsObserverSkillsChangedEdge(root, skills);
            if (owned && health.PaidObserverSuffix(root.Id)) return root;
        }
        return null;
    }

    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 33900)]
    private bool OwnsMovementDamageObserver(long windowId)
    {
        if (frames.Find(windowId) is not DamageTriggerWindowFrame || MovementObserverRoot() is not { } root) return false;
        for (ResolutionFrame? current = root; current is not null; current = frames.ParentOf(current.Id))
            if (current.Id == windowId) return true;
        return false;
    }
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 33900, group: 33900)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 33900)]
    private bool OwnsMovementDying()
    {
        if (health.ActiveDying is not { } dying || MovementObserverRoot() is not { } root) return false;
        for (ResolutionFrame? current = dying; current is not null; current = frames.ParentOf(current.Id))
            if (current.Id == root.Id) return true;
        return false;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 33900)]
    private bool AdvanceMovementChildren() => MovementObserverRoot() is not null && healthOperations.AdvanceObserverSubtree();
}
