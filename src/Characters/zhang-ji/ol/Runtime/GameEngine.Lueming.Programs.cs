using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
 private int ZhangJiLueMingCount(int owner)=>CompleteProgramEventHistory().OfType<ZhangJiStartedEvent>().Count(e=>e.Source.OwnerSeat==owner && !e.Equipment);
 private bool CanBeginZhangJiJudgment(ProgramSkillFrame f,int targetSeat,string reason,string bind,SkillProgramCardSetVisibility visibility)=>f.ZhangJi is{Stage:"judgment",Equipment:false,JudgmentReturned:false,ChosenRank:>=1 and <=13,JudgmentId:not null} && ValidZhangJi(f) && targetSeat==f.OwnerSeat && reason=="lueming" && bind=="zhang-ji-rank" && visibility==SkillProgramCardSetVisibility.Public;
 private bool IsZhangJiJudgment(JudgmentFrame j,ProgramSkillFrame f)=>f.ZhangJi is{Equipment:false,Stage:"judgment",JudgmentReturned:false,JudgmentId:{}id}r && ValidZhangJi(f) &&
  j.Id==id && j.ParentFrameId==f.Id && j.TargetSeat==f.OwnerSeat && j.SourceSeat==f.OwnerSeat && j.Continuation==JudgmentContinuationKind.ProgramSkill && j.Reason=="lueming" && j.ProgramResultBind=="zhang-ji-rank" && j.ProgramResultVisibility==SkillProgramCardSetVisibility.Public;
 private bool TryReturnZhangJiJudgment(JudgmentFrame j)
 {
  if(_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==j.ParentFrameId) is not{ZhangJi:not null}f)return false;
  if(!IsZhangJiJudgment(j,f))throw new InvalidOperationException("Lueming received a foreign judgment return.");
  var result=CompleteProgramEventHistory().OfType<JudgmentResolvedEvent>().Single(e=>e.ResolutionId==j.Id);
  var finalCard=GetJudgmentCard(j);
  SetProgramCardSet(f.Id,"zhang-ji-rank",finalCard is null?[]:[finalCard.Id],SkillProgramCardSetVisibility.Public,finalCard is null?[]:[_cardZones.GetLocation(finalCard.Id)],finalCard is null?null:EffectiveZhenyiJudgmentSuit(j,EffectiveSuit(_players[j.TargetSeat],finalCard)));
  f=GetActiveProgramFrame(f.Id);var r=f.ZhangJi!;
  ReplaceRuntimeTop(f=f with{ZhangJi=r with{JudgmentReturned=true,JudgmentCard=result.CardId,FinalRank=result.Rank,Stage="outcome"}});
  AdvanceEventRulesAndQueueFact(new ZhangJiJudgmentReturnedEvent(f.Id,j.Id,result.CardId,result.Rank));ResumeZhangJi(f.Id);return true;
 }
 private void IssueZhangJiOutcome(ProgramSkillFrame f)
 {
  var r=f.ZhangJi!;if(!_players[r.Target].IsAlive || _winner!=Winner.None){FinishZhangJi(f);return;}
  if(r.FinalRank==r.ChosenRank)
  {
   ReplaceRuntimeTop(f=f with{ZhangJi=r with{Stage="damage"}});AdvanceEventRulesAndQueueFact(new ZhangJiDamageIssuedEvent(f.Id,f.OwnerSeat,r.Target,2));BeginProgramSkillDamage(f,r.Target,2);return;
  }
  if(!_players[f.OwnerSeat].IsAlive){FinishZhangJi(f);return;}
  var cards=new[]{CardLocation.Hand(r.Target),CardLocation.Equipment(r.Target),CardLocation.Judgment(r.Target)}.SelectMany(loc=>_cardZones.CardsAt(loc)).Where(c=>!IsForeignEquipmentDiscardPrevented(f.OwnerSeat,c,_cardZones.GetLocation(c.Id),OwnedCardMoveIntent.Obtain)).OrderBy(c=>c.Id).ToArray();
  if(cards.Length==0){FinishZhangJi(f);return;}
  var card=cards[_random.Next(cards.Length)];var from=_cardZones.GetLocation(card.Id);var before=_movementSequence;
  ReplaceRuntimeTop(f=f with{ZhangJi=r with{Stage="movement",Before=before,After=before,GainCard=card.Id,GainFrom=from},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  MoveProgramCardsFromMultipleSources([card.Id],CardLocation.Hand(f.OwnerSeat),new(ZhangJiGainReason),(_,records)=>{var current=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(current with{ZhangJi=current.ZhangJi! with{After=_movementSequence}});AdvanceEventRulesAndQueueFact(new ZhangJiCardGainedEvent(f.Id,r.Target,f.OwnerSeat,before,_movementSequence));});
  if(!TryBeginCardsMovedProgramWindow(f.Id))ReturnRuntimeProgramMovement(f.Id);
 }
}
