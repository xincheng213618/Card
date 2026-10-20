namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsBinghuoHandoffMovement(MaYuanYiReceipt r,CardMovementRecord m)=>r.JudgmentHandoffSequence==m.Sequence&&m.Sequence<=r.Before&&r.JudgmentCardId==m.CardId&&r.TargetSeat is{}target&&m.From==CardLocation.Judgment(target)&&m.To==CardLocation.Processing&&m.Reason.Value=="skill-program.judgment.result";
    private bool IsBinghuoCleanupMovement(MaYuanYiReceipt r,CardMovementRecord m)=>r.JudgmentCardId==m.CardId&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.JudgmentFinish&&
        _cardMovements.LastOrDefault(p=>p.Sequence<=r.Before&&p.CardId==m.CardId)is{}entry&&IsBinghuoHandoffMovement(r,entry);
    private bool IsExactBinghuoFinalizedChildSubtree(ProgramJudgmentTriggerWindowFrame frame,JudgmentFrame? judgment,int frameIndex)
    {
        if(judgment is null||!frame.Activated||frame.ZhenyiBeforeResult==true||frameIndex<2||frameIndex+1>=_resolutionStack.Count||
            _resolutionStack[frameIndex]!=frame||_resolutionStack[frameIndex-1]!=judgment||
            _resolutionStack[frameIndex-2] is not ProgramSkillFrame{MaYuanYi:{Op:SkillProgramEffectOp.ResolveBinghuoEndingJudgment,Stage:MaYuanYiStage.Judgment}}root||
            !IsBinghuoJudgment(judgment,root)||!DyingSuitsStructuralEdge(judgment,frame)||
            frame.CandidateIndex<0||frame.CandidateIndex>=frame.Candidates.Count||
            _resolutionStack[frameIndex+1] is not ProgramSkillFrame child||
            child.WindowContext is not{Window:SkillProgramTriggerWindow.JudgmentFinalized,Judgment:{}final}context||context.ParentFrameId!=frame.Id||final!=frame.Judgment)
            return false;
        var candidate=frame.Candidates[frame.CandidateIndex];
        if(candidate.OwnerSeat!=child.OwnerSeat||candidate.SkillId!=child.SkillId||candidate.TriggerId!=child.TriggerId||candidate.SkillInstanceId!=child.SkillInstanceId||candidate.GameplayHash!=child.GameplayHash)return false;
        return FindProgramObserverRoot(static(e,f,c)=>e.MaYuanYiFirstChild(f,c),static(e,i)=>e.SameNameHandObserverSuffix(i),root.Id)is not null;
    }
}
