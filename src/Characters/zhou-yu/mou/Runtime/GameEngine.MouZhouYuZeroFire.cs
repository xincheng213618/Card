namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool IsMouZyZeroFireUse(long id)=>LifecycleCardUse(id)is{CardId:0,CardKind:CardKind.FireAttack,PhysicalCardIds.Count:0,MouZhouYuYingmou:{CardId:0,Kind:CardKind.FireAttack}n,Action:{Type:CardActionType.Use,PhysicalCards.Count:0,ConversionChain.Count:0,EffectiveIsRed:not true,RequesterSeat:null}a}u&&
  _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==n.FrameId)is{MouZhouYuAnniversary:{Yin:false}}p&&n.Actor==p.OwnerSeat&&a.ActorSeat==p.OwnerSeat&&a.ProviderSeat==p.OwnerSeat&&(a.EffectiveSuit is null or Suit.None)&&(a.EffectiveRank is null or 0)&&a.TargetSeats.SequenceEqual(u.TargetSeats)&&MouZyNativeRidesOn(u,p);
 private IReadOnlyList<int>? MouZyNativeAncestorProcessing(long id)
 {
  if(LifecycleCardUse(id)is not{MouZhouYuYingmou:{}n}use||_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==n.FrameId)is not{}parent||!MouZyNativeRidesOn(use,parent))return null;
  var at=_resolutionStack.FindIndex(f=>f.Id==id);var ownerAt=_resolutionStack.FindIndex(f=>f.Id==parent.Id);
  if(at!=ownerAt+1||!_resolutionStack.Take(ownerAt).OfType<CardUseFrame>().Any(u=>u.Action?.ActionId==parent.MouZhouYuAnniversary!.OriginalAction))return null;
  var own=use.PhysicalCardIds.ToHashSet();
  return Array.AsReadOnly(_resolutionStack.Take(at).OfType<CardUseFrame>().SelectMany(u=>u.Action?.PhysicalCards.Select(m=>m.CardId)??u.PhysicalCardIds).Where(card=>card>0&&!own.Contains(card)&&_cardZones.GetLocation(card)==CardLocation.Processing).Distinct().ToArray());
 }
 private bool IsMouZyZeroFireAttackConsistent(CardAttackHandle attack,IReadOnlyList<Card> processing)
 {
  if(!IsMouZyZeroFireUse(attack.ResolutionId)||attack.Card is not null||attack.PhysicalCards.Count!=0||attack.EffectiveCardKind!=CardKind.FireAttack||attack.IsSourceLess||attack.IsDelayedJudgmentDamage||attack.ProgramSkillFrameId is not null||attack.ProgramJudgmentFrameId is not null||LifecycleCardUse(attack.ResolutionId)is not{}use||attack.SourceSeat!=use.SourceSeat||attack.CardUserSeat!=use.SourceSeat||ActiveCardAttack is not{}active||!SameAttackOwner(active,attack))return false;
  var ancestors=MouZyNativeAncestorProcessing(use.Id);return ancestors is not null&&processing.All(card=>ancestors.Contains(card.Id));
 }
 [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion,36000)]private bool FinishMouZyZeroFireAttack(CardAttackHandle attack,out bool paused)
 {
  paused=false;if(attack.Card is not null||attack.EffectiveCardKind!=CardKind.FireAttack||!IsMouZyZeroFireUse(attack.ResolutionId))return false;
  var use=LifecycleCardUse(attack.ResolutionId)!;SetCardUseStep(use.Id,ResolutionFrameStep.Completed);AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id,0,CardKind.FireAttack));
  if(_winner==Winner.None&&TryBeginProgramCardWindow(attack,use.Action!,SkillProgramTriggerWindow.CardUseCompleted,use.TargetSeats,ProgramCardContinuation.CompletedSlash,cardUseCausedDamage:attack.CardUseCausedDamage))paused=true;
  else{PopFinishedCardUse(use.Id);CompleteFinishedAttackCardUse(attack);}return true;
 }
}
