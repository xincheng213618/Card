namespace CardGame.Core;
public sealed partial class GameEngine
{
 private void BeginDenglouLease(ProgramSkillFrame f)
 {
  var before=_movementSequence;var lease=WangCanLease(f.OwnerSeat);ReplaceRuntimeTop(f=f with{WangCan=f.WangCan!with{Lease=lease,Before=before,After=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});var ids=new List<int>();
  for(var i=0;i<4&&EnsureDrawPile();i++){var card=_cardZones.CardsAt(CardLocation.DrawPile)[^1];ids.Add(card.Id);MoveCard(card,CardLocation.DrawPile,lease,new(WangCanReason(WangCanStage.DenglouViewChildren)));}
  f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{WangCan=f.WangCan!with{Entities=ids,Remaining=ids,After=_movementSequence,ActualCount=ids.Count,LeaseBefore=before,LeaseAfter=_movementSequence}});IssueWangCanSegment(f);AdvanceRuntimeProgram(f.Id);
 }
 private void ContinueDenglou(ProgramSkillFrame f)
 {
  var r=f.WangCan!;var remaining=r.Remaining.Where(id=>_cardZones.GetLocation(id)==r.Lease).ToArray();ReplaceRuntimeTop(f=f with{WangCan=r=r with{Remaining=remaining,Stage=WangCanStage.DenglouChoose,Native=null},PendingMovementContinuation=null});
  if(!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None){PayWangCanMove(f,remaining,CardLocation.DiscardPile,WangCanStage.DenglouCancel);return;}
  if(remaining.Length==0){FinishWangCan(f);return;}var choices=WangCanChoices(f);if(choices.Count==0){PayWangCanMove(f,remaining,CardLocation.DiscardPile,WangCanStage.DenglouDiscard);return;}PublishWangCan(f,f.OwnerSeat,choices,"登楼：选择仍暂存的一张基本牌及真实使用目标。");
 }
 private CardSnapshot[] DenglouPrivateCards(int viewer)=>_resolutionStack.OfType<ProgramSkillFrame>().Where(f=>f.OwnerSeat==viewer&&f.WangCan?.Lease is not null).SelectMany(f=>f.WangCan!.Remaining.Where(id=>_cardZones.GetLocation(id)==f.WangCan.Lease).Select(id=>ToSnapshot(GetAdvancedCard(id)))).ToArray();
}
