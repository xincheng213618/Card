namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool JieyanDisabled(int owner)=>ProgramEventHistory<YuanJiJieyanDisabledEvent>().Any(e=>e.OwnerSeat==owner&&e.ActualTurn==_turnNumber);
 private YuanJiLossOpportunityEvent? JieyanOpportunity(ProgramTriggerCandidate c)=>ProgramEventHistory<YuanJiLossOpportunityEvent>().SingleOrDefault(e=>e.Index==c.OccurrenceIndex&&e.Source==new CardConversionSource(c.SkillId,c.BindingId,c.OwnerSeat,c.SkillInstanceId)&&e.GameplayHash==c.GameplayHash);
 private bool ValidJieyanOpportunity(YuanJiLossOpportunityEvent e,CardMovementBatchContext batch)
 {
  if(e.BatchId!=batch.Id||e.ActualTurn!=batch.TurnNumber||e.Source.SkillId!="ol:jieyan"||e.Source.BindingId!="hand-pair"||e.Round!=_roundNumber||e.FirstCardId==e.SecondCardId||e.FirstSequence>=e.SecondSequence||_contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash!=e.GameplayHash)return false;
  var cards=batch.Movements.Where(m=>m.From==CardLocation.Hand(e.LoserSeat)&&m.From!=m.To).DistinctBy(m=>m.CardId).OrderBy(m=>m.Sequence).ToArray();return cards is[var first,var second]&&first.CardId==e.FirstCardId&&second.CardId==e.SecondCardId&&first.Sequence==e.FirstSequence&&second.Sequence==e.SecondSequence&&cards.All(_cardMovements.Contains);
 }
 private void CollectJieyanCandidates(CardMovementBatchContext batch,List<ProgramTriggerCandidate> result)
 {
  if(!HasYuanJiCapability)return;foreach(var e in ProgramEventHistory<YuanJiLossOpportunityEvent>().Where(e=>e.BatchId==batch.Id&&_players[e.Source.OwnerSeat].IsAlive&&_players[e.LoserSeat].IsAlive&&!JieyanDisabled(e.Source.OwnerSeat)&&ValidJieyanOpportunity(e,batch)))result.Add(new(e.Source.OwnerSeat,e.Source.SkillId,e.Source.BindingId,e.Source.SkillInstanceId,e.GameplayHash,0,e.Index));
 }
 private ProgramSkillWindowContext CreateJieyanContext(CardsMovedTriggerWindowFrame frame,ProgramTriggerCandidate c)
 {var e=JieyanOpportunity(c)??throw new InvalidOperationException("Jieyan lost its captured atomic Hand pair.");return new(SkillProgramTriggerWindow.CardsMoved,frame.Id,c.OwnerSeat,SourceSeat:e.LoserSeat,TargetSeat:e.LoserSeat,OccurrenceIndex:c.OccurrenceIndex,MovementBatch:frame.Batch);}
 private bool IsIssuedJieyanCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
 {
  if(c.SkillId!="ol:jieyan"||c.BindingId!="hand-pair"||context.Window!=SkillProgramTriggerWindow.CardsMoved||!_players[c.OwnerSeat].IsAlive||JieyanDisabled(c.OwnerSeat)||JieyanOpportunity(c)is not{}e||!_players[e.LoserSeat].IsAlive||e.ActualTurn!=_turnNumber||_resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{}w||w.CandidateIndex<0||w.CandidateIndex>=w.Candidates.Count||w.Candidates[w.CandidateIndex]!=c||!ValidJieyanOpportunity(e,w.Batch))return false;return CreateJieyanContext(w,c)==context;
 }
}
