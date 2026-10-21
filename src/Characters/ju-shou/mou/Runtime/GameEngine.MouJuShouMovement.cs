namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool MouJsMovementParent(ProgramSkillFrame f)
 {
  if(f.SkillId!=MouJuShouRuntime.Muwang||f.TriggerId!="first-loss"||f.ActivationId!=f.TriggerId||f.WindowContext is not{Window:SkillProgramTriggerWindow.CardsMoved}w||w.OwnerSeat!=f.OwnerSeat)return false;
  var at=_resolutionStack.FindIndex(x=>x.Id==f.Id);return at>0&&_resolutionStack[at-1]is CardsMovedTriggerWindowFrame p&&p.Id==w.ParentFrameId&&p.Batch.Id==w.MovementBatch?.Id&&p.CandidateIndex>=0&&p.CandidateIndex<p.Candidates.Count&&MountObserverCandidateMatches(f,p.Candidates[p.CandidateIndex])&&IsIssuedMouJsMovementCandidate(p.Candidates[p.CandidateIndex],w);
 }
 private void CaptureMouJsMovementBatch(CardMovementBatchContext batch)
 {
  if(!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouJuShouMuwang))return;
  if(batch.Movements.Any(m=>!_cardMovements.Contains(m)||m.TurnNumber!=batch.TurnNumber))throw new InvalidOperationException("Muwang requires a genuinely committed original native movement batch.");
  MouJuShou.ObserveBatch(new(batch.Id,batch.TurnNumber,batch.Movements));
 }
 private MouJsMuwangQueuedEvent? MouJsQueue(ProgramTriggerCandidate c,CardMovementBatchContext batch)=>ProgramEventHistory<MouJsMuwangQueuedEvent>().SingleOrDefault(q=>q.Ordinal==c.OccurrenceIndex&&q.Source.SkillId==c.SkillId&&q.Source.BindingId==c.BindingId&&q.Source.OwnerSeat==c.OwnerSeat&&q.Source.SkillInstanceId==c.SkillInstanceId&&q.GameplayHash==c.GameplayHash&&q.Turn==batch.TurnNumber&&q.BatchId==batch.Id);
 private IEnumerable<ProgramTriggerCandidate> CollectMouJsMovementCandidates(CardMovementBatchContext batch)
 {
  if(_contentRegistry.Skills.GetValueOrDefault(MouJuShouRuntime.Muwang)?.Program is not{}program)yield break;var trigger=program.Triggers.Single(t=>t.Id=="first-loss");
  foreach(var q in ProgramEventHistory<MouJsMuwangQueuedEvent>().Where(q=>q.BatchId==batch.Id&&q.Turn==batch.TurnNumber&&_players[q.Source.OwnerSeat].IsAlive&&q.GameplayHash==program.GameplayHash&&!ProgramEventHistory<MouJsMuwangSettledEvent>().Any(e=>e.Ordinal==q.Ordinal)))yield return new(q.Source.OwnerSeat,q.Source.SkillId,q.Source.BindingId,q.Source.SkillInstanceId,q.GameplayHash,trigger.Priority,q.Ordinal);
 }
 private ProgramSkillWindowContext CreateMouJsMovementContext(CardsMovedTriggerWindowFrame p,ProgramTriggerCandidate c)=>new(SkillProgramTriggerWindow.CardsMoved,p.Id,c.OwnerSeat,SourceSeat:c.OwnerSeat,TargetSeat:c.OwnerSeat,OccurrenceIndex:c.OccurrenceIndex,Facts:CaptureProgramTriggerFacts(_players[c.OwnerSeat]),MovementBatch:p.Batch);
 private bool IsIssuedMouJsMovementCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext w)
 {
  if(c.SkillId!=MouJuShouRuntime.Muwang||c.BindingId!="first-loss"||w.Window!=SkillProgramTriggerWindow.CardsMoved||w.OwnerSeat!=c.OwnerSeat||w.SourceSeat!=c.OwnerSeat||w.TargetSeat!=c.OwnerSeat||w.OccurrenceIndex!=c.OccurrenceIndex||_resolutionStack.SingleOrDefault(x=>x.Id==w.ParentFrameId)is not CardsMovedTriggerWindowFrame p||p.CandidateIndex<0||p.CandidateIndex>=p.Candidates.Count||p.Candidates[p.CandidateIndex]!=c||w.MovementBatch is not{}batch||batch.Id!=p.Batch.Id||!batch.Movements.SequenceEqual(p.Batch.Movements)||MouJsQueue(c,batch)is not{}q||q.Turn!=_turnNumber||q.GameplayHash!=_contentRegistry.GetSkill(c.SkillId).Program?.GameplayHash||batch.Movements.Any(m=>!_cardMovements.Contains(m)))return false;
  if(q.Kind==MouJsMuwangKind.ChooseFirst){var choices=ProgramEventHistory<MouJsFirstCandidateEvent>().Where(e=>e.Ordinal==q.Ordinal).ToArray();return choices.Length>0&&choices.Select(e=>e.CardId).Distinct().Count()==choices.Length&&choices.Min(e=>e.Sequence)==q.FirstSequence&&choices.All(e=>batch.Movements.Any(m=>m.CardId==e.CardId&&m.CardKind==e.Kind&&m.From==e.From&&m.From.OwnerSeat==c.OwnerSeat&&m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment&&m.From!=m.To&&m.Sequence==e.Sequence));}
  if(!ProgramEventHistory<MouJsFirstChosenEvent>().Any(e=>e.Source==q.Source&&e.Turn==q.Turn&&e.CardId==q.CardId&&e.FirstSequence==q.FirstSequence))return false;
  return q.Kind==MouJsMuwangKind.Recover?batch.Movements.Any(m=>m.CardId==q.CardId&&m.To==CardLocation.DiscardPile&&m.Sequence>=q.FirstSequence):batch.Movements.Any(m=>m.CardId==q.CardId&&m.From.OwnerSeat==c.OwnerSeat&&m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment&&m.From!=m.To&&ProgramEventHistory<MouJsRecoveredEvent>().Any(e=>e.Source==q.Source&&e.Turn==q.Turn&&e.CardId==q.CardId&&e.ReturnSequence<m.Sequence));
 }
 private MouJsMuwangQueuedEvent? MouJsMuwangOpportunity(ProgramSkillFrame f)
 {if(!MouJsMovementParent(f)||f.WindowContext is not{MovementBatch:{}batch}w)return null;var p=_resolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(x=>x.Id==w.ParentFrameId);return MouJsQueue(p.Candidates[p.CandidateIndex],batch);}
 private bool MouJsReclaimable(MouJsFirstLoss loss,CardMovementRecord discarded)
 {
  if(discarded.CardId!=loss.CardId||discarded.TurnNumber!=loss.Turn||discarded.To!=CardLocation.DiscardPile||!_cardMovements.Contains(discarded)||_cardMovements.SingleOrDefault(m=>m.CardId==loss.CardId&&m.Sequence==loss.FirstSequence)is not{}first||first.From.OwnerSeat!=loss.Source.OwnerSeat||first.From.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment))return false;
  // The selected first-loss invoice follows this exact physical entity through
  // later real transfers in the same actual turn. Processing never creates a
  // second owned loss; only its original HEJ departure names the first loss.
  return discarded.Sequence>=first.Sequence;
 }
}
