namespace CardGame.Core;
public sealed partial class GameEngine
{
 private void PaySanwenDraw(ProgramSkillFrame f)
 {
  var r=f.WangCan!;var count=r.ActualCount*2;var before=_movementSequence;ReplaceRuntimeTop(f=f with{WangCan=r with{Stage=WangCanStage.SanwenDraw,Before=before,After=before,ActualCount=0,Cursor=count},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  var cards=_players[f.OwnerSeat].IsAlive?DrawCards(_players[f.OwnerSeat],count,true,new(WangCanReason(WangCanStage.SanwenDraw))):[];f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{WangCan=f.WangCan!with{After=_movementSequence,ActualCount=cards.Count}});IssueWangCanSegment(f);AdvanceRuntimeProgram(f.Id);
 }
}
