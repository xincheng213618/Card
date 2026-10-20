namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool HasYuanJiCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveJinghuaRoundDraw)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveShuiyueTurnDraw);
 private static CardConversionSource YuanJiSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private static bool IsYuanJiReturnOp(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveJinghuaTurnReturn or SkillProgramEffectOp.ResolveShuiyueRoundReturn;
 private static bool IsYuanJiMarkDraw(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveJinghuaRoundDraw or SkillProgramEffectOp.ResolveShuiyueTurnDraw;
 private void CaptureYuanJiAtomicLoss(CardMovementBatchContext batch)
 {
  if(!HasYuanJiCapability||!_setupComplete||batch.TurnNumber<1||_winner!=Winner.None)return;
  var losses=batch.Movements.Where(m=>m.From is{Zone:CardZoneKind.Hand,OwnerSeat:not null}&&m.From!=m.To).GroupBy(m=>m.From.OwnerSeat!.Value).Where(g=>_players[g.Key].IsAlive&&g.Select(m=>m.CardId).Distinct().Count()==2).ToArray();if(losses.Length==0)return;
  var candidates=_players.Where(p=>p.IsAlive&&!JieyanDisabled(p.Seat)).SelectMany(p=>CollectProgramTriggerCandidates(p,SkillProgramTriggerWindow.CardsMoved)).Where(c=>GetProgramTrigger(c).Effects is[{Op:SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair}]).ToArray();
  foreach(var c in candidates)foreach(var loss in losses)
  {
   var cards=loss.DistinctBy(m=>m.CardId).OrderBy(m=>m.Sequence).ToArray();var index=ProgramEventHistory<YuanJiLossOpportunityEvent>().Count();
   AdvanceEventRulesAndQueueFact(new YuanJiLossOpportunityEvent(index,batch.Id,new(c.SkillId,c.BindingId,c.OwnerSeat,c.SkillInstanceId),c.GameplayHash,loss.Key,batch.TurnNumber,_roundNumber,cards[0].CardId,cards[1].CardId,cards[0].Sequence,cards[1].Sequence));
  }
 }
 private IEnumerable<YuanJiMarkClaim> YuanJiActiveClaims(int owner,string skill)
 {
  var started=ProgramEventHistory<YuanJiStartedEvent>().Where(e=>e.Source.OwnerSeat==owner&&e.Source.SkillId==skill&&IsYuanJiMarkDraw(e.Op)).ToDictionary(e=>e.FrameId);
  var closed=ProgramEventHistory<YuanJiMarkClosedEvent>().Select(e=>(e.DrawFrameId,e.Index,e.CardId)).ToHashSet();
  foreach(var a in ProgramEventHistory<YuanJiAcquiredEvent>().Where(a=>started.ContainsKey(a.FrameId)&&!closed.Contains((a.FrameId,a.Index,a.CardId))).OrderBy(a=>a.MovementSequence))
  {var s=started[a.FrameId];yield return new(a.FrameId,a.Index,a.CardId,s.Source,s.GameplayHash,a.MovementSequence);}
 }
 private string? YuanJiCardMarkLabel(int ownerSeat,int cardId)
 {
  if(!HasYuanJiCapability||!IsValidPlayerSeat(ownerSeat)||!GetHand(_players[ownerSeat]).Any(c=>c.Id==cardId))return null;var mirror=YuanJiActiveClaims(ownerSeat,"ol:jinghua").Any(c=>c.CardId==cardId);var water=YuanJiActiveClaims(ownerSeat,"ol:shuiyue").Any(c=>c.CardId==cardId);return mirror?(water?"镜花·水月":"镜花"):water?"水月":null;
 }
 private IReadOnlyList<ProgramTriggerCandidate> IncludeYuanJiIssuedReturns(CharacterState owner,SkillProgramTriggerWindow window,IReadOnlyList<ProgramTriggerCandidate> ordinary)
 {
  if(!HasYuanJiCapability||!owner.IsAlive||window is not(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or SkillProgramTriggerWindow.RoundEnding)||window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&owner.Seat!=_currentSeat)return ordinary;
  var skill=window==SkillProgramTriggerWindow.RoundEnding?"ol:shuiyue":"ol:jinghua";var binding=window==SkillProgramTriggerWindow.RoundEnding?"round-top":"turn-bottom";var claims=YuanJiActiveClaims(owner.Seat,skill).ToArray();if(claims.Length==0)return ordinary;
  var first=claims[0];var index=ProgramEventHistory<YuanJiStartedEvent>().Select((e,i)=>(e,i)).Single(x=>x.e.FrameId==first.DrawFrameId).i;var candidate=new ProgramTriggerCandidate(owner.Seat,skill,binding,first.Source.SkillInstanceId,first.GameplayHash,0,index);
  if(ordinary.Contains(candidate))return ordinary;return Array.AsReadOnly(ordinary.Append(candidate).ToArray());
 }
 private bool YuanJiWindowCandidate(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context,out ResolutionFrame parent)
 {
  parent=_resolutionStack.SingleOrDefault(w=>w.Id==context.ParentFrameId)!;if(parent is CardsMovedTriggerWindowFrame moved)return context.Window==SkillProgramTriggerWindow.CardsMoved&&moved.CandidateIndex>=0&&moved.CandidateIndex<moved.Candidates.Count&&moved.Candidates[moved.CandidateIndex]==candidate;
  if(parent is ProgramLifecycleTriggerWindowFrame w)return w.Window==context.Window&&w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&w.Candidates[w.CandidateIndex]==candidate;
  return parent is DeferredTurnEndFrame end&&context.Window==SkillProgramTriggerWindow.AfterTurnEnded&&IsActualAfterTurnEndedParent(end)&&AfterTurnEndedCandidate(end)==candidate;
 }
 private bool IsIssuedYuanJiReturn(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
 {
  if(!HasYuanJiCapability||!_players[c.OwnerSeat].IsAlive||context.OwnerSeat!=c.OwnerSeat||context.OccurrenceIndex!=c.OccurrenceIndex||!YuanJiWindowCandidate(c,context,out var parent))return false;
  if(context.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow?(c.SkillId!="ol:jinghua"||c.BindingId!="turn-bottom"||c.OwnerSeat!=_currentSeat):(context.Window!=SkillProgramTriggerWindow.RoundEnding||c.SkillId!="ol:shuiyue"||c.BindingId!="round-top"))return false;
  if(context.Window==SkillProgramTriggerWindow.RoundEnding&&(parent is not ProgramLifecycleTriggerWindowFrame{RoundEndingBoundaryId:{}id}||!_resolutionStack.Any(f=>f is RoundEndingBoundaryFrame&&f.Id==id)))return false;
  var claims=YuanJiActiveClaims(c.OwnerSeat,c.SkillId).ToArray();if(claims.Length==0)return false;var started=ProgramEventHistory<YuanJiStartedEvent>().ToArray();return c.OccurrenceIndex>=0&&c.OccurrenceIndex<started.Length&&started[c.OccurrenceIndex].FrameId==claims[0].DrawFrameId&&claims[0].Source.SkillInstanceId==c.SkillInstanceId&&claims[0].GameplayHash==c.GameplayHash;
 }
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition,27540,SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair,SkillProgramEffectOp.ResolveJinghuaRoundDraw,SkillProgramEffectOp.ResolveJinghuaTurnReturn,SkillProgramEffectOp.ResolveShuiyueTurnDraw,SkillProgramEffectOp.ResolveShuiyueRoundReturn)]
 private bool QualifyYuanJi(ProgramTriggerLegalityContext q)
 {
  if(q.WindowContext is not{}context||!q.Owner.IsAlive)return false;var c=q.Candidate;
  var op=q.Trigger.Effects[0].Op;if(op==SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair)return IsIssuedJieyanCandidate(c,context);if(IsYuanJiReturnOp(op))return IsIssuedYuanJiReturn(c,context);
  if(!YuanJiWindowCandidate(c,context,out _))return false;
  return op==SkillProgramEffectOp.ResolveJinghuaRoundDraw?context.Window==SkillProgramTriggerWindow.RoundStarting&&_roundTurnSeats.Count==1&&ProgramEventHistory<RoundStartedEvent>().Any(e=>e.RoundNumber==_roundNumber&&e.ActorSeat==_currentSeat)&&!ProgramEventHistory<YuanJiStartedEvent>().Any(e=>e.Source.OwnerSeat==q.OwnerSeat&&e.Op==op&&e.Round==_roundNumber):context.Window==SkillProgramTriggerWindow.AfterTurnEnded&&q.OwnerSeat==_currentSeat&&!ProgramEventHistory<YuanJiStartedEvent>().Any(e=>e.Source.OwnerSeat==q.OwnerSeat&&e.Op==op&&e.ActualTurn==_turnNumber);
 }
}
