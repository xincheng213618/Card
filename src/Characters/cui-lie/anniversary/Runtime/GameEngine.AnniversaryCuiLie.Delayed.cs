namespace CardGame.Core;
public sealed partial class GameEngine
{
 // The native use owns the existing sequential cursor and the unique entity.
 // The typed paid Chibi fact admits only this exact new delayed-card producer.
 private bool HasAnniversaryCuiLieDelayedTail(CardUseFrame use)=>IsDelayedCard(use.CardKind)&&use.CardId>0&&use.Action is{Type:CardActionType.Use}a&&a.EffectiveKind==use.CardKind&&a.TargetSeats.SequenceEqual(use.TargetSeats)&&use.TargetSeats.Count>1&&use.TargetSeats.Distinct().Count()==use.TargetSeats.Count&&use.TargetSeats.All(IsValidPlayerSeat)&&ProgramEventHistory<AnniversaryCuiLieUseChangedEvent>().Any(e=>e.UseId==use.Id&&!e.Cancelled&&e.AfterTargets.Count>1&&use.TargetSeats.Take(e.AfterTargets.Count).SequenceEqual(e.AfterTargets)&&IsAnniversaryCuiLieTargetMutationFact(use,e,e.BeforeTargets));
 private bool IsAnniversaryCuiLieDelayedMaterialPlaced(CardUseFrame use)
 {
  if(!HasAnniversaryCuiLieDelayedTail(use))return false;
  var placed=ProgramEventHistory<DelayedCardPlacedEvent>().Where(e=>e.ResolutionId==use.Id&&e.CardId==use.CardId&&e.CardKind==use.CardKind&&e.SourceSeat==use.SourceSeat).ToArray();
  return placed is[var p]&&use.TargetSeats.Take(Math.Min(use.TargetIndex+1,use.TargetSeats.Count)).Contains(p.TargetSeat)&&_cardZones.GetLocation(use.CardId)==CardLocation.Judgment(p.TargetSeat)&&ProgramEventHistory<AnniversaryCuiLieUseChangedEvent>().Any(e=>e.UseId==use.Id&&!e.Cancelled&&IsAnniversaryCuiLieTargetMutationFact(use,e,e.BeforeTargets)&&_cardMovements.Count(m=>m.Sequence>e.MaterialSequenceBefore&&m.CardId==use.CardId&&m.From==CardLocation.Processing&&m.To==CardLocation.Judgment(p.TargetSeat)&&m.Reason==CardMoveReasons.DelayedCardPlace)==1);
 }
 private bool CuiLieDelayedTargetLegal(CardUseFrame use,int target)=>_players[target].IsAlive&&!_players[target].JudgmentAreaAbolished&&!HasJudgmentEffectiveCard(_players[target],use.CardKind)&&!HasCardPolicy(_players[target],SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget)&&(use.CardKind==CardKind.Lightning||target!=use.SourceSeat||IsLiuYanLimuUse(use.Id));
 private IReadOnlyList<int> AnniversaryCuiLieDelayedPlacementTargets(JizhiResolution pending,IReadOnlyList<int> original)
 {
  if(LifecycleCardUse(pending.ResolutionId) is not{}use||!HasAnniversaryCuiLieDelayedTail(use))return original;
  if(use.SequentialTrick is not{}continuation||continuation.ActionKind!=pending.ActionKind||pending.PlayerSeat!=use.SourceSeat||pending.Card.Id!=use.CardId||pending.EffectiveCardKind!=use.CardKind||use.TargetIndex<0||use.TargetIndex>=use.TargetSeats.Count||!original.SequenceEqual(use.TargetSeats)&&!original.SequenceEqual([use.TargetSeats[use.TargetIndex]]))throw new InvalidOperationException("Chibi delayed placement lost its original direct native effect and current target.");
  return Array.AsReadOnly(new[]{use.TargetSeats[use.TargetIndex]});
 }
 private void SkipCuiLieDelayedTarget(CardUseFrame use,int target,CardEffectSkipReason reason)
 {
  MarkCardEffectIneffective(use.Id,target);AdvanceEventRulesAndQueueFact(new CardEffectSkippedEvent(use.Id,use.SourceSeat,target,use.CardKind,reason));
 }
 private bool TryBeginAnniversaryCuiLieDelayedPlacement(ProgramCardTriggerWindowFrame window)
 {
  if(LifecycleCardUse(window.ParentFrameId) is not{}use||!HasAnniversaryCuiLieDelayedTail(use))return false;
  if(window.Continuation!=ProgramCardContinuation.FinalizedDelayedPlacement||window.TrickContinuation is not{}continuation||window.Action.ActionId!=use.Action!.ActionId||!window.Action.TargetSeats.SequenceEqual(use.TargetSeats)||_resolutionStack.LastOrDefault()?.Id!=use.Id||use.SequentialTrick is not null||use.TargetIndex!=0||_cardZones.GetLocation(use.CardId)!=CardLocation.Processing)throw new InvalidOperationException("Chibi delayed placement lost its original paid entity, completed target window or first native cursor.");
  ReplaceRuntimeTop(use=use with{SequentialTrick=new(continuation.ActionKind,continuation.TargetCardId,continuation.RequiredCardKind)});
  var card=GetTrickRepresentation(use.Id,use.CardId,requireProcessing:true);var target=use.TargetSeats[0];
  if(!CuiLieDelayedTargetLegal(use,target))
  {SkipCuiLieDelayedTarget(use,target,CardEffectSkipReason.PublicTargetMissing);FinishCardUse(use.Id,card,use.CardKind);return true;}
  BeginJizhiOrNullificationWindow(use.Id,card,use.SourceSeat,use.TargetSeats,continuation.ActionKind,playedCardKind:use.CardKind);return true;
 }
 private bool ContinueAnniversaryCuiLieDelayedPlacement(CardUseFrame use)
 {
  if(!HasAnniversaryCuiLieDelayedTail(use)||use.SequentialTrick is not{}continuation||_resolutionStack.LastOrDefault()?.Id!=use.Id)throw new InvalidOperationException("Chibi delayed placement requires its exact existing sequential native parent.");
  var next=use.TargetIndex+1;
  while(next<use.TargetSeats.Count&&_winner==Winner.None)
  {
   var target=use.TargetSeats[next];var location=_cardZones.GetLocation(use.CardId);
   ReplaceRuntimeTop(use=use with{TargetIndex=next,Step=ResolutionFrameStep.ResolvingEffect});
   if(location!=CardLocation.Processing)
   {
    if(!IsAnniversaryCuiLieDelayedMaterialPlaced(use)&&!IsClaimedUseCardEntity(use.Id,use.CardId))throw new InvalidOperationException("Chibi cannot skip a delayed entity that left through an unproven destination.");
    SkipCuiLieDelayedTarget(use,target,CardEffectSkipReason.PublicTargetMissing);next++;continue;
   }
   if(!CuiLieDelayedTargetLegal(use,target)){SkipCuiLieDelayedTarget(use,target,CardEffectSkipReason.PublicTargetMissing);next++;continue;}
   // Delayed cards are directly placed. Their counterspell window belongs to
   // the later Judgment effect, exactly as for an ordinary native placement.
   SetCardUseStep(use.Id,ResolutionFrameStep.ResolvingEffect);ResolveNullifiableEffect(new NullificationWindowFrame(0,use.Id,use.SourceSeat,Array.AsReadOnly(new[]{target}),use.CardId,use.CardKind,continuation.ActionKind,null,continuation.RequiredCardKind,[]));return true;
  }
  ReplaceRuntimeTop(use with{TargetIndex=use.TargetSeats.Count,SequentialTrick=null});return false;
 }
 private bool TrySkipAnniversaryCuiLieDelayedTarget(NullificationWindowFrame window)
 {
  if(LifecycleCardUse(window.ParentFrameId) is not{}use||!HasAnniversaryCuiLieDelayedTail(use))return false;
  if(use.SequentialTrick is not{}continuation||window.Id!=0||window.DelayedReturn is not null||window.ActionKind!=continuation.ActionKind||window.EffectCardId!=use.CardId||window.EffectCardKind!=use.CardKind||window.SourceSeat!=use.SourceSeat||use.TargetIndex<0||use.TargetIndex>=use.TargetSeats.Count||!window.TargetSeats.SequenceEqual([use.TargetSeats[use.TargetIndex]])||_resolutionStack.LastOrDefault()?.Id!=use.Id)throw new InvalidOperationException("Chibi delayed effect lost its exact current direct native placement.");
  var target=use.TargetSeats[use.TargetIndex];var location=_cardZones.GetLocation(use.CardId);
  if(location==CardLocation.Processing&&CuiLieDelayedTargetLegal(use,target))return false;
  if(location!=CardLocation.Processing&&!IsAnniversaryCuiLieDelayedMaterialPlaced(use)&&!IsClaimedUseCardEntity(use.Id,use.CardId))throw new InvalidOperationException("Chibi cannot consume an unproven delayed material return.");
  SkipCuiLieDelayedTarget(use,target,CardEffectSkipReason.PublicTargetMissing);FinishCardUse(use.Id,EntityAtCurrentLocation(use.CardId),use.CardKind);return true;
 }
}
