namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool HasLvQianCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RecordWeiluActualDamageSource);
 private static CardConversionSource LvQianSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_lv_qian_ol_LvQianObservers.LvQianSource(f);
 private bool LvQianSourceCurrent(CardConversionSource source,string hash)=>IsValidPlayerSeat(source.OwnerSeat)&&_players[source.OwnerSeat].IsAlive&&HasRuntimeSkillInstance(_players[source.OwnerSeat],source.SkillId,source.SkillInstanceId)&&_contentRegistry.GetSkill(source.SkillId).Program?.GameplayHash==hash;
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 520)]
 private void ObserveLvQianTurnBoundary(IGameEvent payload)
 {
  if(!HasLvQianCapability||payload is not(TurnStartedEvent or TurnEndedEvent or PlayerDiedEvent))return;
  if(payload is TurnStartedEvent started)
  {
   var owner=_players[started.ActorSeat];if(owner.WeiluFuture is{}future)owner.WeiluFuture=future.Select(g=>g.DueOwnTurn is null&&g.ArmedTurn<started.TurnNumber?g with{DueOwnTurn=started.TurnNumber}:g).ToArray();
  }
  if(payload is TurnEndedEvent ended)
  {
   var owner=_players[ended.ActorSeat];if(owner.WeiluFuture is{}future)owner.WeiluFuture=future.Where(g=>g.DueOwnTurn!=ended.TurnNumber).ToArray();
   if(owner.WeiluDebts is{}debts)foreach(var debt in debts.Where(g=>g.Phase.ActualTurn==ended.TurnNumber).ToArray())ConsumeUnreturnedWeiluDebt(owner,debt);
  }
  if(payload is PlayerDiedEvent died)
   foreach(var owner in _players)
   {
    if(owner.WeiluFuture is{}future)owner.WeiluFuture=future.Where(g=>owner.Seat!=died.VictimSeat&&g.DamageSourceSeat!=died.VictimSeat).ToArray();
    if(owner.WeiluDebts is{}debts)foreach(var debt in debts.Where(g=>g.TargetSeat==died.VictimSeat).ToArray())ConsumeUnreturnedWeiluDebt(owner,debt);
   }
 }
 private void ObserveLvQianActualPlayEntry(int ownerSeat,int phaseInstance)
 {
  if(!HasLvQianCapability)return;var owner=_players[ownerSeat];if(owner.WeiluFuture is not{}future)return;
  var phase=new WeiluPhaseKey(_turnNumber,ownerSeat,phaseInstance);owner.WeiluFuture=future.Select(g=>g.DueOwnTurn==_turnNumber&&g.FirstPhase is null?g with{FirstPhase=phase}:g).ToArray();
 }
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 530)]
 private bool CanOfferLvQian(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)
 {
  if(!t.Effects.Any(e=>LvQianComposition.Own(e.Op)))return true;
  return t.Effects[0].Op switch{
   SkillProgramEffectOp.RecordWeiluActualDamageSource=>w.SourceSeat is{}source&&source!=c.OwnerSeat&&w.TargetSeat==c.OwnerSeat&&w.Amount>0&&w.DamageFrameId is not null,
   SkillProgramEffectOp.PayWeiluNextActualPlayHpLoss=>w.WeiluPhase is{}p&&p.OwnerSeat==c.OwnerSeat&&(_players[c.OwnerSeat].WeiluFuture??[]).Any(g=>g.FirstPhase==p&&g.Source.SkillId==c.SkillId&&g.Source.SkillInstanceId==c.SkillInstanceId),
   SkillProgramEffectOp.RestoreWeiluExactPaidPlayHp=>HasIssuedWeiluDebtCandidate(c,w),
   SkillProgramEffectOp.ConsumeZengdaoForActualSourceDamage=>IsIssuedZengdaoCandidate(c,w),_=>false};
 }
 private ProgramSkillWindowContext CreateLvQianBeforeDamageContext(BeforeDamageProgramCandidate item,ProgramSkillWindowContext context)=>context with{ZengdaoGrant=MatchingZengdaoGrant(item.Candidate)};
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 350)]
 private void AssertLvQian(ProgramSkillFrame f)
 {
  if(f.LvQian is not{}r)return;
  if(r.GameplayHash!=f.GameplayHash||r.Instruction!=f.InstructionIndex||r.ActualTurn!=_turnNumber||r.Before>r.After||r.After>_movementSequence||r.Source.SkillId!=f.SkillId||r.Source.SkillInstanceId!=f.SkillInstanceId||r.Entities.Distinct().Count()!=r.Entities.Count||r.Cursor<0||r.Cursor>r.Targets.Count||CompleteProgramEventHistory().OfType<LvQianStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.WindowId==r.WindowId&&e.ActualTurn==r.ActualTurn&&(e.Stage==r.Stage||e.Stage==LvQianStage.DamageChoice&&r.Stage==LvQianStage.DamagePaymentChildren))!=1||r.Stage!=LvQianStage.DamageChoice&&f.PendingMovementContinuation is not{BeforeCount:0,CoverageResultBind:null})throw new InvalidOperationException("Lv Qian lost its exact owning instruction, source or paid native cursor.");
  if(r.Stage is LvQianStage.DepositChildren or LvQianStage.DamagePaymentChildren)
  {
   if(r.Grant is not{}grant||r.Stage==LvQianStage.DepositChildren&&grant.Source!=r.Source||grant.GameplayHash!=r.GameplayHash||CompleteProgramEventHistory().OfType<LvQianSegmentPaidEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Stage==r.Stage&&e.Before==r.Before&&e.After==r.After&&e.Count==r.ActualCount)!=1)throw new InvalidOperationException("Zengdao lost its exact once-paid public entity invoice.");
   var reason=r.Stage==LvQianStage.DepositChildren?ZengdaoDepositReason:ZengdaoConsumeReason;var moves=_cardMovements.Where(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.Reason.Value==reason).ToArray();
   if(moves.Length!=r.ActualCount||!moves.Select(m=>m.CardId).Order().SequenceEqual(r.Entities.Order())||moves.Any(m=>r.Stage==LvQianStage.DepositChildren?m.From!=CardLocation.Equipment(f.OwnerSeat)||m.To!=grant.Location:m.From!=grant.Location||m.To!=CardLocation.DiscardPile))throw new InvalidOperationException("Zengdao cannot borrow another public pile or payment.");
  }
  else if(r.Stage==LvQianStage.DamageChoice){if(r.Choices.Count<1||r.Choices.Select(c=>c.CardId).Distinct().Count()!=r.Choices.Count||r.Choices.Any(c=>!IsDurableZengdaoGrant(c.Grant)||!c.Grant.Entities.Contains(c.CardId)))throw new InvalidOperationException("Zengdao choices lost their frozen issued public entities.");}
  else if(r.Phase is not{}phase||phase.OwnerSeat!=f.OwnerSeat||phase.ActualTurn!=r.ActualTurn||f.WindowContext?.WeiluPhase!=phase||r.WindowId!=f.WindowContext.ParentFrameId)throw new InvalidOperationException("Weilu lost its frozen original actual phase.");
 }
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 310)]
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 310)]
 private bool CanContinueLvQian(ProgramSkillFrame f)
 {
  if(f.LvQian is not null){AssertLvQian(f);return true;}
  if(f.InstructionIndex!=0||f.TriggerId is null||f.WindowContext is not{}window)return false;
  var candidate=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,window.OccurrenceIndex);
  return HasIssuedWeiluDebtCandidate(candidate,window)||IsIssuedZengdaoCandidate(candidate,window);
 }
 [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 310)]
 private bool IsLvQianMovement(ProgramSkillFrame f,SkillProgramEffect?e,ProgramMovementContinuation p)=>f.LvQian is not null&&e is not null&&LvQianComposition.Own(e.Op)&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null&&CanContinueLvQian(f);
 private bool LvQianFirstChild(ProgramSkillFrame root,ResolutionFrame child)
 {
  if(root.LvQian is not{}r||!CanContinueLvQian(root))return false;
  if(child is CardsMovedTriggerWindowFrame moved)return moved.Batch.ParentFrameId==root.Id&&moved.Batch.AwaitingProgramFrameId==root.Id&&moved.Batch.OriginOwnerSeat==root.OwnerSeat&&moved.Batch.OriginSkillId==root.SkillId&&moved.Batch.OriginSkillInstanceId==root.SkillInstanceId&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After);
  if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==root.Id&&hp.ResumeFrameId==root.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&r.HpProof is{}proof&&hp.Change.TargetSeat==proof.TargetSeat&&hp.Change.Kind==proof.Kind&&hp.Change.HpBefore==proof.HpBefore&&hp.Change.Amount<=proof.Amount;
  return child is RecoveryReplacementFrame recovery&&recovery.ParentFrameId==root.Id&&recovery.Return.ResumeFrameId==root.Id&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&IsLvQianRecoveryCompletion(root,recovery.Attempt);
 }
 private ProgramSkillFrame? LvQianObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.LvQianFirstChild(r,c),static(e,i)=>e.HasObserverSuffix(i,static(x,n)=>x.IsSameNameHandObserverEdge(n),static(x,n,d)=>x.IsSuspendedOrPaidRescueObserverSuffix(n,d)||x.TieredRoundZeroDyingRescueRide(n,d)||(x.DrawFundedDistinctBasicDyingRescueRide(n,d) || x.HuanpeiDyingRescueRide(n,d))));
 [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 270)]
 [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 380)]
 private bool HasLvQianDamageObserver(long id)=>LvQianObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 470)]
 private bool HasLvQianBeforeDamageObserver(long id)=>LvQianObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 240)]
 private bool IsLvQianDying()=>ActiveDying is{}d&&LvQianObserverRoot()is{}r&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 380)]
 private bool TryAdvanceLvQianSubtree()=>_pendingDecision is null&&LvQianObserverRoot()is not null&&TryAdvanceObserverSubtree(advanceRecoveryReplacement:true);
 [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 610)]
 private bool AllowsLvQianNestedDamage(ProgramSkillFrame observer,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
 {
  if(sourceLess||observer.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged)||observer.AttackAttempt is not null||observer.InstructionIndex<1||_resolutionStack.LastOrDefault()?.Id!=observer.Id||LvQianObserverRoot()is not{}r||r.Id==observer.Id)return false;var e=GetPausedPrivateOfferEffect(observer);return e is{Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(observer,p):ResolveProgramEffectTarget(observer,e.Target));
 }

 private bool IsLvQianRecoveryCompletion(ProgramSkillFrame f,RecoveryAttempt attempt)=>f.LvQian is{Stage:LvQianStage.RecoveryChildren,HpProof:{Kind:HpChangeKind.Recovery}proof,Phase:{}phase}r&&phase==f.WindowContext?.WeiluPhase&&attempt.SourceSeat==f.OwnerSeat&&attempt.TargetSeat==proof.TargetSeat&&attempt.Amount==proof.Amount&&attempt.HpBefore==proof.HpBefore&&attempt.Completion.Producer==RecoveryAttemptProducer.Program&&attempt.Completion.InstructionIndex==f.InstructionIndex&&CompleteProgramEventHistory().OfType<WeiluDebtConsumedEvent>().Count(e=>e.FrameId==f.Id&&e.Phase==phase&&e.TargetSeat==proof.TargetSeat&&e.TargetAlive&&e.Amount>=proof.Amount)==1;

}
