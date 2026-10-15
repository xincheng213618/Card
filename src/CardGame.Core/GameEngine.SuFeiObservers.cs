namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool HasSuFeiCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawThenOfferActualConsecutiveTargetGift);
 private bool SuFeiOwnerQualified(int seat)=>IsValidPlayerSeat(seat)&&_players[seat].IsAlive&&GetSkillBindingShard(_players[seat]).GetInstanceTriggers(SkillProgramTriggerWindow.CardUseTargetsFinalized).Any(b=>ProgramInstructionResolver.Default.Features(b.Trigger).HasOperation(SkillProgramEffectOp.DrawThenOfferActualConsecutiveTargetGift));
 private int[] SuFeiPrimaryTargets(CardActionContext a)
 {
  if(a.EffectiveKind==CardKind.BorrowedSword)
  {
   // Some native conversion producers already compress DesignatedTargetSeats
   // to weapon holders. Only TargetSeats retains the complete ordered pairs.
   var pairs=a.TargetSeats;if(pairs.Count==0||pairs.Count%2!=0)return [];
   return pairs.Where((_,i)=>i%2==0).Distinct().ToArray();
  }
  // NormalizeImplicitDrawTwoUseTarget historically updates TargetSeats while
  // retaining an explicitly empty designated list. Recognize only that exact
  // native implicit-self shape here; other empty designation stays empty.
  if(a.EffectiveKind==CardKind.DrawTwo&&a.EffectiveDesignatedTargetSeats.Count==0&&a.TargetSeats.Count==1&&a.TargetSeats[0]==a.ActorSeat)return [a.ActorSeat];
  return a.EffectiveDesignatedTargetSeats.Distinct().ToArray();
 }
 private void CaptureSuFeiFinalizedUse(long nativeUseId,CardActionContext action)
 {
  if(!HasSuFeiCapability||_phase!=TurnPhase.Play||_turnNumber<1||action.Type!=CardActionType.Use||action.ActorSeat!=_currentSeat||!SuFeiOwnerQualified(action.ActorSeat)&&_players[action.ActorSeat].SuFeiLastUse is null)return;
  if(LifecycleCardUse(nativeUseId)is not{Action:{}actual}use||actual.ActionId!=action.ActionId||!SameOverflowAction(actual,action)||!HasExactAcceptedActualHandGainUse(use,action))throw new InvalidOperationException("Lianpian requires the exact accepted native use and finalized target action.");
  if(use.SuFeiFinalizedUse is not null)return;
  var owner=_players[action.ActorSeat];var last=owner.SuFeiLastUse;
  if(last?.NativeUseId==nativeUseId)return;
  var current=new SuFeiActualUse(_turnNumber,_cardUseDebitPhaseInstanceId,use.Id,action,SuFeiPrimaryTargets(action));
  SuFeiConsecutiveQualification? q=null;
  if(last is not null&&last.ActualTurn==current.ActualTurn&&last.PhaseInstance==current.PhaseInstance&&last.Action.ActorSeat==action.ActorSeat&&last.NativeUseId!=use.Id)
  {
   var common=last.PrimaryTargets.Intersect(current.PrimaryTargets).Where(s=>IsValidPlayerSeat(s)).ToArray();
   if(common.Length>0)q=new(last,current,common);
  }
  owner.SuFeiLastUse=current;ReplaceRuntimeFrame(use.Id,use with{SuFeiQualification=q,SuFeiFinalizedUse=current});AdvanceEventRulesAndQueueFact(new SuFeiActualUseRecordedEvent(current));
 }
 private void ObserveSuFeiTurnOrDeath(IGameEvent payload)
 {
  if(payload is TurnEndedEvent ended&&_players[ended.ActorSeat].SuFeiLastUse is not null)_players[ended.ActorSeat].SuFeiLastUse=null;
  else if(payload is PlayerDiedEvent dead)_players[dead.VictimSeat].SuFeiLastUse=null;
 }
 private bool SuFeiUseIssued(SuFeiActualUse u)=>u.ActualTurn==_turnNumber&&u.PhaseInstance==_cardUseDebitPhaseInstanceId&&u.Action.Type==CardActionType.Use&&u.Action.ActorSeat==_currentSeat&&u.PrimaryTargets.SequenceEqual(SuFeiPrimaryTargets(u.Action))&&CompleteProgramEventHistory().OfType<SuFeiActualUseRecordedEvent>().Count(e=>e.Use.NativeUseId==u.NativeUseId&&e.Use.ActualTurn==u.ActualTurn&&e.Use.PhaseInstance==u.PhaseInstance&&SameOverflowAction(e.Use.Action,u.Action)&&e.Use.PrimaryTargets.SequenceEqual(u.PrimaryTargets))==1;
 private bool ValidSuFeiQualification(SuFeiConsecutiveQualification q,int owner)=>q.Current.Action.ActorSeat==owner&&q.Previous.Action.ActorSeat==owner&&q.Current.NativeUseId!=q.Previous.NativeUseId&&SuFeiUseIssued(q.Previous)&&SuFeiUseIssued(q.Current)&&q.CommonTargets.Count>0&&q.CommonTargets.SequenceEqual(q.Previous.PrimaryTargets.Intersect(q.Current.PrimaryTargets).Where(IsValidPlayerSeat));
 private bool SuFeiFirstChild(ProgramSkillFrame root,ResolutionFrame child)
 {
  if(!CanContinueSuFei(root)||root.SuFei is not{}r||child is not CardsMovedTriggerWindowFrame moved)return false;
  var before=r.Stage==SuFeiStage.DrawChildren?r.DrawBefore:r.GiftBefore;var after=r.Stage==SuFeiStage.DrawChildren?r.DrawAfter:r.GiftAfter;
  return r.Stage is SuFeiStage.DrawChildren or SuFeiStage.GiftChildren&&moved.Batch.ParentFrameId==root.Id&&moved.Batch.AwaitingProgramFrameId==root.Id&&moved.Batch.OriginOwnerSeat==root.OwnerSeat&&moved.Batch.OriginSkillId==root.SkillId&&moved.Batch.OriginSkillInstanceId==root.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>before&&m.Sequence<=after&&(r.Stage==SuFeiStage.DrawChildren?m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(root.OwnerSeat)&&m.Reason.Value==SuFeiDrawReason:m.CardId==r.DrawnId&&m.From==CardLocation.Hand(root.OwnerSeat)&&m.To==CardLocation.Hand(r.GiftTarget!.Value)&&m.Reason.Value==SuFeiGiftReason));
 }
 private ProgramSkillFrame? SuFeiObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.SuFeiFirstChild(r,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
 private bool HasSuFeiDamageObserver(long id)=>SuFeiObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 private bool HasSuFeiBeforeDamageObserver(long id)=>SuFeiObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 private bool IsSuFeiDying()=>ActiveDying is{}d&&SuFeiObserverRoot()is{}r&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 private bool TryAdvanceSuFeiSubtree()=>_pendingDecision is null&&SuFeiObserverRoot()is not null&&TryAdvanceObserverSubtree(advanceRecoveryReplacement:true);
 private bool AllowsSuFeiNestedDamage(ProgramSkillFrame observer,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
 {
  if(sourceLess||observer.AttackAttempt is not null||observer.InstructionIndex<1||_resolutionStack.LastOrDefault()?.Id!=observer.Id||observer.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged)||SuFeiObserverRoot()is not{}r||r.Id==observer.Id)return false;
  var e=GetPausedPrivateOfferEffect(observer);return e is{Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is{}reference?ResolveProgramParticipant(observer,reference):ResolveProgramEffectTarget(observer,e.Target));
 }
}
