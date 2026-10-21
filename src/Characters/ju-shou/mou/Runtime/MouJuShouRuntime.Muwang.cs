namespace CardGame.Core;
internal sealed partial class MouJuShouRuntime
{
 internal void ObserveBatch(MouJsObservedBatch batch)
 {
  if(batch.Turn<1||batch.Turn!=facts.Turn||batch.Moves.Count==0)return;
  foreach(var owner in facts.Seats.Where(facts.Alive))
  foreach(var source in facts.MuwangSources(owner))
  {
   var losses=facts.Losses(owner).Where(l=>l.Turn==batch.Turn).ToArray();var loss=losses.SingleOrDefault(l=>l.Source==source.Source);
   var departures=batch.Moves.Where(m=>m.From.OwnerSeat==owner&&m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment&&m.From!=m.To).OrderBy(m=>m.Sequence).ToArray();
   if(loss is null&&!facts.History<MouJsMuwangQueuedEvent>().Any(e=>e.Source==source.Source&&e.Turn==batch.Turn&&e.Kind==MouJsMuwangKind.ChooseFirst))
   {
    var first=departures.Where(m=>facts.Eligible(m.CardId)).DistinctBy(m=>m.CardId).ToArray();if(first.Length==0)continue;
    var q=Queue(source.Source,source.Hash,batch,MouJsMuwangKind.ChooseFirst,0,first[0].Sequence);foreach(var m in first)events.AdvanceFact(new MouJsFirstCandidateEvent(q.Ordinal,m.CardId,m.CardKind,m.From,m.Sequence));continue;
   }
   if(loss is null)continue;
   if(loss is{Returned:true,LostAgain:false}&&departures.Any(m=>m.CardId==loss.CardId&&m.Sequence>loss.FirstSequence))
   {native.SetLosses(owner,losses.Select(l=>l==loss?l with{LostAgain=true}:l).ToArray());Queue(source.Source,source.Hash,batch,MouJsMuwangKind.Penalty,loss.CardId,loss.FirstSequence);continue;}
   if(!loss.Returned&&batch.Moves.Any(m=>m.CardId==loss.CardId&&facts.Reclaimable(loss,m))&&!facts.History<MouJsMuwangQueuedEvent>().Any(e=>e.Source==loss.Source&&e.Turn==loss.Turn&&e.Kind==MouJsMuwangKind.Recover))Queue(source.Source,source.Hash,batch,MouJsMuwangKind.Recover,loss.CardId,loss.FirstSequence);
  }
 }
 private MouJsMuwangQueuedEvent Queue(CardConversionSource source,string hash,MouJsObservedBatch batch,MouJsMuwangKind kind,int card,long sequence)
 {var q=new MouJsMuwangQueuedEvent(facts.History<MouJsMuwangQueuedEvent>().Count(),source,hash,batch.Turn,batch.Id,kind,card,sequence);events.AdvanceFact(q);return q;}
 private SkillProgramStepOutcome StartMuwang(ProgramSkillFrame f)
 {
  var q=facts.MuwangOpportunity(f)??throw new InvalidOperationException("Muwang requires its exact issued original native movement batch.");
  var cards=facts.History<MouJsFirstCandidateEvent>().Where(e=>e.Ordinal==q.Ordinal).OrderBy(e=>e.Sequence).Select(e=>e.CardId).ToArray();
  var r=new MouJuShouReceipt(Source(f),f.GameplayHash,SkillProgramEffectOp.ResolveMouJuShouMuwang,facts.Turn,f.OwnerSeat){Stage=q.Kind==MouJsMuwangKind.ChooseFirst?MouJuShouStage.FirstLossChoice:q.Kind==MouJsMuwangKind.Recover?MouJuShouStage.RecoverChildren:MouJuShouStage.Penalty,Before=facts.Sequence,After=facts.Sequence,MuwangOrdinal=q.Ordinal,CardId=q.CardId,LossSequence=q.FirstSequence,Recover=q.Kind==MouJsMuwangKind.Recover,Penalty=q.Kind==MouJsMuwangKind.Penalty,Bound=cards};
  frames.ReplaceTop(f=f with{MouJuShou=r});events.AdvanceFact(new MouJsStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.Turn,r.Actor));
  if(q.Kind==MouJsMuwangKind.ChooseFirst&&cards is[var only])ChooseFirst(f,only);
  else if(q.Kind==MouJsMuwangKind.Recover){frames.ReplaceTop(f=f with{PendingMovementContinuation=new(f.OwnerSeat,0,null)});if(facts.InDiscard(r.CardId))native.Recover(f);else Complete(f);}
  Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;
 }
 private void ChooseFirst(ProgramSkillFrame f,int card)
 {
  var r=f.MouJuShou!;var candidate=facts.History<MouJsFirstCandidateEvent>().Single(e=>e.Ordinal==r.MuwangOrdinal&&e.CardId==card);if(facts.Losses(r.Actor).Any(l=>l.Source==r.Source&&l.Turn==r.Turn))throw new InvalidOperationException("Muwang cannot choose a second first-loss entity.");
  var loss=new MouJsFirstLoss(r.Source,r.GameplayHash,r.Turn,card,candidate.Sequence);native.SetLosses(r.Actor,facts.Losses(r.Actor).Where(l=>l.Turn==r.Turn).Append(loss).ToArray());events.AdvanceFact(new MouJsFirstChosenEvent(f.Id,r.MuwangOrdinal,r.Source,r.Turn,card,candidate.Sequence));
  var recover=facts.ReadyToReclaim(loss);frames.ReplaceTop(f=f with{MouJuShou=r with{CardId=card,LossSequence=candidate.Sequence,Stage=MouJuShouStage.RecoverChildren,Recover=recover,Before=facts.Sequence,After=facts.Sequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});if(recover)native.Recover(f);
 }
 internal void Recovered(long frameId,CardMovementRecord move)
 {
  var f=frames.ActiveProgram(frameId);var r=f.MouJuShou!;var loss=facts.Losses(r.Actor).Single(l=>l.Source==r.Source&&l.Turn==r.Turn&&l.CardId==r.CardId&&l.FirstSequence==r.LossSequence);
  if(!Valid(f)||r.Stage!=MouJuShouStage.RecoverChildren||!r.Recover||loss.Returned||move.CardId!=r.CardId||move.From!=CardLocation.DiscardPile||move.To!=CardLocation.Hand(r.Actor)||move.Sequence<=r.Before)throw new InvalidOperationException("Muwang cannot mark an unreturned entity as recovered.");
  native.SetLosses(r.Actor,facts.Losses(r.Actor).Select(l=>l==loss?l with{Returned=true}:l).ToArray());events.AdvanceFact(new MouJsRecoveredEvent(f.Id,r.MuwangOrdinal,r.Source,r.Turn,r.CardId,r.LossSequence,move.Sequence));
 }
}
