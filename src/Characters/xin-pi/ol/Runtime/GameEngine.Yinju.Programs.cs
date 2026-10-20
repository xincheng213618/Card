namespace CardGame.Core;
public sealed partial class GameEngine
{
 private void PayYinjuDraw(ProgramSkillFrame f,int seat)
 {
  var r=f.XinPi! with{Before=_movementSequence,After=_movementSequence,Requested=1,Actual=0};ReplaceRuntimeTop(f with{XinPi=r,PendingMovementContinuation=new(seat,0,null)});var actual=DrawCards(_players[seat],1,true,new(YinjuDrawReason)).Count;r=r with{After=_movementSequence,Actual=actual};ReplaceRuntimeTop(GetActiveProgramFrame(f.Id)with{XinPi=r});AdvanceEventRulesAndQueueFact(new YinjuDrawPaidEvent(f.Id,r.Relation!,r.UseId!.Value,r.ActionId!.Value,seat,r.Before,r.After,actual));AdvanceRuntimeProgram(f.Id);
 }
}
