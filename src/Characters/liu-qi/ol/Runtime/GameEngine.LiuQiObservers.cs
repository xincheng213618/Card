namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool _hasIssuedLiuQi;
 private bool HasTunjiangCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawForUnskippedUntargetedPlay);
 private static string WenjiName(CardKind kind)=>CardCatalog.Get(kind).DisplayName;
 private void ObserveLiuQiPlayBoundary(int ownerSeat,int phaseInstance,bool skipped)
 {
  if(!HasTunjiangCapability||_turnNumber<1||ownerSeat!=_currentSeat||!IsValidPlayerSeat(ownerSeat))return;
  var owner=_players[ownerSeat];var entry=new LiuQiPlayEntry(_turnNumber,ownerSeat,phaseInstance,skipped);
  if((owner.LiuQiPlayEntries??[]).Contains(entry))return;
  owner.LiuQiPlayEntries=(owner.LiuQiPlayEntries??[]).Append(entry).ToArray();_hasIssuedLiuQi=true;AdvanceEventRulesAndQueueFact(new LiuQiPlayEnteredEvent(entry));
 }
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 450)]
 private void ObserveLiuQiEvent(IGameEvent payload)
 {
  if(!_hasIssuedLiuQi&&!HasTunjiangCapability)return;
  if(payload is CardActionAcceptedEvent{Action:{Type:CardActionType.Response,EffectiveKind:CardKind.Nullification}response}&&_hasIssuedLiuQi)
  {
   var window=_resolutionStack.OfType<NullificationWindowFrame>().LastOrDefault();
   if(window is null||window.ChainDepth<=0||!HasNullificationEffectOwner(window)||response.ParentActionId!=NullificationParentActionId(window)||response.ActorSeat!=response.ProviderSeat||response.ResponderSeat!=response.ActorSeat||response.RequesterSeat is not null||response.OpponentSeat!=window.SourceSeat)return;
   var grant=(_players[response.ActorSeat].WenjiTurnNameGrants??[]).LastOrDefault(g=>g.ActualTurn==_turnNumber&&g.Source.OwnerSeat==response.ActorSeat&&response.ActorSeat==_currentSeat&&WenjiName(g.Name)==WenjiName(CardKind.Nullification));
   if(grant is null)return;
   if(response.PhysicalCards.Count==0&&!IsIssuedTieredRoundZeroResponse(window,response))return;
   if(response.PhysicalCards.Any(c=>!CompleteProgramEventHistory().OfType<NullificationRespondedEvent>().Any(e=>e.ResolutionId==window.ParentFrameId&&e.ChainDepth==window.ChainDepth&&e.ResponderSeat==response.ActorSeat&&e.NullificationCardId==c.CardId)))return;
   var qualification=new WenjiCounterspellQualification(grant,window.Id,window.ParentFrameId,window.ChainDepth,response);ReplaceRuntimeFrame(window.Id,window with{WenjiCounterspell=qualification});AdvanceEventRulesAndQueueFact(new WenjiCounterspellQualifiedEvent(window.Id,window.ParentFrameId,window.ChainDepth,response.ActionId,grant));
  }
  else if(payload is TargetsConfirmedEvent targets&&_phase==TurnPhase.Play&&_turnNumber>0&&LifecycleCardUse(targets.ResolutionId)is{}use&&use.SourceSeat==_currentSeat&&targets.TargetSeats.SequenceEqual(use.TargetSeats))
  {
   var action=use.Action;
   if(action is not null&&(action.Type!=CardActionType.Use||action.ActorSeat!=use.SourceSeat||action.EffectiveKind!=use.CardKind))return;
   if(action is null&&(use.CardId!=0||use.PhysicalCardIds is not{Count:0}||!IsActualTurnLegacyVirtualUse(use)))return;
   if(!CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Any(e=>e.ResolutionId==use.Id&&e.SourceSeat==use.SourceSeat&&e.CardId==use.CardId&&e.CardKind==use.CardKind))return;
   var seats=use.CardKind==CardKind.BorrowedSword?targets.TargetSeats.Where((_,i)=>i%2==0):targets.TargetSeats;
   foreach(var target in seats.Where(s=>s!=use.SourceSeat&&IsValidPlayerSeat(s)).Distinct())
   {
    var receipt=new LiuQiForeignTargetUse(_turnNumber,use.SourceSeat,_cardUseDebitPhaseInstanceId,use.Id,action?.ActionId,target);var owner=_players[use.SourceSeat];
    if((owner.LiuQiForeignTargetUses??[]).Contains(receipt))continue;
    owner.LiuQiForeignTargetUses=(owner.LiuQiForeignTargetUses??[]).Append(receipt).ToArray();_hasIssuedLiuQi=true;AdvanceEventRulesAndQueueFact(new LiuQiForeignUseRecordedEvent(receipt));
   }
  }
  else if(payload is TurnEndedEvent ended){var owner=_players[ended.ActorSeat];owner.WenjiTurnNameGrants=null;owner.LiuQiPlayEntries=null;owner.LiuQiForeignTargetUses=null;}
  else if(payload is PlayerDiedEvent died){var owner=_players[died.VictimSeat];owner.WenjiTurnNameGrants=null;owner.LiuQiPlayEntries=null;owner.LiuQiForeignTargetUses=null;}
 }
 private bool IsWenjiResponseForbidden(CardActionContext originalUse,int responderSeat)
 {
  if(!_hasIssuedLiuQi||originalUse.Type!=CardActionType.Use||!IsValidPlayerSeat(originalUse.ActorSeat)||responderSeat==originalUse.ActorSeat||originalUse.ActorSeat!=_currentSeat||_turnNumber<1)return false;
  var grants=_players[originalUse.ActorSeat].WenjiTurnNameGrants;if(grants is not{Count:>0})return false;
  foreach(var grant in grants)if(grant.ActualTurn==_turnNumber&&grant.Source.OwnerSeat==originalUse.ActorSeat&&WenjiName(grant.Name)==WenjiName(originalUse.EffectiveKind)&&CompleteProgramEventHistory().OfType<WenjiNameArmedEvent>().Count(e=>e.Grant==grant)==1)return true;
  return false;
 }
 private bool IsWenjiResponseForbidden(long useId,int responderSeat)
 {
  if(!_hasIssuedLiuQi||LifecycleCardUse(useId)is not{}use)return false;
  if(use.Action is{}action)return IsWenjiResponseForbidden(action,responderSeat);
  if(use.SourceSeat!=_currentSeat||responderSeat==use.SourceSeat||use.CardId!=0||use.PhysicalCardIds is not{Count:0}||!IsActualTurnLegacyVirtualUse(use)||!CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Any(e=>e.ResolutionId==use.Id&&e.SourceSeat==use.SourceSeat&&e.CardId==0&&e.CardKind==use.CardKind))return false;
  return (_players[use.SourceSeat].WenjiTurnNameGrants??[]).Any(g=>g.ActualTurn==_turnNumber&&g.Source.OwnerSeat==use.SourceSeat&&WenjiName(g.Name)==WenjiName(use.CardKind)&&CompleteProgramEventHistory().OfType<WenjiNameArmedEvent>().Count(e=>e.Grant==g)==1);
 }
 private bool IsWenjiNullificationResponseForbidden(NullificationWindowFrame window,int responderSeat)
 {
  if(!_hasIssuedLiuQi)return false;
  if(window.DelayedReturn is null&&IsWenjiResponseForbidden(window.ParentFrameId,responderSeat))return true;
  if(window.WenjiCounterspell is not{}q||q.WindowId!=window.Id||q.ParentFrameId!=window.ParentFrameId||q.ChainDepth!=window.ChainDepth||q.Grant.ActualTurn!=_turnNumber||q.Grant.Source.OwnerSeat!=q.Action.ActorSeat||responderSeat==q.Action.ActorSeat||q.Action.ActorSeat!=_currentSeat||q.Action.Type!=CardActionType.Response||q.Action.EffectiveKind!=CardKind.Nullification||q.Action.ParentActionId!=NullificationParentActionId(window)||q.Action.ActorSeat!=q.Action.ProviderSeat||q.Action.ResponderSeat!=q.Action.ActorSeat||q.Action.RequesterSeat is not null||!HasNullificationEffectOwner(window))return false;
  return CompleteProgramEventHistory().OfType<WenjiNameArmedEvent>().Count(e=>e.Grant==q.Grant)==1&&CompleteProgramEventHistory().OfType<WenjiCounterspellQualifiedEvent>().Count(e=>e==new WenjiCounterspellQualifiedEvent(window.Id,window.ParentFrameId,window.ChainDepth,q.Action.ActionId,q.Grant))==1&&CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Any(e=>e.Action.ActionId==q.Action.ActionId&&e.Action.ParentActionId==q.Action.ParentActionId&&e.Action.Type==q.Action.Type&&e.Action.ActorSeat==q.Action.ActorSeat&&e.Action.ProviderSeat==q.Action.ProviderSeat&&e.Action.EffectiveKind==q.Action.EffectiveKind);
 }
 private bool HasUntargetedUnskippedLiuQiPlay(int owner)
 {
  if(owner!=_currentSeat||_turnNumber<1||_players[owner].LiuQiPlayEntries is not{Count:>0}entries)return false;
  return entries.Any(e=>e.ActualTurn==_turnNumber&&!e.Skipped)&&!entries.Any(e=>e.ActualTurn==_turnNumber&&e.Skipped)&&!(_players[owner].LiuQiForeignTargetUses??[]).Any(e=>e.ActualTurn==_turnNumber);
 }
 private bool LiuQiFirstChild(ProgramSkillFrame root,ResolutionFrame child)
 {
  if(!CanContinueLiuQi(root)||child is not CardsMovedTriggerWindowFrame moved||root.LiuQi is not{}r)return false;
  return moved.Batch.ParentFrameId==root.Id&&moved.Batch.AwaitingProgramFrameId==root.Id&&moved.ResumeProgramFrameId is null&&moved.Batch.OriginOwnerSeat==root.OwnerSeat&&moved.Batch.OriginSkillId==root.SkillId&&moved.Batch.OriginSkillInstanceId==root.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After&&m.To==CardLocation.Hand(root.OwnerSeat)&&m.Reason.Value==(r.Stage==LiuQiStage.GiftChildren?WenjiGiftReason:TunjiangDrawReason));
 }
 private ProgramSkillFrame? LiuQiObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.LiuQiFirstChild(r,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
 private bool HasLiuQiDamageObserver(long id)=>LiuQiObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 private bool HasLiuQiBeforeDamageObserver(long id)=>LiuQiObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 private bool IsLiuQiDying()=>ActiveDying is{}d&&LiuQiObserverRoot()is{}r&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 private bool TryAdvanceLiuQiSubtree()=>_pendingDecision is null&&LiuQiObserverRoot()is not null&&TryAdvanceObserverSubtree(advanceRecoveryReplacement:true);
 private bool AllowsLiuQiNestedDamage(ProgramSkillFrame observer,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
 {
  if(sourceLess||observer.AttackAttempt is not null||observer.InstructionIndex<1||_resolutionStack.LastOrDefault()?.Id!=observer.Id||observer.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged)||LiuQiObserverRoot()is not{}r||r.Id==observer.Id)return false;
  var e=GetPausedPrivateOfferEffect(observer);return e is{Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is{}reference?ResolveProgramParticipant(observer,reference):ResolveProgramEffectTarget(observer,e.Target));
 }
}
