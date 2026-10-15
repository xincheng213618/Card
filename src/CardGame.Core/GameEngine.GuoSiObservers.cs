namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool HasGuoSiCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.OfferSidaoCompletedPairSnatch);
 private bool GuoSiQualified(int seat)=>IsValidPlayerSeat(seat)&&_players[seat].IsAlive&&GetSkillBindingShard(_players[seat]).GetInstanceTriggers(SkillProgramTriggerWindow.CardUseCompleted).Any(b=>ProgramInstructionResolver.Default.Features(b.Trigger).HasOperation(SkillProgramEffectOp.OfferSidaoCompletedPairSnatch));
 private bool GuoSiResponseUse(CardActionContext a)
 {
  if(a.Type!=CardActionType.Response||a.ActorSeat!=a.ProviderSeat||a.RequesterSeat is not null||a.ResponderSeat!=a.ActorSeat)return false;
  if(a.EffectiveKind==CardKind.Nullification)return ActiveNullificationWindow is{}n&&a.OpponentSeat==n.SourceSeat&&a.ParentActionId==NullificationParentActionId(n);
  return a.EffectiveKind==CardKind.Dodge&&ActiveCardAttack is{}attack&&attack.EffectiveCardKind is{}incoming&&IsSlashCard(incoming)&&attack.TargetSeat==a.ActorSeat&&a.OpponentSeat==attack.SourceSeat&&LifecycleCardUse(attack.ResolutionId)?.Action?.ActionId==a.ParentActionId;
 }
 private void ObserveGuoSiAcceptedUse(IGameEvent payload)
 {
  if(payload is not CardActionAcceptedEvent e||!HasGuoSiCapability)return;var a=e.Action;
  if(_phase!=TurnPhase.Play||a.ActorSeat!=_currentSeat||a.Type!=CardActionType.Use&&!GuoSiResponseUse(a)||!GuoSiQualified(a.ActorSeat)&&_players[a.ActorSeat].GuoSiUses is null)return;
  var p=_players[a.ActorSeat];var old=p.GuoSiUses??[];if(old.Any(s=>s.ActionId==a.ActionId))return;
  p.GuoSiUses=Array.AsReadOnly(old.Where(s=>s.Turn==_turnNumber&&s.Phase==_cardUseDebitPhaseInstanceId).Append(new GuoSiUseStamp(_turnNumber,_cardUseDebitPhaseInstanceId,a.ActionId,0,a.ActorSeat,false,false,[])).OrderBy(s=>s.ActionId).TakeLast(2).ToArray());
 }
 private void BindGuoSiAcceptedUse(CardUseFrame use)
 {
  if(!HasGuoSiCapability||use.Action is not{}a||_players[a.ActorSeat].GuoSiUses is not{}list)return;
  _players[a.ActorSeat].GuoSiUses=Array.AsReadOnly(list.Select(s=>s.ActionId==a.ActionId?s with{NativeUseId=use.Id}:s).ToArray());
 }
 private void CaptureGuoSiFinalizedUse(long id,CardActionContext a)
 {
  if(!HasGuoSiCapability||_players[a.ActorSeat].GuoSiUses is not{}list||!list.Any(s=>s.ActionId==a.ActionId))return;
  if(LifecycleCardUse(id)is not{Action:{}actual}use||actual.ActionId!=a.ActionId||!SameOverflowAction(actual,a)||!HasExactAcceptedActualHandGainUse(use,a))throw new InvalidOperationException("Sidao finalized targets require their actual accepted native action.");
  _players[a.ActorSeat].GuoSiUses=Array.AsReadOnly(list.Select(s=>s.ActionId==a.ActionId?s with{NativeUseId=id,Finalized=true,Targets=SuFeiPrimaryTargets(a)}:s).ToArray());
 }
 private void CaptureGuoSiCompletedUse(long id,CardActionContext a)
 {
  if(!HasGuoSiCapability||_players[a.ActorSeat].GuoSiUses is not{}list||!list.Any(s=>s.ActionId==a.ActionId))return;
  var use=LifecycleCardUse(id);var response=GuoSiResponseUse(a);
  if(response&&(_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(f=>f.Id==id)is not{CompletedResponseReturn:{}ret}window||!SameOverflowAction(window.Action,a)||!IsCompletedResponseParentConsistent(window)||!IsCompletedResponseUse(a,ret.Kind)))throw new InvalidOperationException("Sidao response completion requires its exact native use-return parent.");
  if(!response&&(use is not{Step:ResolutionFrameStep.Completed,Action:{}actual}||actual.ActionId!=a.ActionId||!SameOverflowAction(actual,a)||!HasExactAcceptedActualHandGainUse(use,a)||CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e=>e.ResolutionId==use.Id&&e.CardId==use.CardId&&e.CardKind==use.CardKind)!=1))throw new InvalidOperationException("Sidao completion lost its native action.");
  var next=Array.AsReadOnly(list.Select(s=>s.ActionId==a.ActionId?s with{Completed=true,Finalized=response||s.Finalized,Targets=response?[]:SuFeiPrimaryTargets(a)}:s).ToArray());_players[a.ActorSeat].GuoSiUses=next;
  if(response||use is null||next.Count!=2||!GuoSiQualified(a.ActorSeat)||next.Any(s=>!s.Finalized||!s.Completed||s.Turn!=_turnNumber||s.Phase!=_cardUseDebitPhaseInstanceId)||use.GuoSiPair is not null)return;
  var first=next[0];var second=next[1];if(CompleteProgramEventHistory().OfType<GuoSiPairQualifiedEvent>().Any(e=>e.OwnerSeat==a.ActorSeat&&e.FirstActionId==first.ActionId&&e.SecondActionId==second.ActionId))return;
  var common=first.Targets.Intersect(second.Targets).Where(s=>s!=a.ActorSeat&&IsValidPlayerSeat(s)&&_players[s].IsAlive).ToArray();if(common.Length==0)return;
  ReplaceRuntimeFrame(id,use with{GuoSiPair=new(first,second,common)});AdvanceEventRulesAndQueueFact(new GuoSiPairQualifiedEvent(id,a.ActorSeat,first.ActionId,second.ActionId));
 }
 private bool HasGuoSiTargetEffect(int actor,int target,DirectedTurnCardPolicyEffect effect)
 {
  if(!IsValidPlayerSeat(actor)||!IsValidPlayerSeat(target)||_players[actor].GuoSiPacts is not{}pacts)return false;
  return pacts.Any(p=>p.Turn==_turnNumber&&p.TurnOwner==_currentSeat&&p.Actor==actor&&p.Target==target&&CompleteProgramEventHistory().OfType<GuoSiPactPaidEvent>().Count(e=>e.Pact==p)==1&&(p.Branch==0?effect==DirectedTurnCardPolicyEffect.ForbidTarget:effect is DirectedTurnCardPolicyEffect.IgnoreDistance or DirectedTurnCardPolicyEffect.BypassSlashLimit));
 }
 private void ObserveGuoSiTurnExpiry(IGameEvent payload)
 {
  if(payload is not TurnEndedEvent||!HasGuoSiCapability)return;
  foreach(var p in _players){if(p.GuoSiPacts is not null)p.GuoSiPacts=null;if(p.GuoSiUses is not null)p.GuoSiUses=null;}
 }
 private bool CanOfferGuoSi(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)
 {
  if(!ProgramInstructionResolver.Default.Features(t).HasOperation(SkillProgramEffectOp.OfferSidaoCompletedPairSnatch))return true;
  return _phase==TurnPhase.Play&&c.OwnerSeat==_currentSeat&&w.CardUse is{}i&&LifecycleCardUse(i.ParentCardUseFrameId)is{GuoSiPair:{}q,Action:{}a}&&a.ActionId==i.CardActionId&&GuoSiQualified(c.OwnerSeat)&&SidaoActions(c.OwnerSeat,q).Count>0;
 }
}
