namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool WuTuGuEndingParentMatches(ProgramSkillFrame f)=>f.WindowContext is{Window:SkillProgramTriggerWindow.TurnEnding,TargetSeat:{}target,SourceSeat:{}source}c&&target==f.OwnerSeat&&source==f.OwnerSeat&&f.OwnerSeat==_currentSeat&&
        _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(w=>w.Id==c.ParentFrameId)is{}end&&end.OwnerSeat==f.OwnerSeat&&end.TurnNumber==_turnNumber&&end.ItemIndex>=0&&end.ItemIndex<end.Items.Count&&end.Items[end.ItemIndex].Candidate is{}candidate&&MountObserverCandidateMatches(f,candidate);
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent,27320)]
    private void CaptureWuTuGuRanshangPayment(ProgramSkillHpLostEvent paid)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{WuTuGuRanshang:{Stage:WuTuGuRanshangStage.Issuing}r}f||paid.FrameId!=f.Id||paid.SkillId!=f.SkillId||paid.TargetSeat!=f.OwnerSeat||paid.Amount!=r.Amount)return;
        ReplaceRuntimeTop(f with{WuTuGuRanshang=r with{Stage=WuTuGuRanshangStage.HpChildren}});
        AdvanceEventRulesAndQueueFact(new WuTuGuRanshangPaidEvent(f.Id,paid.Amount,paid.RemainingHp));
    }
}
