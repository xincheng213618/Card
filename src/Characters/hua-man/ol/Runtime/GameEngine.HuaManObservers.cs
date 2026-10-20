namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedAfterCategory, 20)]
    private bool HuaManFinalizedAfterCategoryTargetWindow(ProgramTargetCardWindowQuery query) =>
        HasHuaManCapability;


 private bool HasHuaManCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.PaySouyingDirectionalSoleTarget);
 private static CardConversionSource HuaManSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_hua_man_ol_HuaManObservers.HuaManSource(f);
 private bool HuaManCurrent(ProgramSkillFrame f)=>_players[f.OwnerSeat].IsAlive&&HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)&&_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash==f.GameplayHash;
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 550)]
 private void ObserveAcceptedHuaManUse(CardActionAcceptedEvent accepted) => ObserveHuaManAcceptedUse(accepted.Action);

 private void ObserveHuaManAcceptedUse(CardActionContext action)
 {
  if(!HasActualSouyingObserver()||!HasOngoingActualUseTurn||action.Type!=CardActionType.Use&&!GuoSiResponseUse(action)||!IsValidPlayerSeat(action.ActorSeat))return;
  var owner=_players[action.ActorSeat];var list=(owner.HuaManUses??[]).Where(s=>s.ActualTurn==_turnNumber).ToArray();if(list.Any(s=>s.ActionId==action.ActionId))return;
  owner.HuaManUses=[..list,new(_turnNumber,action.ActionId,0,action.ActorSeat,action.ProviderSeat,action.EffectiveKind){Targets=action.EffectiveDesignatedTargetSeats.Where(IsValidPlayerSeat).Distinct().ToArray()}];
 }
 private void CaptureHuaManFinalizedUse(long id,CardActionContext action)
 {
  if(!HasActualSouyingObserver()||_players[action.ActorSeat].HuaManUses is not{}list||!list.Any(s=>s.ActionId==action.ActionId))return;
  if(LifecycleCardUse(id)is not{Action:{}actual}use||!SameOverflowAction(actual,action)||!HasExactAcceptedActualHandGainUse(use,actual))throw new InvalidOperationException("Souying requires an exact accepted native target designation.");
  var primary=action.EffectiveDesignatedTargetSeats.Distinct().ToArray();var updated=list.Select(s=>s.ActionId==action.ActionId?s with{NativeUseId=id,Finalized=true,Targets=primary}:s).ToArray();_players[action.ActorSeat].HuaManUses=updated;
  if(primary is not[var target]||target==action.ActorSeat||!IsValidPlayerSeat(target))return;
  var ordinal=updated.Count(s=>s.ActualTurn==_turnNumber&&s.ActionId<=action.ActionId&&s.Targets.Contains(target));if(ordinal<2)return;
  ReplaceRuntimeFrame(id,use with{HuaManSouyingQualification=new(_turnNumber,id,action.ActionId,action.ActorSeat,action.ProviderSeat,target,ordinal)});
 }
 private void CaptureHuaManCompletedUse(long id,CardActionContext action)
 {
  if(!HasHuaManCapability||_players[action.ActorSeat].HuaManUses is not{}list)return;var use=LifecycleCardUse(id);if(use?.Action is not{}actual||!SameOverflowAction(actual,action))return;
  _players[action.ActorSeat].HuaManUses=list.Select(s=>s.ActionId==action.ActionId?s with{Targets=action.EffectiveDesignatedTargetSeats.Distinct().ToArray()}:s).ToArray();
 }
 private ProgramSkillWindowContext CreateHuaManCardUseContext(ProgramCardTriggerWindowFrame frame,ProgramTriggerCandidate c,ProgramSkillWindowContext context)=>context with{HuaManSouying=LifecycleCardUse(frame.ParentFrameId)?.HuaManSouyingQualification};
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 560)]
 private bool CanOfferHuaMan(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)
 {
  if(!t.Effects.Any(e=>HuaManComposition.Own(e.Op)))return true;
  return t.Effects[0].Op switch{
   SkillProgramEffectOp.DrawAndCountMansiActualBarbarianDamage=>w.DamageFrameId is{}damage&&_resolutionStack.OfType<DamageFrame>().SingleOrDefault(d=>d.Id==damage)is{}d&&d.Amount>0&&_resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(f=>f.ParentFrameId==damage)is{}window&&GetDamageTriggerAttack(window).EffectiveCardKind==CardKind.BarbarianAssault,
   SkillProgramEffectOp.AwakenZhanyuanAndOfferXili=>c.OwnerSeat==_currentSeat&&(_players[c.OwnerSeat].HuaManProgress?.MansiCards??0)>7&&_players[c.OwnerSeat].HuaManProgress?.Awakened!=true&&CurrentActualDiscardRecoveryPhase()is{Kind:ActualDiscardRecoveryPhaseKind.Preparation}phase&&phase.ActorSeat==c.OwnerSeat,
   SkillProgramEffectOp.PaySouyingDirectionalSoleTarget=>w.HuaManSouying is{}q&&q.ActualTurn==_turnNumber&&q.DirectionOrdinal>1&&(q.ActorSeat==c.OwnerSeat||q.TargetSeat==c.OwnerSeat)&&LifecycleCardUse(q.NativeUseId)is{Action:{}a}use&&a.EffectiveDesignatedTargetSeats.SequenceEqual([q.TargetSeat])&&(q.TargetSeat==c.OwnerSeat||a.PhysicalCards.Any(p=>_cardZones.GetLocation(p.CardId)==CardLocation.Processing&&!IsClaimedUseCardEntity(use.Id,p.CardId)))&&HasHuaManDiscard(c.OwnerSeat),
   SkillProgramEffectOp.PayXiliActualOtherOwnerDamage=>w.SourceSeat is{}source&&source!=c.OwnerSeat&&source==_currentSeat&&w.Amount>0&&w.TargetSeat is{}target&&HasRuntimeXili(source)&&!HasRuntimeXili(target)&&CurrentDamageAttempt is{IsSourceLess:false,IsDelayedJudgmentDamage:false}attack&&attack.SourceSeat==source&&attack.TargetSeat==target&&HasHuaManDiscard(c.OwnerSeat),_=>false};
 }
 private IEnumerable<Card> HuaManDiscardCards(int seat)=>GetHand(_players[seat]).Concat(GetEquipment(_players[seat])).Concat(UsesFormalWoodenOx&&GetEquipment(_players[seat]).Any(c=>c.Kind==CardKind.WoodenOx)?GetWoodenOxGrain(_players[seat]):[]).Where(c=>!c.IsGeneralWeapon&&!IsSelfHandCategoryDiscardForbidden(seat,c,_cardZones.GetLocation(c.Id),OwnedCardMoveIntent.Discard));
 private bool HuaManDiscardLocation(int seat,CardLocation at)=>at.OwnerSeat==seat&&(at.Zone is CardZoneKind.Hand or CardZoneKind.Equipment||at==CardLocation.WoodenOxGrain(seat)&&UsesFormalWoodenOx&&GetEquipment(_players[seat]).Any(c=>c.Kind==CardKind.WoodenOx));
 private bool HasHuaManDiscard(int seat)=>HuaManDiscardCards(seat).Any();
 private bool IsHuaManClaimedEntity(long useId,int cardId)=>LifecycleCardUse(useId)is{HuaManSouyingMutation:{Cancelled:false}r}use&&r.Qualification.NativeUseId==useId&&r.ClaimedEntities.Contains(cardId)&&ValidSouyingMutation(use,r);

 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 380)]
 private void AssertHuaManFrame(ProgramSkillFrame f)
 {
  if(f.HuaMan is not{}r)return;
  if(r.Source!=HuaManSource(f)||r.GameplayHash!=f.GameplayHash||r.Instruction!=f.InstructionIndex||r.WindowId!=f.WindowContext?.ParentFrameId||r.ActualTurn!=_turnNumber||r.Before>r.After||r.After>_movementSequence||CompleteProgramEventHistory().OfType<HuaManStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.WindowId==r.WindowId&&e.ActualTurn==r.ActualTurn&&e.Operation==GetPausedPrivateOfferEffect(f)?.Op)!=1)throw new InvalidOperationException("Hua Man lost the exact original source, instruction or native window.");
  if(r.Stage is HuaManStage.DiscardChildren or HuaManStage.MansiDraw or HuaManStage.ClaimChildren or HuaManStage.XiliPayerDraw or HuaManStage.XiliSourceDraw)
  {
   var subject=r.Stage==HuaManStage.XiliSourceDraw?r.OtherSeat!.Value:f.OwnerSeat;
   if(!r.Paid||f.PendingMovementContinuation is not{BeforeCount:0,CoverageResultBind:null}||CompleteProgramEventHistory().OfType<HuaManMovementPaidEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Stage==r.Stage&&e.Before==r.Before&&e.After==r.After&&e.ActualCount==r.ActualCount&&e.SubjectSeat==subject)!=1)throw new InvalidOperationException("Hua Man cannot borrow another native movement's payment.");
   var reason=r.Stage==HuaManStage.DiscardChildren?HuaManDiscardReason:r.Stage==HuaManStage.ClaimChildren?HuaManClaimReason:HuaManDrawReason;var moves=_cardMovements.Where(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.Reason.Value==reason).ToArray();
   if(moves.Length!=r.ActualCount||moves.Select(m=>m.CardId).Distinct().Count()!=moves.Length||moves.Any(m=>r.Stage==HuaManStage.DiscardChildren?m.CardId!=r.DiscardedId||m.From.OwnerSeat!=f.OwnerSeat||m.From.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain)||m.To!=CardLocation.DiscardPile:r.Stage==HuaManStage.ClaimChildren?m.From!=CardLocation.Processing||m.To!=CardLocation.Hand(f.OwnerSeat):m.From!=CardLocation.DrawPile||m.To!=CardLocation.Hand(subject)))throw new InvalidOperationException("Hua Man's actual payment differs from its exact entity/zone invoice.");
  }
 }
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 340)]
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 340)]
 private bool CanContinueHuaMan(ProgramSkillFrame f){if(f.HuaMan is not{Paid:true})return false;AssertHuaManFrame(f);return true;}
 [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 340)]
 private bool IsHuaManMovement(ProgramSkillFrame f,SkillProgramEffect?effect,ProgramMovementContinuation pending)=>f.HuaMan is{Paid:true}&&effect is not null&&HuaManComposition.Own(effect.Op)&&pending.SubjectSeat==f.OwnerSeat&&pending.BeforeCount==0&&pending.CoverageResultBind is null&&CanContinueHuaMan(f);
 private bool HuaManFirstChild(ProgramSkillFrame root,ResolutionFrame child)
 {
  if(root.HuaMan is not{}r||!CanContinueHuaMan(root))return false;
  if(child is CardsMovedTriggerWindowFrame moved)return moved.Batch.ParentFrameId==root.Id&&moved.Batch.AwaitingProgramFrameId==root.Id&&moved.Batch.OriginOwnerSeat==root.OwnerSeat&&moved.Batch.OriginSkillId==root.SkillId&&moved.Batch.OriginSkillInstanceId==root.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After);
  if(child is HpChangedTriggerWindowFrame hp){if(r.Stage==HuaManStage.DiscardChildren&&HuaManLionCost(root)&&hp.Change.ParentFrameId==root.Id&&hp.ResumeFrameId==root.Id&&hp.Change.TargetSeat==root.OwnerSeat&&hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.HpBefore==r.NativeOwnerHpBefore&&hp.Change.HpAfter==r.NativeOwnerHpAfter&&hp.Change.Amount==r.NativeOwnerHpAfter-r.NativeOwnerHpBefore&&hp.Change.Amount==1)return true;return r.Stage is HuaManStage.MaximumChildren or HuaManStage.RecoveryChildren&&hp.Change.ParentFrameId==root.Id&&hp.ResumeFrameId==root.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&hp.Change.TargetSeat==root.OwnerSeat&&(r.Stage==HuaManStage.MaximumChildren?hp.Change.Kind==HpChangeKind.MaximumHp:hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.Amount<=r.RecoveryRequested);}
  if(child is RecoveryReplacementFrame lion&&r.Stage==HuaManStage.DiscardChildren&&HuaManLionCost(root)&&lion.ParentFrameId==root.Id&&lion.Return.ResumeFrameId==root.Id&&lion.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&lion.Attempt.Completion.CardId==r.DiscardedId&&lion.Attempt.Completion.MoveReason?.Value==HuaManDiscardReason&&lion.Attempt.SourceSeat==root.OwnerSeat&&lion.Attempt.TargetSeat==root.OwnerSeat&&lion.Attempt.Amount==1)return true;
  return child is RecoveryReplacementFrame recovery&&r.Stage==HuaManStage.RecoveryChildren&&recovery.ParentFrameId==root.Id&&recovery.Return.ResumeFrameId==root.Id&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&recovery.Attempt.TargetSeat==root.OwnerSeat&&recovery.Attempt.SourceSeat==root.OwnerSeat&&recovery.Attempt.Amount==r.RecoveryRequested&&recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.Program&&recovery.Attempt.Completion.InstructionIndex==root.InstructionIndex;
 }
 private ProgramSkillFrame? HuaManObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.HuaManFirstChild(r,c),static(e,i)=>e.HasObserverSuffix(i,static(x,n)=>x.IsSameNameHandObserverEdge(n),static(x,n,d)=>x.IsSuspendedOrPaidRescueObserverSuffix(n,d)||x.TieredRoundZeroDyingRescueRide(n,d)||(x.DrawFundedDistinctBasicDyingRescueRide(n,d) || x.HuanpeiDyingRescueRide(n,d))));
 [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 300)]
 [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 410)]
 private bool HasHuaManDamageObserver(long id)=>HuaManObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 500)]
 private bool HasHuaManBeforeDamageObserver(long id)=>HuaManObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 270)]
 private bool IsHuaManDying()=>ActiveDying is{}d&&HuaManObserverRoot()is{}r&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 410)]
 private bool TryAdvanceHuaManSubtree()=>_pendingDecision is null&&HuaManObserverRoot()is not null&&TryAdvanceObserverSubtree(advanceRecoveryReplacement:true);
 [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 640)]
 private bool AllowsHuaManNestedDamage(ProgramSkillFrame observer,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
 {
  if(sourceLess||observer.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged)||observer.AttackAttempt is not null||observer.InstructionIndex<1||_resolutionStack.LastOrDefault()?.Id!=observer.Id||HuaManObserverRoot()is not{}r||r.Id==observer.Id)return false;var e=GetPausedPrivateOfferEffect(observer);return e is{Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(observer,p):ResolveProgramEffectTarget(observer,e.Target));
 }

 private bool HuaManLionCost(ProgramSkillFrame f)=>f.HuaMan is{Stage:HuaManStage.DiscardChildren,DiscardedId:{}id}r&&GetAdvancedCard(id).Kind==CardKind.SilverLion&&_cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardId==id&&m.From==CardLocation.Equipment(f.OwnerSeat)&&m.To==CardLocation.DiscardPile&&m.Reason.Value==HuaManDiscardReason)==1;
 private bool HasHuaManCardContinuation(long useId)
 {
  if(HasHuaManCardObserver(useId))return true;
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{HuaMan:{Souying:{}q}r}f||q.NativeUseId!=useId||r.Source!=HuaManSource(f)||r.GameplayHash!=f.GameplayHash||r.GameplayHash!=_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash||r.Instruction!=f.InstructionIndex||r.WindowId!=f.WindowContext?.ParentFrameId||r.ActualTurn!=_turnNumber||GetPausedPrivateOfferEffect(f)?.Op!=SkillProgramEffectOp.PaySouyingDirectionalSoleTarget||!ExactSouyingUse(f,out var use)||use.Id!=useId||f.WindowContext?.HuaManSouying!=q)return false;
  if(CompleteProgramEventHistory().OfType<HuaManStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.WindowId==r.WindowId&&e.ActualTurn==r.ActualTurn&&e.Operation==SkillProgramEffectOp.PaySouyingDirectionalSoleTarget)!=1||CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e=>e.FrameId==f.Id&&e.OwnerSeat==f.OwnerSeat&&e.SkillId==f.SkillId&&e.BindingId==GetProgramBindingId(f)&&e.SkillInstanceId==f.SkillInstanceId&&e.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized)!=1)return false;
  if(r.Stage==HuaManStage.DiscardChoice)return !r.Paid&&r.DiscardedId is null&&r.Before==0&&r.After==0&&f.PendingMovementContinuation is null&&f.Step==ResolutionFrameStep.AwaitingResponse&&HuaManCurrent(f)&&_pendingDecision is{Kind:DecisionKind.ProgramTrigger,IsPrivate:true}pending&&pending.PlayerSeat==f.OwnerSeat&&pending.SkillPrompt?.SkillId==f.SkillId&&pending.Choices.Count>0&&AssistedChoicesEqual(pending.Choices,HuaManChoices(f));
  return r.Stage==HuaManStage.DiscardChildren&&r.Paid&&_pendingDecision is null&&CanContinueHuaMan(f);
 }
 private bool HasHuaManCardObserver(long useId)=>HuaManObserverRoot()is{HuaMan:{Souying:{}q}}root&&q.NativeUseId==useId&&root.WindowContext?.HuaManSouying==q&&LifecycleCardUse(useId)is{Action:{}a}use&&a.ActionId==q.ActionId&&a.ActorSeat==q.ActorSeat&&a.ProviderSeat==q.ProviderSeat&&use.HuaManSouyingQualification==q&&_resolutionStack.FindIndex(f=>f.Id==useId)<_resolutionStack.FindIndex(f=>f.Id==root.Id);

}
