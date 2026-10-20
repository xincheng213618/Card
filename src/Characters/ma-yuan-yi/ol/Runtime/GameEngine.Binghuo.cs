using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool CanBeginBinghuoJudgment(ProgramSkillFrame f,int targetSeat,string reason,string bind,SkillProgramCardSetVisibility visibility)=>f.MaYuanYi is{Stage:MaYuanYiStage.Judgment,TargetSeat:{}target,JudgmentId:not null}&&target==targetSeat&&reason=="binghuo"&&bind==BinghuoBind&&visibility==SkillProgramCardSetVisibility.Public;
    private bool IsBinghuoJudgment(JudgmentFrame j,ProgramSkillFrame f)=>f.MaYuanYi is{Stage:MaYuanYiStage.Judgment,TargetSeat:{}target,JudgmentId:{}id}&&j.Id==id&&j.ParentFrameId==f.Id&&j.TargetSeat==target&&j.SourceSeat==f.OwnerSeat&&j.Continuation==JudgmentContinuationKind.ProgramSkill&&j.Reason=="binghuo"&&j.ProgramResultBind==BinghuoBind&&j.ProgramResultVisibility==SkillProgramCardSetVisibility.Public;
    private bool TryReturnBinghuoJudgment(JudgmentFrame j)
    {
        if(_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==j.ParentFrameId)is not{MaYuanYi:{}r}f)return false;
        if(!IsBinghuoJudgment(j,f))throw new InvalidOperationException("Binghuo received a foreign judgment return.");
        var result=CompleteProgramEventHistory().OfType<JudgmentResolvedEvent>().Single(e=>e.ResolutionId==j.Id&&e.ParentResolutionId==f.Id&&e.TargetSeat==r.TargetSeat&&e.Reason=="binghuo");
        var before=_movementSequence;var count=0;CardMovementRecord? handoff=null;
        if(result.CardId is{}card&&_cardZones.GetLocation(card)==CardLocation.Processing)
        {
            handoff=_cardMovements.LastOrDefault(m=>m.Sequence<=before&&m.CardId==card);
            if(handoff is null||handoff.From!=CardLocation.Judgment(j.TargetSeat)||handoff.To!=CardLocation.Processing||handoff.Reason.Value!="skill-program.judgment.result")
                throw new InvalidOperationException("Binghuo cleanup requires the exact finalized native judgment result movement.");
        }
        ReplaceRuntimeTop(f=f with{MaYuanYi=r with{Stage=MaYuanYiStage.JudgmentChildren,JudgmentCardId=result.CardId,JudgmentSuit=result.Suit,JudgmentHandoffSequence=handoff?.Sequence,Before=before,After=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        AdvanceEventRulesAndQueueFact(new BinghuoJudgmentReturnedEvent(f.Id,j.Id,result.CardId,result.Suit,handoff?.Sequence));
        if(handoff is not null){MoveCard(GetAdvancedCard(handoff.CardId),CardLocation.Processing,CardLocation.DiscardPile,CardMoveReasons.JudgmentFinish);count=1;}
        FinishMaYuanYiMovement(f.Id,before,count);ContinueMaYuanYi(f.Id);return true;
    }
}
