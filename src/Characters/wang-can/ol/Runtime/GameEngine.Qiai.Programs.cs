namespace CardGame.Core;
public sealed partial class GameEngine
{
 private IReadOnlyList<PromptChoice> QiaiChoices(ProgramSkillFrame f)
 {
  var r=f.WangCan!;if(r.Cursor>=r.Seats.Count||!_players[f.OwnerSeat].IsAlive)return [];var seat=r.Seats[r.Cursor];if(!_players[seat].IsAlive)return [];
  return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id,seat,seat,[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Transfer,canSelect:(_,c)=>!c.IsGeneralWeapon).Select(c=>c with{Parameters=new Dictionary<string,string>(c.Parameters){["program-action"]="wangcan",["branch"]="qiai"}}).ToArray());
 }
 private void ContinueQiai(ProgramSkillFrame f)
 {
  var r=f.WangCan!;if(!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None){FinishWangCan(f);return;}
  while(r.Cursor<r.Seats.Count)
  {
   ReplaceRuntimeTop(f=f with{WangCan=r with{Stage=WangCanStage.QiaiChoose},PendingMovementContinuation=null});var choices=QiaiChoices(f);if(choices.Count>0){PublishWangCan(f,r.Seats[r.Cursor],choices,"七哀：请选择交给其的一张牌。");return;}r=r with{Cursor=r.Cursor+1};
  }
  FinishWangCan(f);
 }
}
