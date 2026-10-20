namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsExactNingwuFinalizedChildSubtree(ProgramJudgmentTriggerWindowFrame frame, JudgmentFrame? judgment, int index)
    {
        if (judgment is null || !frame.Activated || frame.ZhenyiBeforeResult == true || index < 2 || index + 1 >= _resolutionStack.Count || _resolutionStack[index] != frame || _resolutionStack[index - 1] != judgment || _resolutionStack[index - 2] is not ProgramSkillFrame { ZhouQun: not null } root || !IsNingwuJudgment(judgment, root) || !DyingSuitsStructuralEdge(judgment, frame) || frame.CandidateIndex < 0 || frame.CandidateIndex >= frame.Candidates.Count || _resolutionStack[index + 1] is not ProgramSkillFrame child || child.WindowContext is not { Window: SkillProgramTriggerWindow.JudgmentFinalized, Judgment: { } final } c || c.ParentFrameId != frame.Id || final != frame.Judgment) return false;
        var candidate = frame.Candidates[frame.CandidateIndex];
        return candidate.OwnerSeat == child.OwnerSeat && candidate.SkillId == child.SkillId && candidate.TriggerId == child.TriggerId && candidate.SkillInstanceId == child.SkillInstanceId && candidate.GameplayHash == child.GameplayHash && FindProgramObserverRoot(static (e, f, p) => e.ZhouQunFirstChild(f, p), static (e, i) => e.SameNameHandObserverSuffix(i), root.Id) is not null;
    }
}
