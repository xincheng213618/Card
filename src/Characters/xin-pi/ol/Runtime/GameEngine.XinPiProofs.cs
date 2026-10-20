namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool HasXinPiCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.InvalidateRemainingActualDamageCardEffects)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ClaimCompletedUndamagedIncomingCardEntities)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.PreventYinjuDamageAndRecover)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawBothAfterYinjuActualTargetUse)||_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ArmYinjuActualTurnRelation);
 private static CardConversionSource XinPiSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 540)]
 private void ObserveXinPiEvent(IGameEvent payload)
 {
  if(!HasXinPiCapability)return;
  if(payload is CardUseDeclaredEvent declared&&LifecycleCardUse(declared.ResolutionId)is{Action:{Type:CardActionType.Use}a}u)
  {if(u.XinPiOrigin is not null)throw new InvalidOperationException("XinPi use origin cannot issue twice.");UpdateLifecycleCardUse(u.Id,f=>f with{XinPiOrigin=new(u.Id,a,_movementSequence)});}
  if(payload is CardActionAcceptedEvent{Action:{Type:CardActionType.Use}accepted}&&_resolutionStack.OfType<CardUseFrame>().LastOrDefault(f=>f.Action?.ActionId==accepted.ActionId)is{XinPiOrigin:{}origin}native)
  {
   var invoices=origin.Materials.ToList();foreach(var cost in accepted.PhysicalCards.Where(c=>c.From==CardLocation.Processing))if(!invoices.Any(m=>m.CardId==cost.CardId)&&XinPiExactTunanPayment(native,cost.CardId)is{}payment)invoices.Add(new(cost.CardId,payment,null));if(invoices.Count!=origin.Materials.Count)UpdateLifecycleCardUse(native.Id,f=>f with{XinPiOrigin=origin with{Materials=invoices}});
  }
  if(payload is TurnEndedEvent){foreach(var p in _players){p.YinjuRelation=null;p.ChijieUsedActualTurn=null;}}
 }
 private bool YinjuRelationValid(YinjuTurnRelation r)=>r.ActualTurn==_turnNumber&&IsValidPlayerSeat(r.Source.OwnerSeat)&&IsValidPlayerSeat(r.TargetSeat)&&r.Source.OwnerSeat!=r.TargetSeat&&_contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash==r.GameplayHash&&CompleteProgramEventHistory().OfType<YinjuRelationArmedEvent>().Count(e=>e.Relation==r)==1&&CompleteProgramEventHistory().OfType<ProgramSkillStartedEvent>().Count(e=>e.FrameId==r.ActivationFrameId&&e.OwnerSeat==r.Source.OwnerSeat&&e.SkillId==r.Source.SkillId&&e.ActivationId==r.Source.BindingId)==1&&_contentRegistry.GetSkill(r.Source.SkillId).Program!.Activations.Any(a=>a.Id==r.Source.BindingId&&a.Effects is[{Op:SkillProgramEffectOp.ArmYinjuActualTurnRelation}]);
 private YinjuTurnRelation? CurrentYinju(int owner)=>IsValidPlayerSeat(owner)&&_players[owner].IsAlive&&_players[owner].YinjuRelation is{}r&&_players[r.TargetSeat].IsAlive&&YinjuRelationValid(r)?r:null;
 private static IReadOnlyList<int> XinPiMainTargets(CardUseFrame u)=>u.CardKind==CardKind.BorrowedSword?Array.AsReadOnly(u.Action!.TargetSeats.Where((_,i)=>i%2==0).ToArray()):u.TargetSeats;
 private CardUseFrame? ExactXinPiNativeWindow(ProgramSkillWindowContext w,bool completed)
 {
  if(w.CardUse is not{}identity||_resolutionStack.SingleOrDefault(f=>f.Id==w.ParentFrameId)is not ProgramCardTriggerWindowFrame window||window.CompletedResponseReturn is not null||window.ParentFrameId!=identity.ParentCardUseFrameId||GetCardActionWindow(window)!=(completed?SkillProgramTriggerWindow.CardUseCompleted:SkillProgramTriggerWindow.CardUseTargetsFinalized)||LifecycleCardUse(window.ParentFrameId)is not{Action:{Type:CardActionType.Use}a}u||a.ActionId!=identity.CardActionId||!SameOverflowAction(a,window.Action)||!HasExactAcceptedActualHandGainUse(u,a))return null;
  var index=_resolutionStack.FindIndex(f=>f.Id==window.Id);if(index<1||_resolutionStack[index-1].Id!=u.Id)return null;
  if(completed&&(u.Step!=ResolutionFrameStep.Completed||CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e=>e.ResolutionId==u.Id&&e.CardId==u.CardId&&e.CardKind==u.CardKind)!=1))return null;return u;
 }
 private CardUseFrame? ChijieDamageUse(int owner,ProgramSkillWindowContext w,out CompletedUndamagedUseDamageRecordedEvent damage)
 {
  damage=null!;if(w.Window!=SkillProgramTriggerWindow.AfterDamageApplied||_resolutionStack.SingleOrDefault(f=>f.Id==w.ParentFrameId)is not DamageTriggerWindowFrame window||window.TargetSeat!=owner||window.SourceSeat==owner||w.TargetSeat!=owner||w.SourceSeat!=window.SourceSeat||window.TriggerWindow!=w.Window)return null;
  var facts=CompleteProgramEventHistory().OfType<CompletedUndamagedUseDamageRecordedEvent>().Where(e=>e.DamageFrameId==window.ParentFrameId&&e.TargetSeat==owner&&e.SourceSeat==window.SourceSeat&&e.Amount>0&&!e.SourceLess).ToArray();if(facts is not[var fact]||LifecycleCardUse(fact.CardUseFrameId)is not{Action:{Type:CardActionType.Use}a}use||fact.CardActionId!=a.ActionId||fact.EffectiveKind!=use.CardKind||!HasExactAcceptedActualHandGainUse(use,a))return null;damage=fact;return use;
 }
 [ProgramCardUsePayment(ProgramCardUsePaymentStage.CaptureMovement, 40)]
 private void CaptureXinPiUseMovementBatch(CardMovementBatchContext batch)
 {
  if(!HasXinPiCapability||batch.ParentBatchId is not null||_resolutionStack.LastOrDefault()is not CardUseFrame{XinPiOrigin:{}origin,Action:{}action}use||batch.ParentFrameId!=use.Id||origin.UseId!=use.Id||origin.Action.ActionId!=action.ActionId)return;
  var materials=origin.Materials.ToList();foreach(var m in batch.Movements)
  {
   if(!action.PhysicalCards.Any(c=>c.CardId==m.CardId&&c.CardKind==m.CardKind)||m.Sequence<=origin.Before||!_cardMovements.Contains(m))continue;
   var cost=action.PhysicalCards.Single(c=>c.CardId==m.CardId);var existing=materials.FindIndex(c=>c.CardId==m.CardId);
   if(m.From==cost.From&&m.To==CardLocation.Processing&&existing<0)materials.Add(new(m.CardId,m,batch.Id));
   else if(existing<0&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&XinPiExactTunanPayment(use,m.CardId)is{}tunan)materials.Add(new(m.CardId,tunan,null,m,batch.Id));
   else if(existing>=0&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&materials[existing].Finished is null&&m.Sequence>materials[existing].Payment.Sequence)materials[existing]=materials[existing]with{Finished=m,FinishedBatchId=batch.Id};
  }
  if(materials.Count!=origin.Materials.Count||!materials.SequenceEqual(origin.Materials))UpdateLifecycleCardUse(use.Id,f=>f with{XinPiOrigin=origin with{Materials=materials}});
 }
 private CardMovementRecord? XinPiExactTunanPayment(CardUseFrame use,int id)
 {
  if(use.XinPiOrigin is not{}origin||use.Action is not{}a||use.CardAttack is null||!HasExactCompletedUndamagedTunanVirtualAttack(use,a,new CardAttackHandle(this,use.Id)))return null;
  var index=_resolutionStack.FindIndex(f=>f.Id==use.Id);if(index<1||_resolutionStack[index-1]is not ProgramSkillFrame parent)return null;
  return _cardMovements.LastOrDefault(m=>m.CardId==id&&m.Sequence<=origin.Before&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Processing&&m.Reason.Value==$"skill-program.{parent.SkillId}.{GetProgramBindingId(parent)}.reveal");
 }
 private bool XinPiMaterialStillOriginal(CardUseFrame use,XinPiMaterialInvoice invoice)
 {
  if(use.XinPiOrigin is not{}origin||use.Action is not{}a||origin.UseId!=use.Id||origin.Action.ActionId!=a.ActionId||invoice.PaymentBatchId is not>0&&XinPiExactTunanPayment(use,invoice.CardId)!=invoice.Payment||!a.PhysicalCards.Any(c=>c.CardId==invoice.CardId&&c.CardKind==invoice.Payment.CardKind&&(c.From==invoice.Payment.From||c.From==CardLocation.Processing&&XinPiExactTunanPayment(use,invoice.CardId)==invoice.Payment))||invoice.Payment.CardId!=invoice.CardId||invoice.Payment.To!=CardLocation.Processing||invoice.Payment.Sequence<=origin.Before&&XinPiExactTunanPayment(use,invoice.CardId)!=invoice.Payment||!_cardMovements.Contains(invoice.Payment))return false;
  var tail=_cardMovements.Where(m=>m.CardId==invoice.CardId&&m.Sequence>invoice.Payment.Sequence).ToArray();
  return invoice.Finished is{}finished?invoice.FinishedBatchId is>0&&finished.CardId==invoice.CardId&&finished.From==CardLocation.Processing&&finished.To==CardLocation.DiscardPile&&tail is[var actual]&&actual==finished&&_cardZones.GetLocation(invoice.CardId)==CardLocation.DiscardPile:tail.Length==0&&_cardZones.GetLocation(invoice.CardId)==CardLocation.Processing;
 }
 private IReadOnlyList<int> ChijieClaimable(CardUseFrame use)
 {
  if(use.XinPiOrigin is not{}o||use.Action is not{}a||o.UseId!=use.Id||o.Action.ActionId!=a.ActionId||!o.Action.PhysicalCards.SequenceEqual(a.PhysicalCards)||!HasExactAcceptedCompletedUndamagedUse(use,a))return [];
  return Array.AsReadOnly(o.Materials.Where(m=>!IsClaimedUseCardEntity(use.Id,m.CardId)&&XinPiMaterialStillOriginal(use,m)).Select(m=>m.CardId).Distinct().ToArray());
 }
 private bool ChijieUndamagedIncoming(int owner,ProgramSkillWindowContext w,out CardUseFrame use)
 {
  var actual=ExactXinPiNativeWindow(w,true);use=actual!;return actual is{Action:{}a}&&a.ActorSeat!=owner&&XinPiMainTargets(actual).Contains(owner)&&!actual.CausedDamage&&!CompleteProgramEventHistory().OfType<CompletedUndamagedUseDamageRecordedEvent>().Any(e=>e.CardUseFrameId==actual.Id&&e.Amount>0)&&ChijieClaimable(actual).Count>0;
 }
 private bool IsXinPiUseIneffective(CardUseFrame use)
 {
  if(use.ChijieInvalidation is not{}p)return false;
  if(p.UseId!=use.Id||use.Action is not{}a||p.ActionId!=a.ActionId||CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e=>e.FrameId==p.ProgramFrameId&&e.SkillId==p.Source.SkillId&&e.BindingId==p.Source.BindingId&&e.SkillInstanceId==p.Source.SkillInstanceId&&e.OwnerSeat==p.Source.OwnerSeat&&e.Window==SkillProgramTriggerWindow.AfterDamageApplied)!=1||_contentRegistry.GetSkill(p.Source.SkillId).Program?.GameplayHash!=p.GameplayHash||CompleteProgramEventHistory().OfType<ChijieUseInvalidatedEvent>().Count(e=>e.Proof==p)!=1||CompleteProgramEventHistory().OfType<ChijieUsedEvent>().Count(e=>e.FrameId==p.ProgramFrameId&&e.Source==p.Source&&e.Branch=="invalidate"&&e.UseId==use.Id&&e.ActionId==a.ActionId&&e.ActualTurn==p.ActualTurn)!=1||!CompleteProgramEventHistory().OfType<CompletedUndamagedUseDamageRecordedEvent>().Any(e=>e.CardUseFrameId==use.Id&&e.CardActionId==a.ActionId&&e.DamageFrameId==p.DamageFrameId&&e.SourceSeat==p.SourceSeat&&e.TargetSeat==p.TargetSeat&&e.Amount>0&&!e.SourceLess))throw new InvalidOperationException("Chijie invalidation lost its exact already-applied native card damage proof.");return true;
 }
 private bool IsXinPiRemainingCardDamageIneffective(IDamageAttempt attack)=>attack is CardAttackHandle c&&!c.IsProgramSkillDamage&&!c.IsProgramJudgmentDamage&&!c.IsDelayedJudgmentDamage&&LifecycleCardUse(c.ResolutionId)is{}use&&use.CardKind==c.EffectiveCardKind&&CompletedUndamagedNativeUseActorMatches(use,c)&&IsXinPiUseIneffective(use);
 private bool IsXinPiPhysicalClaim(long useId,int cardId)
 {
  if(LifecycleCardUse(useId)is not{XinPiClaim:{}p,Action:{}a,XinPiOrigin:{}origin}||p.UseId!=useId||p.ActionId!=a.ActionId||!p.Entities.Contains(cardId)||p.After is not{}after||p.BatchId is not{}batch||after<=p.Before||batch<=0||_contentRegistry.GetSkill(p.Source.SkillId).Program?.GameplayHash!=p.GameplayHash||!origin.Materials.Any(m=>m.CardId==cardId)||CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e=>e.FrameId==p.FrameId&&e.OwnerSeat==p.Source.OwnerSeat&&e.SkillId==p.Source.SkillId&&e.BindingId==p.Source.BindingId&&e.SkillInstanceId==p.Source.SkillInstanceId&&e.Window==SkillProgramTriggerWindow.CardUseCompleted)!=1||CompleteProgramEventHistory().OfType<ChijieClaimIssuedEvent>().Count(e=>e.FrameId==p.FrameId&&e.UseId==useId&&e.ActionId==a.ActionId&&e.Source==p.Source&&e.GameplayHash==p.GameplayHash&&e.Before==p.Before&&e.Count==p.Entities.Count)!=1||CompleteProgramEventHistory().OfType<ChijieClaimPaidEvent>().Count(e=>e==new ChijieClaimPaidEvent(p.FrameId,p.Source,useId,a.ActionId,p.Before,after,batch,p.Entities.Count))!=1)return false;
  return p.Entities.All(id=>origin.Materials.Any(m=>m.CardId==id)&&_cardMovements.Count(m=>m.CardId==id&&m.Sequence>p.Before&&m.Sequence<=after&&m.From.Zone is CardZoneKind.Processing or CardZoneKind.DiscardPile&&m.To==CardLocation.Hand(p.Source.OwnerSeat)&&m.Reason.Value==ChijieClaimReason)==1);
 }
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 550)]
 private bool CanOfferXinPi(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)
 {
  var features=ProgramInstructionResolver.Default.Features(t);
  if(features.HasOperation(SkillProgramEffectOp.InvalidateRemainingActualDamageCardEffects))return _players[c.OwnerSeat].ChijieUsedActualTurn!=_turnNumber&&ChijieDamageUse(c.OwnerSeat,w,out _)is{}use&&!IsXinPiUseIneffective(use);
  if(features.HasOperation(SkillProgramEffectOp.ClaimCompletedUndamagedIncomingCardEntities))return _players[c.OwnerSeat].ChijieUsedActualTurn!=_turnNumber&&ChijieUndamagedIncoming(c.OwnerSeat,w,out _);
  if(features.HasOperation(SkillProgramEffectOp.PreventYinjuDamageAndRecover))return ExactYinjuBeforeCandidate(c,w,out _,out _);
  if(features.HasOperation(SkillProgramEffectOp.DrawBothAfterYinjuActualTargetUse))return ExactYinjuUseCandidate(c,w,out _,out _);
  return true;
 }
}
