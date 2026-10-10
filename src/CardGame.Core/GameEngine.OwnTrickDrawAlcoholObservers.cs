namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool OwnTrickDrawLegacyAlcoholRide(ProgramSkillFrame root, ActualUseTargetWindowFrame parent,
        int dyingIndex, DyingFrame dying)
    {
        var rootIndex = _resolutionStack.FindIndex(frame => frame.Id == root.Id);
        if (rootIndex < 1 || rootIndex + 1 >= dyingIndex ||
            _resolutionStack[rootIndex - 1] is not ActualUseTargetWindowFrame owningWindow || owningWindow.Id != parent.Id ||
            !OwnTrickDrawMatches(root, parent) || !OwnTrickDrawFirstChild(root, _resolutionStack[rootIndex + 1]) ||
            !ExactLegacyDyingAlcoholReturnRide(dyingIndex, dying)) return false;
        for (var index = rootIndex + 1; index <= dyingIndex; index++)
            if (!HalfHandPaidDamageObserverEdge(index)) return false;

        return true;
    }
}
