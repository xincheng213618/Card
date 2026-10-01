namespace CardGame.Core;
public sealed partial class GameEngine
{
    private readonly Dictionary<int, long> _observedSkillGrantRevisions = [];
    private bool TryBeginAdvancedSkillsChanged(long parentFrameId)
    {
        foreach (var owner in _players.Where(player => player.IsAlive))
        {
            var changed = _observedSkillGrantRevisions.TryGetValue(owner.Seat, out var revision) && revision != owner.SkillGrants.Revision;
            _observedSkillGrantRevisions[owner.Seat] = owner.SkillGrants.Revision;
            if (!changed) continue;
            var facts = CaptureProgramTriggerFacts(owner);
            var candidates = CollectEligibleProgramTriggerCandidates(owner, SkillProgramTriggerWindow.SkillsChanged, facts);
            if (candidates.Count == 0) continue;
            PushRuntimeFrame(new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence, owner.Seat,
                SkillProgramTriggerWindow.SkillsChanged, candidates, ProgramLifecycleContinuation.ResumeParentProgram, facts)
                { ResumeProgramFrameId = parentFrameId });
            AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
            return true;
        }
        return false;
    }
}
