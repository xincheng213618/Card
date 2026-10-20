namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool CaiMaoDamageParentMatches(ProgramSkillFrame f,ProgramSkillWindowContext context)=>
        context.Window==SkillProgramTriggerWindow.BeforeDamageApplied&&context.OwnerSeat==f.OwnerSeat&&context.TargetSeat==f.OwnerSeat&&context.Amount>0&&
        _resolutionStack.OfType<BeforeDamageProgramWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is{}parent&&parent.TargetSeat==f.OwnerSeat&&parent.Amount==context.Amount&&!parent.Prevented&&parent.RedirectedTargetSeat is null&&
        parent.CandidateIndex>=0&&parent.CandidateIndex<parent.Candidates.Count&&MountObserverCandidateMatches(f,parent.Candidates[parent.CandidateIndex].Candidate);
}
