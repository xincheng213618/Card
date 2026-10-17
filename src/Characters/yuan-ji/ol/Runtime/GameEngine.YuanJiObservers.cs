namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool YuanJiParent(ProgramSkillFrame f)
 {
  if(f.YuanJi is not{}r||f.WindowContext is not{}c||c.ParentFrameId!=r.ParentId||c.Window!=r.Window)return false;var i=_resolutionStack.FindIndex(x=>x.Id==f.Id);if(i<1||_resolutionStack[i-1].Id!=r.ParentId)return false;
  var candidate=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,0,c.OccurrenceIndex);if(!YuanJiWindowCandidate(candidate,c,out var parent))return false;
  if(r.Op==SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair)return parent is CardsMovedTriggerWindowFrame moved&&JieyanOpportunity(candidate)is{}loss&&loss.Index==r.OpportunityIndex&&loss.LoserSeat==r.LoserSeat&&ValidJieyanOpportunity(loss,moved.Batch)&&CreateJieyanContext(moved,candidate)==c;
  if(r.Op==SkillProgramEffectOp.ResolveShuiyueTurnDraw)return parent is DeferredTurnEndFrame end&&end.OwnerSeat==f.OwnerSeat&&MatchesAfterTurnEndedChild(end,f,c);
  if(parent is not ProgramLifecycleTriggerWindowFrame window||!MountObserverCandidateMatches(f,window.Candidates[window.CandidateIndex]))return false;
  if(r.Op==SkillProgramEffectOp.ResolveShuiyueRoundReturn)return window is{Window:SkillProgramTriggerWindow.RoundEnding,RoundEndingBoundaryId:{}id}&&i>=2&&_resolutionStack[i-2]is RoundEndingBoundaryFrame root&&root.Id==id&&r.Round==_roundNumber;
  return r.Op==SkillProgramEffectOp.ResolveJinghuaTurnReturn?window.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&f.OwnerSeat==_currentSeat:window.Window==SkillProgramTriggerWindow.RoundStarting&&ProgramEventHistory<RoundStartedEvent>().Any(e=>e.RoundNumber==r.Round&&e.ActorSeat==_currentSeat);
 }
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27540)]private void AssertYuanJi(ProgramSkillFrame f)
 {
  if(f.YuanJi is not{}r)return;
  if(f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||f.CardSetBindings.Count!=0||f.ChoiceBindings.Count!=0||f.NumberBindings.Count!=0||GetPausedPrivateOfferEffect(f)?.Op!=r.Op||!YuanJiComposition.IsOp(r.Op)||r.Source!=YuanJiSource(f)||r.GameplayHash!=f.GameplayHash||!YuanJiParent(f)||r.Round!=_roundNumber||r.ActualTurn!=_turnNumber||r.TurnOwner!=_turnProgression.OwnerSeat||r.Before<0||r.After<r.Before||r.After>_movementSequence||!Enum.IsDefined(r.Stage)||ProgramEventHistory<YuanJiStartedEvent>().Count(e=>e==new YuanJiStartedEvent(f.Id,r.Source,r.GameplayHash,r.Op,r.ParentId,r.Window,r.ActualTurn,r.TurnOwner,r.Round,r.LoserSeat,r.OpportunityIndex))!=1)throw new InvalidOperationException("YuanJi lost its native mother, exact original source/hash, committed instruction or scalar start.");
  var pair=r.Op==SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair;if(r.Cursor<0||r.Cursor>(pair?2:1)||r.Draws.Count!=(r.Stage==YuanJiStage.DrawChildren?r.Cursor+1:r.Cursor)||r.Draws.Select(d=>d.Index).Where((n,i)=>n!=i).Any())throw new InvalidOperationException("YuanJi cannot repeat a draw or merge the two references to the same participant.");
  foreach(var d in r.Draws)
  {
   var recipient=pair&&d.Index==1?r.LoserSeat:f.OwnerSeat;var reason=pair?YuanJiPairReason:r.Op==SkillProgramEffectOp.ResolveJinghuaRoundDraw?YuanJiJinghuaReason:YuanJiShuiyueReason;
   var movements=_cardMovements.Where(m=>m.Sequence>d.Before&&m.Sequence<=d.After&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(recipient)&&m.Reason.Value==reason).ToArray();
   if(d.RecipientSeat!=recipient||d.Requested!=(pair?1:2)||d.Actual<0||d.Actual>d.Requested||d.Before<0||d.After<d.Before||movements.Length!=d.Actual||movements.Select(m=>m.CardId).Distinct().Count()!=d.Actual||ProgramEventHistory<YuanJiDrawCompletedEvent>().Count(e=>e==new YuanJiDrawCompletedEvent(f.Id,d.Index,recipient,d.Top,d.Requested,d.Actual,d.Before,d.After))!=1||d.Actual>0&&ProgramEventHistory<NativeDrawInvocationRecordedEvent>().Count(e=>e.ParentFrameId==f.Id&&e.OwnerSeat==recipient&&e.Requested==d.Requested&&e.ActualCount==d.Actual&&e.SequenceBefore==d.Before&&e.SequenceAfter==d.After&&e.DirectProducer==r.Source&&e.Reason.Value==reason)!=1)throw new InvalidOperationException("YuanJi lost the exact independent native whole-draw payment.");
   if(pair){var shown=ProgramEventHistory<YuanJiShownEvent>().Where(e=>e.FrameId==f.Id&&e.Index==d.Index).ToArray();if(d.Top!=(d.Index==0)||(d.Actual==0?(shown.Length!=0||d.CardId is not null||d.Suit is not null):(shown is not[var s]||s.RecipientSeat!=recipient||s.CardId!=d.CardId||s.Suit!=d.Suit||s.MovementSequence!=movements[0].Sequence||s.CardId!=movements[0].CardId)))throw new InvalidOperationException("Jieyan lost its exact publicly shown opposite-end physical card.");}
   else{var marks=ProgramEventHistory<YuanJiAcquiredEvent>().Where(e=>e.FrameId==f.Id).OrderBy(e=>e.Index).ToArray();if(marks.Length!=d.Actual||marks.Where((e,i)=>e.Index!=i||e.CardId!=movements[i].CardId||e.MovementSequence!=movements[i].Sequence).Any())throw new InvalidOperationException("YuanJi marks must be the exact real two-card draw entities.");}
  }
  if(IsYuanJiReturnOp(r.Op))
  {
   if(r.Claims.Count==0||r.Claims.Select(c=>(c.DrawFrameId,c.Index,c.CardId)).Distinct().Count()!=r.Claims.Count||r.Eligible.Distinct().Count()!=r.Eligible.Count||r.Eligible.Any(id=>!r.Claims.Any(c=>c.CardId==id))||r.Ordered.Distinct().Count()!=r.Ordered.Count||r.Ordered.Any(id=>!r.Eligible.Contains(id)))throw new InvalidOperationException("YuanJi return cannot invent, duplicate or adopt a foreign mark.");
   foreach(var claim in r.Claims)if(ProgramEventHistory<YuanJiStartedEvent>().Count(e=>e.FrameId==claim.DrawFrameId&&e.Source==claim.Source&&e.GameplayHash==claim.GameplayHash&&e.Source.OwnerSeat==f.OwnerSeat&&e.Source.SkillId==f.SkillId&&IsYuanJiMarkDraw(e.Op))!=1||ProgramEventHistory<YuanJiAcquiredEvent>().Count(a=>a==new YuanJiAcquiredEvent(claim.DrawFrameId,claim.Index,claim.CardId,claim.MovementSequence))!=1)throw new InvalidOperationException("YuanJi lost its original draw-source lifetime invoice.");
   if(!r.Ordered.Select((id,i)=>new YuanJiReturnSelectedEvent(f.Id,i,id)).SequenceEqual(ProgramEventHistory<YuanJiReturnSelectedEvent>().Where(e=>e.FrameId==f.Id)))throw new InvalidOperationException("YuanJi changed the privately selected exact return order.");
   if(r.Stage==YuanJiStage.ReturnChildren&&(r.Ordered.Count!=r.Eligible.Count||ProgramEventHistory<YuanJiReturnPaidEvent>().Count(e=>e==new YuanJiReturnPaidEvent(f.Id,r.Ordered.Count,r.Op==SkillProgramEffectOp.ResolveShuiyueRoundReturn,r.Before,r.After))!=1||r.Claims.Any(c=>ProgramEventHistory<YuanJiMarkClosedEvent>().Count(e=>e.DrawFrameId==c.DrawFrameId&&e.Index==c.Index&&e.CardId==c.CardId)!=1)))throw new InvalidOperationException("YuanJi cannot expire or return a mark twice.");
  }
  else if(r.Claims.Count!=0||r.Eligible.Count!=0||r.Ordered.Count!=0)throw new InvalidOperationException("A YuanJi draw cannot claim a previous marked hand.");
  if(r.Stage is YuanJiStage.DrawChildren or YuanJiStage.ReturnChildren){if(f.PendingMovementContinuation is not{SubjectSeat:var subject,BeforeCount:0,CoverageResultBind:null}||subject!=f.OwnerSeat||_cardMovements.Where(m=>m.Sequence>r.Before&&m.Sequence<=r.After).Any(m=>!IsYuanJiOwnMovement(f,m)))throw new InvalidOperationException("YuanJi lost its exact native movement range.");}
  else if(f.PendingMovementContinuation is not null||r.Before!=0||r.After!=0)throw new InvalidOperationException("An unpaid YuanJi choice cannot claim movement.");
  if(_resolutionStack.LastOrDefault()?.Id==f.Id&&_pendingDecision is{}prompt&&(r.Stage!=YuanJiStage.ReturnOrder||prompt.PlayerSeat!=f.OwnerSeat||!prompt.IsPrivate||prompt.SkillPrompt?.SkillId!=f.SkillId||!AssistedChoicesEqual(prompt.Choices,YuanJiOrderChoices(f))))throw new InvalidOperationException("YuanJi lost its private exact original-card ordering menu.");
 }
 private bool IsYuanJiOwnMovement(ProgramSkillFrame f,CardMovementRecord m)
 {
  if(f.YuanJi is not{}r||m.Sequence<=r.Before||m.Sequence>r.After)return false;
  if(r.Stage==YuanJiStage.ReturnChildren)return m.From==CardLocation.Hand(f.OwnerSeat)&&m.To==CardLocation.DrawPile&&r.Eligible.Contains(m.CardId)&&m.Reason.Value==(r.Op==SkillProgramEffectOp.ResolveJinghuaTurnReturn?YuanJiBottomReason:YuanJiTopReason);
  if(r.Stage!=YuanJiStage.DrawChildren||r.Draws.LastOrDefault()is not{}d)return false;return m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile&&m.Reason==CardMoveReasons.Reshuffle||m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(d.RecipientSeat)&&m.Reason.Value==(r.Op==SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair?YuanJiPairReason:r.Op==SkillProgramEffectOp.ResolveJinghuaRoundDraw?YuanJiJinghuaReason:YuanJiShuiyueReason);
 }
 private bool IsYuanJiMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>f.YuanJi is{Stage:YuanJiStage.DrawChildren or YuanJiStage.ReturnChildren}r&&p==f.PendingMovementContinuation&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null&&e?.Op==r.Op;
 private bool YuanJiFirstChild(ProgramSkillFrame f,ResolutionFrame child)
 {
  if(f.YuanJi is not{Stage:YuanJiStage.DrawChildren or YuanJiStage.ReturnChildren})return false;AssertYuanJi(f);
  if(child is CardsMovedTriggerWindowFrame moved)return ProgramMovementReturnMatches(f,moved)&&moved.Batch.ParentFrameId==f.Id&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>IsYuanJiOwnMovement(f,m));
  if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement;
  if(child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills)return skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
  if(child is ProgramLifecycleTriggerWindowFrame state&&state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange)return state.ResumeProgramFrameId==f.Id;
  return child is RecoveryReplacementFrame recovery&&IsYuanJiRecoveryParent(f,recovery);
 }
 private bool YuanJiObserverEdge(int n)=>n>0&&_resolutionStack[n-1]is ProgramSkillFrame f&&YuanJiFirstChild(f,_resolutionStack[n]);
 private ProgramSkillFrame? YuanJiObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.YuanJiFirstChild(f,c),static(e,i)=>e.HasObserverSuffix(i,static(g,n)=>g.IsMovementObserverEdge(n)||g.YuanJiObserverEdge(n)||g.IsYanPuDyingExitEdge(n)||g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n],g._resolutionStack[n-1]),static(g,n,d)=>g.IsSuspendedOrPaidRescueObserverSuffix(n,d)));
 private bool HasYuanJiDamageObserver(long id)=>YuanJiObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 private bool HasYuanJiBeforeDamageObserver(long id)=>YuanJiObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 private bool IsYuanJiDying()=>ActiveDying is{}d&&YuanJiObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 private bool AllowsYuanJiNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
 {if(f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||YuanJiObserverRoot()is not{}root||root.Id==f.Id)return false;var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));}
 private bool IsYuanJiRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)=>f.YuanJi is{Stage:YuanJiStage.DrawChildren}r&&r.Draws.Count>0&&RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement;
}
