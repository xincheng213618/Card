namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool HasCaoYuCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveGongjieRoundFirstGifts)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveXiangxuMinimumTransition)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveXiangzuDyingGiftAndRecovery);
 private static CardConversionSource CaoYuSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private bool CaoYuActualTurnOpen(int turn,int owner)=>turn>0&&turn==_turnNumber&&turn==_turnProgression.TurnNumber&&owner==_turnProgression.OwnerSeat&&!ProgramEventHistory<ActualTurnCursorReturnedEvent>().Any(e=>e.ActualTurn==turn&&e.OwnerSeat==owner);
 private bool XiangzuSpent(int owner)=>ProgramEventHistory<CaoYuXiangzuSpentEvent>().Any(e=>e.Source.OwnerSeat==owner&&e.Source.SkillId=="ol:xiangzu");
 private bool CaoYuHistory(int owner,string skill,int recipient)=>ProgramEventHistory<CaoYuRecipientHistoryEvent>().Any(e=>e.OwnerSeat==owner&&e.SkillId==skill&&e.RecipientSeat==recipient);
 private bool IsXiangxuMinimumTrigger(SkillProgramTrigger t)=>t.Effects is[{Op:SkillProgramEffectOp.ResolveXiangxuMinimumTransition}];
 private bool IsXiangxuEndingTrigger(SkillProgramTrigger t)=>t.Effects is[{Op:SkillProgramEffectOp.ResolveXiangxuIssuedTurnEndAdjustment}];
 private sealed record CaoYuDeathMinimumCapture(long DeathFrameId,long ParentFrameId,int VictimSeat,int? KillerSeat,DeathReturnKind ReturnKind,int ActualTurn,int TurnOwner,IReadOnlyList<ProgramTriggerCandidate> Sources,IReadOnlyList<int> HandCounts,IReadOnlyList<bool> Alive)
 {
  public IReadOnlyList<ProgramTriggerCandidate> Sources{get;init;}=Array.AsReadOnly(Sources.ToArray());
  public IReadOnlyList<int> HandCounts{get;init;}=Array.AsReadOnly(HandCounts.ToArray());
  public IReadOnlyList<bool> Alive{get;init;}=Array.AsReadOnly(Alive.ToArray());
 }
 private CaoYuDeathMinimumCapture? CaptureCaoYuDeathMinimumSources(int victimSeat)
 {
  if(!HasCaoYuCapability||!_setupComplete||!IsValidPlayerSeat(victimSeat)||!_players[victimSeat].IsAlive||!CaoYuActualTurnOpen(_turnNumber,_turnProgression.OwnerSeat)||_resolutionStack.LastOrDefault()is not DeathFrame{ReturnKind:{}kind}death||death.VictimSeat!=victimSeat)return null;
  var alive=_players.Select(p=>p.IsAlive).ToArray();var counts=_players.Select(p=>GetHand(p).Count).ToArray();var remaining=_players.Where(p=>p.IsAlive&&p.Seat!=victimSeat).ToArray();if(remaining.Length==0)return null;
  var before=_players.Where(p=>p.IsAlive).Min(p=>counts[p.Seat]);var after=remaining.Min(p=>counts[p.Seat]);if(before==after)return null;
  var sources=remaining.Where(p=>counts[p.Seat]==after&&counts[p.Seat]!=before).SelectMany(p=>CollectProgramTriggerCandidates(p,SkillProgramTriggerWindow.CardsMoved)).Where(c=>IsXiangxuMinimumTrigger(GetProgramTrigger(c))).ToArray();
  return sources.Length==0?null:new(death.Id,death.ParentFrameId,victimSeat,death.KillerSeat,kind,_turnNumber,_turnProgression.OwnerSeat,sources,counts,alive);
 }
 private void RecordCaoYuDeathMinimumTransitions(long deathFrameId,int victimSeat,CaoYuDeathMinimumCapture? capture)
 {
  if(capture is not{}r)return;
  if(_resolutionStack.LastOrDefault()is not DeathFrame death||death.Id!=deathFrameId||death.Id!=r.DeathFrameId||death.ParentFrameId!=r.ParentFrameId||death.VictimSeat!=victimSeat||victimSeat!=r.VictimSeat||death.KillerSeat!=r.KillerSeat||death.ReturnKind!=r.ReturnKind||!CaoYuActualTurnOpen(r.ActualTurn,r.TurnOwner)||r.HandCounts.Count!=_players.Count||r.Alive.Count!=_players.Count||!r.Alive[victimSeat]||_players.Where((p,i)=>GetHand(p).Count!=r.HandCounts[i]||p.IsAlive!=(i!=victimSeat&&r.Alive[i])).Any())throw new InvalidOperationException("Xiangxu death minimum requires the exact immediate native alive-to-dead transition with unchanged Hand counts.");
  var before=_players.Where(p=>r.Alive[p.Seat]).Min(p=>r.HandCounts[p.Seat]);var after=_players.Where(p=>p.IsAlive).Min(p=>r.HandCounts[p.Seat]);
  foreach(var c in r.Sources)
  {
   if(!_players[c.OwnerSeat].IsAlive||r.HandCounts[c.OwnerSeat]!=after||r.HandCounts[c.OwnerSeat]==before||!IsXiangxuMinimumTrigger(GetProgramTrigger(c)))throw new InvalidOperationException("Xiangxu death minimum changed its captured original source or causal population edge.");
   var id=++_resolutionSequence;var source=new CardConversionSource(c.SkillId,c.BindingId,c.OwnerSeat,c.SkillInstanceId);var index=ProgramEventHistory<CaoYuMinimumTransitionEvent>().Count();
   AdvanceEventRulesAndQueueFact(new CaoYuMinimumTransitionEvent(id,index,source,c.GameplayHash,null,r.ActualTurn,r.TurnOwner,r.HandCounts[c.OwnerSeat],r.HandCounts[c.OwnerSeat],before,after,death.Id));
   AdvanceEventRulesAndQueueFact(new CaoYuMinimumDeathRecordedEvent(id,death.Id,death.ParentFrameId,victimSeat,death.KillerSeat,r.ReturnKind));
   foreach(var p in _players)AdvanceEventRulesAndQueueFact(new CaoYuMinimumSeatEvent(id,p.Seat,r.HandCounts[p.Seat],r.HandCounts[p.Seat],p.IsAlive,r.Alive[p.Seat]));
   AdvanceEventRulesAndQueueFact(new CaoYuMinimumArmedEvent(death.Id,id,source,c.GameplayHash));
  }
 }
 private void CaptureCaoYuMinimumTransitions(CardMovementBatchContext batch)
 {
  if(!HasCaoYuCapability||!_setupComplete||!CaoYuActualTurnOpen(batch.TurnNumber,_turnProgression.OwnerSeat)||!batch.Movements.Any(m=>m.From.Zone==CardZoneKind.Hand||m.To.Zone==CardZoneKind.Hand))return;
  var bindings=_players.Where(p=>p.IsAlive).SelectMany(p=>CollectProgramTriggerCandidates(p,SkillProgramTriggerWindow.CardsMoved)).Where(c=>IsXiangxuMinimumTrigger(GetProgramTrigger(c))).ToArray();if(bindings.Length==0)return;
  var before=_players.Select(p=>GetHand(p).Count).ToArray();var after=before.ToArray();
  foreach(var m in batch.Movements){if(m.From==m.To)continue;if(m.From is{Zone:CardZoneKind.Hand,OwnerSeat:{}from})before[from]++;if(m.To is{Zone:CardZoneKind.Hand,OwnerSeat:{}to})before[to]--;}
  if(before.Any(n=>n<0))throw new InvalidOperationException("CaoYu cannot reconstruct a negative pre-batch Hand count.");var alive=_players.Where(p=>p.IsAlive).Select(p=>p.Seat).ToArray();if(alive.Length==0)return;var minimumBefore=alive.Min(s=>before[s]);var minimumAfter=alive.Min(s=>after[s]);
  foreach(var c in bindings.Where(c=>after[c.OwnerSeat]==minimumAfter&&(before[c.OwnerSeat]!=after[c.OwnerSeat]||before[c.OwnerSeat]!=minimumBefore)))
  {
   var owner=_players[c.OwnerSeat];
   var id=++_resolutionSequence;var index=ProgramEventHistory<CaoYuMinimumTransitionEvent>().Count();var source=new CardConversionSource(c.SkillId,c.BindingId,c.OwnerSeat,c.SkillInstanceId);
   AdvanceEventRulesAndQueueFact(new CaoYuMinimumTransitionEvent(id,index,source,c.GameplayHash,batch.Id,batch.TurnNumber,_turnProgression.OwnerSeat,before[owner.Seat],after[owner.Seat],minimumBefore,minimumAfter));
   foreach(var p in _players)AdvanceEventRulesAndQueueFact(new CaoYuMinimumSeatEvent(id,p.Seat,before[p.Seat],after[p.Seat],p.IsAlive));
  }
 }
 private CaoYuMinimumTransitionEvent? CaoYuTransition(ProgramTriggerCandidate c,long? id=null)=>ProgramEventHistory<CaoYuMinimumTransitionEvent>().SingleOrDefault(e=>(id is null||e.OpportunityId==id)&&e.Index==c.OccurrenceIndex&&e.Source.OwnerSeat==c.OwnerSeat&&e.Source.SkillId==c.SkillId&&e.Source.SkillInstanceId==c.SkillInstanceId&&e.GameplayHash==c.GameplayHash);
 private bool ValidCaoYuTransition(CaoYuMinimumTransitionEvent e,CardMovementBatchContext? batch=null)
 {
  if(e.Source.SkillId!="ol:xiangxu"||e.Source.BindingId!="minimum-edge"||_contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash!=e.GameplayHash||!IsValidPlayerSeat(e.Source.OwnerSeat)||e.HandAfter!=e.MinimumAfter||e.HandBefore==e.HandAfter&&e.HandBefore==e.MinimumBefore)return false;
  var seats=ProgramEventHistory<CaoYuMinimumSeatEvent>().Where(x=>x.OpportunityId==e.OpportunityId).OrderBy(x=>x.Seat).ToArray();if(seats.Length!=_players.Count||!seats.Select(x=>x.Seat).SequenceEqual(Enumerable.Range(0,_players.Count))||seats.Any(x=>x.Before<0||x.After<0)||!seats.Any(x=>x.Alive)||seats.Where(x=>x.Alive).Min(x=>x.After)!=e.MinimumAfter||seats[e.Source.OwnerSeat]is not{Alive:true}own||own.Before!=e.HandBefore||own.After!=e.HandAfter)return false;
  if(e.DeathFrameId is{}deathId)
  {
   var deaths=ProgramEventHistory<CaoYuMinimumDeathRecordedEvent>().Where(x=>x.OpportunityId==e.OpportunityId).ToArray();if(batch is not null||e.BatchId is not null||deaths is not[var d]||d.DeathFrameId!=deathId||deathId<1||d.ParentFrameId<1||!IsValidPlayerSeat(d.VictimSeat)||d.VictimSeat==e.Source.OwnerSeat||!Enum.IsDefined(d.ReturnKind)||seats.Any(s=>s.AliveBefore is null||s.Before!=s.After||s.Alive!=(s.Seat!=d.VictimSeat&&s.AliveBefore.Value))||seats[d.VictimSeat].AliveBefore!=true||seats.Where(s=>s.AliveBefore==true).Min(s=>s.Before)!=e.MinimumBefore||ProgramEventHistory<CaoYuMinimumArmedEvent>().Count(a=>a.FrameId==deathId&&a.OpportunityId==e.OpportunityId&&a.Source==e.Source&&a.GameplayHash==e.GameplayHash)!=1)return false;return true;
  }
  if(e.BatchId is null||seats.Any(s=>s.AliveBefore is not null)||seats.Where(x=>x.Alive).Min(x=>x.Before)!=e.MinimumBefore||ProgramEventHistory<CaoYuMinimumDeathRecordedEvent>().Any(x=>x.OpportunityId==e.OpportunityId))return false;
  if(batch is null)return true;if(batch.Id!=e.BatchId||batch.TurnNumber!=e.ActualTurn||batch.Movements.Count==0)return false;
  foreach(var seat in seats){var delta=batch.Movements.Count(m=>m.To==CardLocation.Hand(seat.Seat)&&m.From!=m.To)-batch.Movements.Count(m=>m.From==CardLocation.Hand(seat.Seat)&&m.From!=m.To);if(seat.After-seat.Before!=delta)return false;}return batch.Movements.All(_cardMovements.Contains);
 }
 private void CollectXiangxuMinimumCandidates(CardMovementBatchContext batch,List<ProgramTriggerCandidate> result)
 {
  if(!HasCaoYuCapability)return;foreach(var e in ProgramEventHistory<CaoYuMinimumTransitionEvent>().Where(e=>e.BatchId==batch.Id&&_players[e.Source.OwnerSeat].IsAlive&&ValidCaoYuTransition(e,batch)))result.Add(new(e.Source.OwnerSeat,e.Source.SkillId,e.Source.BindingId,e.Source.SkillInstanceId,e.GameplayHash,0,e.Index));
 }
 private ProgramSkillWindowContext CreateXiangxuMinimumContext(CardsMovedTriggerWindowFrame f,ProgramTriggerCandidate c)
 {
  var e=CaoYuTransition(c)??throw new InvalidOperationException("Xiangxu minimum lost its issued original transition.");return new(SkillProgramTriggerWindow.CardsMoved,f.Id,c.OwnerSeat,SourceSeat:e.TurnOwner,TargetSeat:c.OwnerSeat,OccurrenceIndex:c.OccurrenceIndex,MovementBatch:f.Batch){CaoYuOpportunityId=e.OpportunityId};
 }
 private bool IsXiangxuMinimumCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
 {
  if(c.SkillId!="ol:xiangxu"||c.BindingId!="minimum-edge"||context.Window!=SkillProgramTriggerWindow.CardsMoved||context.OwnerSeat!=c.OwnerSeat||context.CaoYuOpportunityId is not{}id||!_players[c.OwnerSeat].IsAlive||CaoYuTransition(c,id)is not{}e||!CaoYuActualTurnOpen(e.ActualTurn,e.TurnOwner)||_resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(f=>f.Id==context.ParentFrameId)is not{}w||w.CandidateIndex<0||w.CandidateIndex>=w.Candidates.Count||w.Candidates[w.CandidateIndex]!=c||!ValidCaoYuTransition(e,w.Batch))return false;
  return CreateXiangxuMinimumContext(w,c)==context;
 }
 private bool CaoYuOpportunityConsumed(long id)=>ProgramEventHistory<CaoYuOpportunityConsumedEvent>().Any(e=>e.OpportunityId==id);
 private IEnumerable<CaoYuMinimumTransitionEvent> CaoYuOutstanding(int turn)=>ProgramEventHistory<CaoYuMinimumTransitionEvent>().Where(e=>e.ActualTurn==turn&&ValidCaoYuTransition(e)&&!CaoYuOpportunityConsumed(e.OpportunityId)&&ProgramEventHistory<CaoYuMinimumArmedEvent>().Count(a=>a.OpportunityId==e.OpportunityId&&a.Source==e.Source&&a.GameplayHash==e.GameplayHash)==1);
 private IReadOnlyList<TurnEndingBoundaryItem> IncludeXiangxuAfterTurnEndedItems(DeferredTurnEndFrame parent,IReadOnlyList<TurnEndingBoundaryItem> ordinary)
 {
  if(!HasCaoYuCapability)return ordinary;var items=ordinary.Where(i=>i.Candidate is not{SkillId:"ol:xiangxu",BindingId:"end-own"or"end-foreign"}||parent.AfterTurnEnded is not null).ToList();
  foreach(var e in CaoYuOutstanding(parent.TurnNumber).Where(e=>e.TurnOwner==parent.OwnerSeat&&_players[e.Source.OwnerSeat].IsAlive&&CaoYuActualTurnOpen(e.ActualTurn,e.TurnOwner)).OrderBy(e=>e.Index))
  {var binding=e.Source.OwnerSeat==parent.OwnerSeat?"end-own":"end-foreign";var c=new ProgramTriggerCandidate(e.Source.OwnerSeat,e.Source.SkillId,binding,e.Source.SkillInstanceId,e.GameplayHash,0,e.Index);var stable=$"xiangxu:{e.OpportunityId}";if(!items.Any(i=>i.StableIdentity==stable))items.Add(new(TurnEndingBoundaryItemKind.Program,0,stable,c,CaptureProgramTriggerFacts(_players[c.OwnerSeat])));}
  return items.SequenceEqual(ordinary)?ordinary:Array.AsReadOnly(items.ToArray());
 }
 private ProgramSkillWindowContext CreateXiangxuAfterTurnEndedContext(DeferredTurnEndFrame p,ProgramTriggerCandidate c,ProgramSkillWindowContext context)=>c.SkillId=="ol:xiangxu"&&c.BindingId is"end-own"or"end-foreign"&&CaoYuTransition(c)is{}e?context with{CaoYuOpportunityId=e.OpportunityId}:context;
 private bool IsIssuedXiangxuEndingCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
 {
  if(c.SkillId!="ol:xiangxu"||c.BindingId is not("end-own"or"end-foreign")||context.Window!=SkillProgramTriggerWindow.AfterTurnEnded||context.OwnerSeat!=c.OwnerSeat||context.CaoYuOpportunityId is not{}id||CaoYuTransition(c,id)is not{}e||!ValidCaoYuTransition(e)||!CaoYuActualTurnOpen(e.ActualTurn,e.TurnOwner)||_resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p=>p.Id==context.ParentFrameId)is not{}p||!IsActualAfterTurnEndedParent(p)||AfterTurnEndedCandidate(p)!=c||p.TurnNumber!=e.ActualTurn||p.OwnerSeat!=e.TurnOwner||context.SourceSeat!=p.OwnerSeat||context.TargetSeat!=p.OwnerSeat||context.OccurrenceIndex!=e.Index||context.Facts!=p.AfterTurnEnded!.Items[p.AfterTurnEnded.ItemIndex].Facts||c.BindingId!=(c.OwnerSeat==p.OwnerSeat?"end-own":"end-foreign"))return false;
  return _players[c.OwnerSeat].IsAlive&&!CaoYuOpportunityConsumed(id)||_resolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.CaoYu is{OpportunityId:var key,Op:SkillProgramEffectOp.ResolveXiangxuIssuedTurnEndAdjustment}r&&key==id&&r.Source==CaoYuSource(f)&&f.WindowContext==context&&(r.Paid||_players[c.OwnerSeat].IsAlive));
 }
 private bool CaoYuRoundFirst(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
 {
  if(c.SkillId!="ol:gongjie"||c.BindingId!="round-first"||context.Window!=SkillProgramTriggerWindow.RoundStarting||!_players[c.OwnerSeat].IsAlive||_roundNumber<1||_turnProgression.Kind==ActualTurnKind.Extra||_roundTurnSeats.Count!=1||!_roundTurnSeats.Contains(_currentSeat)||ProgramEventHistory<RoundStartedEvent>().LastOrDefault()is not{}round||round.RoundNumber!=_roundNumber||round.ActorSeat!=_currentSeat||_resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{Window:SkillProgramTriggerWindow.RoundStarting}w||w.OwnerSeat!=_currentSeat||w.CandidateIndex<0||w.CandidateIndex>=w.Candidates.Count||w.Candidates[w.CandidateIndex]!=c||ProgramEventHistory<CaoYuStartedEvent>().Any(e=>e.Source.OwnerSeat==c.OwnerSeat&&e.Source.SkillId==c.SkillId&&e.Round==_roundNumber))return false;
  return CaoYuGiftCards(c.OwnerSeat,c.OwnerSeat).Count>0&&_players.Any(p=>p.IsAlive&&p.Seat!=c.OwnerSeat);
 }
 private bool CaoYuDyingEntry(ProgramSkillFrame f,out DyingFrame dying)
 {
  dying=null!;var i=_resolutionStack.FindIndex(x=>x.Id==f.Id);if(i<2||_resolutionStack[i-1]is not ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.DyingEntering,Continuation:ProgramLifecycleContinuation.ResumeDyingEntry}w||_resolutionStack[i-2]is not DyingFrame d||w.ResumeDyingFrameId!=d.Id||d.VictimSeat!=f.OwnerSeat||w.OwnerSeat!=f.OwnerSeat||f.WindowContext is not{Window:SkillProgramTriggerWindow.DyingEntering}context||context.ParentFrameId!=w.Id||context.TargetSeat!=f.OwnerSeat||w.CandidateIndex<0||w.CandidateIndex>=w.Candidates.Count||!MountObserverCandidateMatches(f,w.Candidates[w.CandidateIndex]))return false;dying=d;return true;
 }
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27440)]
 private bool QualifyCaoYu(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)
 {
  if(t.Effects is not[{ }e]||!CaoYuComposition.Handles(e.Op))return true;
  return e.Op switch{SkillProgramEffectOp.ResolveGongjieRoundFirstGifts=>CaoYuRoundFirst(c,context),SkillProgramEffectOp.ResolveXiangxuMinimumTransition=>IsXiangxuMinimumCandidate(c,context),SkillProgramEffectOp.ResolveXiangxuIssuedTurnEndAdjustment=>IsIssuedXiangxuEndingCandidate(c,context),_=>context.Window==SkillProgramTriggerWindow.DyingEntering&&context.TargetSeat==c.OwnerSeat&&_players[c.OwnerSeat].IsAlive&&!XiangzuSpent(c.OwnerSeat)&&CaoYuGiftCards(c.OwnerSeat,c.OwnerSeat).Count>0&&_players.Any(p=>p.IsAlive&&p.Seat!=c.OwnerSeat)};
 }
}
