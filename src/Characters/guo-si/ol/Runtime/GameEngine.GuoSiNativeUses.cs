namespace CardGame.Core;
public sealed partial class GameEngine
{
 private void IssueSidao(ProgramSkillFrame f,LegalAction action)
 {
  var r=f.GuoSi!;var actor=_players[f.OwnerSeat];var card=GetHand(actor).Single(c=>c.Id==action.CardId);var suit=GetProgramEffectiveSuit(actor,card);
  var option=BuildProgramOrdinaryTrickUseOptions(actor,CardKind.Snatch,suit,enforceUsePermission:true,beneficiaryShieldSuit:suit,actualEffectiveColor:SuitColor(suit),hasActualColor:true,physicalCardIds:[card.Id],includeNextActualUseAdjustment:false).Single(o=>o.ActionKind==action.Kind&&o.TargetCardId==action.TargetCardId&&o.TargetSeats.SequenceEqual(action.TargetSeats));
  var id=BeginCardUse(card,f.OwnerSeat,action.TargetSeats,CardKind.Snatch,physicalCardIds:[card.Id],conversionSource:r.Source);var use=LifecycleCardUse(id)!;
  var native=new GuoSiNativeReceipt(f.Id,r.Source,r.GameplayHash,r.Turn,r.Phase,r.Instruction,id,use.Action!.ActionId,use.Action.ParentActionId??throw new InvalidOperationException("Sidao lost its original action parent."),card.Id,action.TargetSeats.Single(),action.Kind,action.TargetCardId,option.RequiredCardKind);
  var parent=_resolutionStack.OfType<ProgramSkillFrame>().Single(p=>p.Id==f.Id);
  if(parent.GuoSi!=r||parent.GameplayHash!=f.GameplayHash||parent.SkillInstanceId!=f.SkillInstanceId||_resolutionStack.FindIndex(p=>p.Id==parent.Id)+1!=_resolutionStack.FindIndex(p=>p.Id==id))throw new InvalidOperationException("Sidao native child lost its direct original parent.");
  ReplaceRuntimeFrame(f.Id,parent with{GuoSi=r with{Stage=GuoSiStage.NativeUse,Native=native}});UpdateLifecycleCardUse(id,u=>u with{GuoSiNative=native,GuoSiCostBefore=_movementSequence,GuoSiCostAfter=_movementSequence,GuoSiCostDrained=false});
  MoveCard(card,CardLocation.Hand(f.OwnerSeat),CardLocation.Processing,CardMoveReasons.Use);UpdateLifecycleCardUse(id,u=>u with{GuoSiCostAfter=_movementSequence});AdvanceEventRulesAndQueueFact(new GuoSiNativeIssuedEvent(native));ContinueGuoSiNativePayment(id);
 }
 private bool IsGuoSiNativeUse(CardUseFrame use)=>use.GuoSiNative is not null;
 private bool GuoSiNativeRidesOn(ProgramSkillFrame f,CardUseFrame use)=>f.GuoSi is{Stage:GuoSiStage.NativeUse,Native:{}r}&&r.ProgramFrameId==f.Id&&use.GuoSiNative==r&&r.Source==GuoSiSource(f)&&r.GameplayHash==f.GameplayHash;
 [NativeUseValidation(60)]
 private void ValidateGuoSiNativeUseReturn(CardUseFrame use)
 {
     if (IsGuoSiNativeUse(use)) AssertGuoSiNativeUse(use);
 }
 private void AssertGuoSiNativeUse(CardUseFrame use)
 {
  if(use.GuoSiNative is not{}r||_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==r.ProgramFrameId)is not{}parent||!GuoSiNativeRidesOn(parent,use)||use.Id!=r.NativeUseId||use.Action is not{Type:CardActionType.Use}a||a.ActorSeat!=r.Source.OwnerSeat||a.ProviderSeat!=a.ActorSeat||a.ActionId!=r.ActionId||a.ParentActionId!=r.ParentActionId||parent.WindowContext?.CardUse?.CardActionId!=r.ParentActionId||a.EffectiveKind!=CardKind.Snatch||use.CardKind!=CardKind.Snatch||use.CardId!=r.Material||!a.PhysicalCards.Select(c=>c.CardId).SequenceEqual([r.Material])||a.PhysicalCards.Single().From!=CardLocation.Hand(a.ActorSeat)||!a.ConversionChain.SequenceEqual([r.Source])||!use.TargetSeats.SequenceEqual([r.Target])||use.GuoSiCostBefore is not{}before||use.GuoSiCostAfter is not{}after||before>after||_cardMovements.Count(m=>m.Sequence>before&&m.Sequence<=after&&m.CardId==r.Material&&m.From==CardLocation.Hand(a.ActorSeat)&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.Use)!=1||CompleteProgramEventHistory().OfType<GuoSiNativeIssuedEvent>().Count(e=>e.Receipt==r)!=1)throw new InvalidOperationException("Sidao native use lost its exact pair, single hand material, source, accepted action or payment invoice.");
 }
 [NativeFrameRoute(typeof(CardUseFrame), 140, typeof(NativeFrameRouteConditions), nameof(NativeFrameRouteConditions.GuoSiPayment))]
 private void ContinueGuoSiNativePayment(long id)
 {
  var use=LifecycleCardUse(id)!;AssertGuoSiNativeUse(use);if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.GuoSiCardUse)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.GuoSiCardUse)||TryBeginCardsMovedProgramWindow(id))return;
  UpdateLifecycleCardUse(id,u=>u with{GuoSiCostDrained=true});var r=use.GuoSiNative!;var card=GetAttackCard(r.Material);
  if(_winner!=Winner.None||!_players[use.SourceSeat].IsAlive){FinishCardUse(id,card,CardKind.Snatch);return;}
  BeginJizhiOrNullificationWindow(id,card,use.SourceSeat,use.TargetSeats,r.ActionKind,r.TargetCardId,r.RequiredKind,CardKind.Snatch);
 }
 [NativeUseReturn(40)]
 private bool CompleteGuoSiUseReturn(CardUseFrame use)
 {
     if (!IsGuoSiNativeUse(use)) return false;
     ReturnGuoSiNativeUse(use);
     return true;
 }
 private void ReturnGuoSiNativeUse(CardUseFrame use)
 {
  if(use.GuoSiNative is not{}r)return;var f=GetActiveProgramFrame(r.ProgramFrameId);
  if(_resolutionStack.LastOrDefault()?.Id!=f.Id||!GuoSiNativeRidesOn(f,use)||LifecycleCardUse(use.Id)is not null||CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e=>e.ResolutionId==use.Id)!=1||CompleteProgramEventHistory().OfType<GuoSiNativeReturnedEvent>().Any(e=>e.Receipt==r))throw new InvalidOperationException("Sidao must return exactly once after its complete native Snatch.");
  AdvanceEventRulesAndQueueFact(new GuoSiNativeReturnedEvent(r));ReplaceRuntimeTop(f with{GuoSi=f.GuoSi!with{Stage=GuoSiStage.Completed}});
 }
 private bool GuoSiFirstChild(ProgramSkillFrame root,ResolutionFrame child)
 {
  if(root.GuoSi is not{}r)return false;
  if(child is CardUseFrame use)return GuoSiNativeRidesOn(root,use)&&CanContinueIssuedGuoSi(root);
  if(r.Stage!=GuoSiStage.MovementChildren||child is not CardsMovedTriggerWindowFrame moved||!CanContinueIssuedGuoSi(root))return false;
  return moved.Batch.ParentFrameId==root.Id&&moved.Batch.AwaitingProgramFrameId==root.Id&&moved.Batch.OriginOwnerSeat==root.OwnerSeat&&moved.Batch.OriginSkillId==root.SkillId&&moved.Batch.OriginSkillInstanceId==root.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardId==r.SelectedId&&m.From==r.SelectedFrom&&m.To==CardLocation.Hand(root.OwnerSeat)&&m.Reason.Value==TanbeiGainReason);
 }
 private bool GuoSiObserverSuffix(int index)=>HasObserverSuffix(index,static(e,i)=>e.IsSameNameHandObserverEdge(i)||e.GuoSiNativePaymentEdge(i),static(e,i,d)=>e.IsSuspendedOrPaidRescueObserverSuffix(i,d)||e.TieredRoundZeroDyingRescueRide(i,d)||(e.DrawFundedDistinctBasicDyingRescueRide(i,d) || e.HuanpeiDyingRescueRide(i,d)));
 private bool GuoSiNativePaymentEdge(int index)
 {
  if(index<1||_resolutionStack[index-1]is not CardUseFrame{GuoSiNative:{}r,GuoSiCostDrained:false}use)return false;
  AssertGuoSiNativeUse(use);var child=_resolutionStack[index];
  if(child is CardsMovedTriggerWindowFrame moved)return moved.ResumeGuoSiUseFrameId==use.Id&&moved.Batch.ParentFrameId==use.Id&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>use.GuoSiCostBefore&&m.Sequence<=use.GuoSiCostAfter&&m.CardId==r.Material&&m.From==CardLocation.Hand(r.Source.OwnerSeat)&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.Use);
  if(child is HpChangedTriggerWindowFrame hp)return hp.ResumeFrameId==use.Id&&hp.Change.ParentFrameId==use.Id&&hp.Continuation==PostEventContinuation.GuoSiCardUse;
  return child is RecoveryReplacementFrame recovery&&recovery.ParentFrameId==use.Id&&recovery.Return.ResumeFrameId==use.Id&&recovery.Return.Continuation==PostEventContinuation.GuoSiCardUse;
 }
 private ProgramSkillFrame? GuoSiObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.GuoSiFirstChild(r,c),static(e,i)=>e.GuoSiObserverSuffix(i));
 [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 210)]
 [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 320)]
 private bool HasGuoSiDamageObserver(long id)=>GuoSiObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 410)]
 private bool HasGuoSiBeforeDamageObserver(long id)=>GuoSiObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 180)]
 private bool IsGuoSiDying()=>ActiveDying is{}d&&GuoSiObserverRoot()is{}r&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 320)]
 private bool TryAdvanceGuoSiSubtree()=>_pendingDecision is null&&GuoSiObserverRoot()is not null&&TryAdvanceObserverSubtree(advanceRecoveryReplacement:true);
 [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 550)]
 private bool AllowsGuoSiNestedDamage(ProgramSkillFrame observer,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
 {
  if(sourceLess||observer.AttackAttempt is not null||observer.InstructionIndex<1||_resolutionStack.LastOrDefault()?.Id!=observer.Id||observer.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged)||GuoSiObserverRoot()is not{}r||r.Id==observer.Id)return false;
  var e=GetPausedPrivateOfferEffect(observer);return e is{Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is{}reference?ResolveProgramParticipant(observer,reference):ResolveProgramEffectTarget(observer,e.Target));
 }
}
