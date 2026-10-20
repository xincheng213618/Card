namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedAfterCategory, 100)]
    private bool YisuanFinalizedAfterCategoryTargetWindow(ProgramTargetCardWindowQuery query) =>
        HasYisuanCapability && query.Category == SkillProgramCardCategory.Trick;


 private bool HasYisuanCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.PayMaxHpAndObtainActualUsedTrick);
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 480)]
 private void ObserveLiJueNativeEvent(IGameEvent payload)
 {
  if(!HasYisuanCapability||_phase!=TurnPhase.Play||_turnNumber<1)return;
  if(payload is CardUseDeclaredEvent declared&&LifecycleCardUse(declared.ResolutionId)is{Action:{Type:CardActionType.Use}action}use&&action.ActorSeat==_currentSeat&&action.PhysicalCards.Count>0&&GetProgramCardCategory(action.EffectiveKind)==SkillProgramCardCategory.Trick)
  {
   if(use.LiJueActualUse is not null)throw new InvalidOperationException("Yisuan native declaration origin was issued twice.");
   ReplaceRuntimeFrame(use.Id,use with{LiJueActualUse=new(use.Id,_turnNumber,_currentSeat,_cardUseDebitPhaseInstanceId,_movementSequence,action)});
  }
  else if(payload is CardActionAcceptedEvent { Action: { Type:CardActionType.Response, EffectiveKind:CardKind.Nullification } response })
  {
   var native = _resolutionStack.OfType<NullificationWindowFrame>().LastOrDefault();
   if(native is not null) CaptureLiJueNullificationInvoice(native,response,true,null);
  }
  else if(payload is NullificationRespondedEvent responded)
  {
   var native = _resolutionStack.OfType<NullificationWindowFrame>().LastOrDefault();
   if(native is null || native.ParentFrameId!=responded.ResolutionId || native.ChainDepth!=responded.ChainDepth)return;
   var recentAction=CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Select(e=>e.Action).LastOrDefault(a=>a.Type==CardActionType.Response&&a.EffectiveKind==CardKind.Nullification&&a.ActorSeat==responded.ResponderSeat&&a.PhysicalCards.Any(c=>c.CardId==responded.NullificationCardId)&&a.ParentActionId==NullificationParentActionId(native));
   if(recentAction is not null)CaptureLiJueNullificationInvoice(native,recentAction,false,responded);
  }
 }
 // Called after the real deferred counterspell binds its native action. The
 // single-card producer has not accepted yet; its later Accepted fact handles it.
 private void CaptureLiJueCompletedNullificationUse(long windowId,CardActionContext action)
 {
  if(!HasYisuanCapability||_phase!=TurnPhase.Play||_turnNumber<1)return;
  if(_resolutionStack.SingleOrDefault(f=>f.Id==windowId)is NullificationWindowFrame window)
   CaptureLiJueNullificationInvoice(window,action,false,null);
 }
 private void CaptureLiJueNullificationInvoice(NullificationWindowFrame window,CardActionContext action,bool incomingAccepted,NullificationRespondedEvent? incomingResponse)
 {
  if(_phase!=TurnPhase.Play||_turnNumber<1||action.Type!=CardActionType.Response||action.EffectiveKind!=CardKind.Nullification||action.ActorSeat!=_currentSeat||action.ProviderSeat!=action.ActorSeat||action.ResponderSeat!=action.ActorSeat||action.RequesterSeat is not null||action.PhysicalCards.Count==0||!HasNullificationEffectOwner(window)||window.ChainDepth<1||action.ParentActionId!=NullificationParentActionId(window)||action.OpponentSeat!=window.SourceSeat)return;
  var history=CompleteProgramEventHistory().ToArray();
  if(!incomingAccepted&&!history.OfType<CardActionAcceptedEvent>().Any(e=>e.Action.ActionId==action.ActionId&&SameOverflowAction(e.Action,action)))return;
  var deferred=window.NamedAmbushResponse is{}named&&named.ActionId==action.ActionId&&named.ResponderSeat==action.ActorSeat&&action.PhysicalCards.Any(c=>c.CardId==named.CardId)&&window.ChainDepth==named.PreviousChainDepth+1&&window.EffectNullified==named.PreviousEffectNullified;
  bool Matches(NullificationRespondedEvent e)=>e.ResolutionId==window.ParentFrameId&&e.ResponderSeat==action.ActorSeat&&e.ChainDepth==window.ChainDepth&&e.EffectCardId==window.EffectCardId&&e.EffectCardKind==window.EffectCardKind&&e.NullificationCardId==action.PhysicalCards[0].CardId;
  if(!deferred&&!(incomingResponse is{}r&&Matches(r))&&!history.OfType<NullificationRespondedEvent>().Any(Matches))return;
  var finished=new List<CardMovementRecord>();
  foreach(var cost in action.PhysicalCards)
  {
   var exit=_cardMovements.LastOrDefault(m=>m.CardId==cost.CardId&&m.TurnNumber==_turnNumber&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason is var reason&&(reason==CardMoveReasons.NullificationFinished||reason==CardMoveReasons.ResponseFinished));
   if(exit is null||_cardZones.GetLocation(cost.CardId)!=CardLocation.DiscardPile)return;
   var entry=_cardMovements.LastOrDefault(m=>m.CardId==cost.CardId&&m.TurnNumber==_turnNumber&&m.Sequence<exit.Sequence&&m.From==cost.From&&m.To==CardLocation.Processing);
   if(entry is null)return;
   if(exit.Reason==CardMoveReasons.ResponseFinished)
   {
    if(entry.Reason!=CardMoveReasons.Respond||!history.OfType<ProgramViewAsConvertedEvent>().Any(e=>e.FrameId==window.ParentFrameId&&!e.IsUse&&e.OwnerSeat==action.ActorSeat&&e.OutputKind==CardKind.Nullification&&e.PhysicalCardIds.SequenceEqual(action.PhysicalCards.Select(c=>c.CardId))&&action.ConversionChain.Any(c=>c.SkillId==e.SkillId&&c.BindingId==e.BindingId)))return;
   }
   else if(entry.Reason!=CardMoveReasons.Nullification&&!(entry.Reason.Value=="conversion.declaration.pay"&&IsResponseCompletionEntry(action,ProgramCardContinuation.NullificationResponse,window.Id,cost,entry)))return;
   finished.Add(exit);
  }
  // Multi-card finish is several genuine batches: only the final batch carries
  // the complete invoice, never an invented merged source count or early claim.
  var last=finished.Max(m=>m.Sequence);
  var index=_pendingCardsMovedBatches.FindLastIndex(b=>b.ParentFrameId==window.Id&&b.TurnNumber==_turnNumber&&b.Movements.Any(m=>m.Sequence==last&&finished.Contains(m)));
  if(index<0)return;
  var batch=_pendingCardsMovedBatches[index];
  if(batch.LiJueUseDiscard is{}existing){if(existing.Action.ActionId!=action.ActionId)throw new InvalidOperationException("Yisuan cannot replace a different actual nullification invoice.");return;}
  var fact=new YisuanNullificationQualifiedEvent(batch.Id,window.Id,action.ActionId,action.ActorSeat,_turnNumber,_cardUseDebitPhaseInstanceId,window.ChainDepth,finished.Min(m=>m.Sequence),last,deferred);
  if(history.OfType<YisuanNullificationQualifiedEvent>().Any(e=>e.BatchId==batch.Id))throw new InvalidOperationException("Yisuan nullification qualification was already issued.");
  AdvanceEventRulesAndQueueFact(fact);
  _pendingCardsMovedBatches[index]=batch with{LiJueUseDiscard=new(batch.Id,window.ParentFrameId,window.Id,_turnNumber,_currentSeat,_cardUseDebitPhaseInstanceId,action,finished,true)};
 }
 private LiJueUseDiscardInvoice? CaptureLiJueUseDiscard(CardMovementBatchContext batch)
 {
  if(!HasYisuanCapability||_phase!=TurnPhase.Play||_turnNumber<1||batch.TurnNumber!=_turnNumber||!batch.Movements.Any(m=>m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.UseFinished))return null;
  foreach(var use in _resolutionStack.OfType<CardUseFrame>().Reverse())
  {
   if(use.LiJueActualUse is not{}origin||origin.ActualTurn!=_turnNumber||origin.ActualTurnOwner!=_currentSeat||origin.PhaseInstance!=_cardUseDebitPhaseInstanceId||use.Action is not{Type:CardActionType.Use}action||action.ActorSeat!=_currentSeat||action.PhysicalCards.Count==0||GetProgramCardCategory(action.EffectiveKind)!=SkillProgramCardCategory.Trick||!HasExactAcceptedActualHandGainUse(use,action)||action.PhysicalCards.Any(c=>_cardZones.GetLocation(c.CardId)!=CardLocation.DiscardPile))continue;
   var finished=new List<CardMovementRecord>();foreach(var cost in action.PhysicalCards)
   {
    var moved=_cardMovements.Where(m=>m.CardId==cost.CardId&&m.Sequence>origin.MovementBefore&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.UseFinished).ToArray();var paid=_cardMovements.Where(m=>m.CardId==cost.CardId&&m.Sequence>origin.MovementBefore&&m.From==cost.From&&m.To==CardLocation.Processing).ToArray();
    if(moved is not[var actual]||paid is not[var payment]||payment.Sequence>=actual.Sequence){finished.Clear();break;}finished.Add(actual);
   }
   if(finished.Count!=action.PhysicalCards.Count||!batch.Movements.Any(m=>finished.Contains(m)))continue;
   var nativeParent=batch.ParentFrameId==use.Id||_resolutionStack.Any(f=>f.Id==batch.ParentFrameId&&f is NullificationWindowFrame{ParentFrameId:var id}&&id==use.Id);if(!nativeParent)continue;
   return new(batch.Id,batch.ParentFrameId,use.Id,_turnNumber,_currentSeat,_cardUseDebitPhaseInstanceId,action,finished);
  }
  return null;
 }
 private bool ValidLiJueUseDiscard(LiJueUseDiscardInvoice invoice,CardMovementBatchContext batch,int owner,bool requireAvailable=true)
 {
  var action=invoice.Action;if(invoice.BatchId!=batch.Id||invoice.ActualTurn!=_turnNumber||invoice.ActualTurnOwner!=_currentSeat||invoice.PhaseInstance!=_cardUseDebitPhaseInstanceId||owner!=_currentSeat||action.ActorSeat!=owner||action.PhysicalCards.Count==0||GetProgramCardCategory(action.EffectiveKind)!=SkillProgramCardCategory.Trick||invoice.FinishedMovements.Count!=action.PhysicalCards.Count||invoice.FinishedMovements.Select(m=>m.CardId).Distinct().Count()!=invoice.FinishedMovements.Count||!batch.Movements.Any(m=>invoice.FinishedMovements.Contains(m)))return false;
  if(invoice.NullificationUse?(action.Type!=CardActionType.Response||action.EffectiveKind!=CardKind.Nullification||action.ActorSeat!=action.ProviderSeat||action.ResponderSeat!=owner||action.RequesterSeat is not null):action.Type!=CardActionType.Use)return false;
  if(invoice.NullificationUse&&(!CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Any(e=>e.Action.ActionId==action.ActionId&&SameOverflowAction(e.Action,action))||CompleteProgramEventHistory().OfType<YisuanNullificationQualifiedEvent>().Count(e=>e.BatchId==invoice.BatchId&&e.WindowId==invoice.NativeUseId&&e.ActionId==action.ActionId&&e.OwnerSeat==owner&&e.ActualTurn==invoice.ActualTurn&&e.PhaseInstance==invoice.PhaseInstance&&e.FirstSequence==invoice.FinishedMovements.Min(m=>m.Sequence)&&e.LastSequence==invoice.FinishedMovements.Max(m=>m.Sequence))!=1))return false;
  return invoice.FinishedMovements.All(m=>_cardMovements.Contains(m)&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&(invoice.NullificationUse?m.Reason==CardMoveReasons.NullificationFinished||m.Reason==CardMoveReasons.ResponseFinished:m.Reason==CardMoveReasons.UseFinished)&&action.PhysicalCards.Count(c=>c.CardId==m.CardId)==1&&(!requireAvailable||_cardZones.GetLocation(m.CardId)==CardLocation.DiscardPile));
 }
 private IReadOnlyList<ProgramTriggerCandidate> CollectLiJueUseDiscardCandidates(CardMovementBatchContext batch)
 {
  if(batch.LiJueUseDiscard is not{}invoice||!ValidLiJueUseDiscard(invoice,batch,invoice.Action.ActorSeat))return [];
  return CollectProgramTriggerCandidates(_players[invoice.Action.ActorSeat],SkillProgramTriggerWindow.CardsMoved).Where(c=>ProgramInstructionResolver.Default.Features(GetProgramTrigger(c)).HasOperation(SkillProgramEffectOp.PayMaxHpAndObtainActualUsedTrick)).ToArray();
 }
 private int[] MatchingLiJueUseDiscardIndexes(CardMovementBatchContext batch,ProgramTriggerCandidate candidate)=>batch.LiJueUseDiscard is{}invoice&&ValidLiJueUseDiscard(invoice,batch,candidate.OwnerSeat)?batch.Movements.Select((m,i)=>(m,i)).Where(x=>invoice.FinishedMovements.Contains(x.m)).Select(x=>x.i).ToArray():[];
 private ProgramSkillWindowContext CreateLiJueMovementContext(CardsMovedTriggerWindowFrame frame,ProgramTriggerCandidate candidate)
 {
  var batch=frame.Batch;var indexes=MatchingLiJueUseDiscardIndexes(batch,candidate);if(indexes.Length==0||batch.LiJueUseDiscard is not{}invoice)throw new InvalidOperationException("Yisuan context requires its exact native processing/discard invoice.");
  var count=batch.SourceCounts.Single(c=>c.Location==CardLocation.Processing);
  return new(SkillProgramTriggerWindow.CardsMoved,frame.Id,candidate.OwnerSeat,SourceSeat:invoice.Action.ProviderSeat,TargetSeat:candidate.OwnerSeat,OccurrenceIndex:candidate.OccurrenceIndex,Facts:CaptureCardsMovedTriggerFacts(_players[candidate.OwnerSeat],indexes.Length,count,SkillProgramTriggerWindow.CardsMoved,batch.MovementTiming),MovementBatch:batch,MovementIndex:indexes[0]);
 }
}
