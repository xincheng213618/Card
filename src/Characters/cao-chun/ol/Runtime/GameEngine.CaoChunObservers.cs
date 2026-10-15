namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool HasCaoChunCapability=>_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.DrawAndRequestShanjiaSlash);
 private bool HasIssuedCaoChunDebt {get{foreach(var p in _players)if(p.Shanjia?.Debt is not null)return true;return false;}}
 private static bool ShanjiaHeld(CardLocation location,int owner)=>location.OwnerSeat==owner&&location.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain;
 private void ObserveCaoChunMovement(CardMovementBatchContext batch)
 {
  if(!HasCaoChunCapability)return;
  foreach(var group in batch.Movements.Where(m=>m.From.OwnerSeat is{}owner&&ShanjiaHeld(m.From,owner)&&!ShanjiaHeld(m.To,owner)&&CardCatalog.Get(m.CardKind).CategoryName=="装备牌").GroupBy(m=>m.From.OwnerSeat!.Value))
  {
   var owner=_players[group.Key];if(owner.Shanjia is not{}state||state.LossMarks==0||!owner.IsAlive||!HasRuntimeSkillInstance(owner,state.Source.SkillId,state.Source.SkillInstanceId)||_contentRegistry.GetSkill(state.Source.SkillId).Program?.GameplayHash!=state.GameplayHash)continue;
   foreach(var movement in group.OrderBy(m=>m.Sequence))
   {
    if(CompleteProgramEventHistory().OfType<ShanjiaMarksChangedEvent>().Any(e=>e.Source==state.Source&&e.MovementSequence==movement.Sequence))continue;
    var next=Math.Max(0,state.LossMarks-1);owner.Shanjia=state=state with{LossMarks=next};AdvanceEventRulesAndQueueFact(new ShanjiaMarksChangedEvent(state.Source,state.GameplayHash,next+1,next,movement.Sequence));if(next==0)break;
   }
  }
 }
 // Called only after the actual native material payment, never from a declared/finalized header.
 private CaoChunHandUsePayment? CaptureCaoChunPaidHandUse(long nativeParentId,CardActionContext action,long before,long after,ProgramCompletedResponseKind? responseKind=null)
 {
  if((!HasCaoChunCapability)||_phase!=TurnPhase.Play||action.ActorSeat!=_currentSeat||action.ProviderSeat!=action.ActorSeat||!_players[action.ActorSeat].IsAlive||_players[action.ActorSeat].Shanjia?.Debt is not{}debt||debt.ActualTurn!=_turnNumber||debt.PhaseInstance!=_cardUseDebitPhaseInstanceId||debt.Remaining<=0||before>after||after>_movementSequence)return null;
  if(action.Type==CardActionType.Use)
  {
   if(responseKind is not null||LifecycleCardUse(nativeParentId)is not{Action:{}actual}use||!SameOverflowAction(actual,action)||!HasExactAcceptedActualHandGainUse(use,action))return null;
  }
  else if(responseKind is not{}kind||!IsCompletedResponseUse(action,kind)||!CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Any(e=>SameOverflowAction(e.Action,action)))return null;
  var hand=action.PhysicalCards.Where(c=>c.From.OwnerSeat==action.ActorSeat&&c.From.Zone is CardZoneKind.Hand or CardZoneKind.WoodenOxGrain).ToArray();if(hand.Length==0||hand.Select(c=>c.CardId).Distinct().Count()!=hand.Length)return null;
  var reason=action.Type==CardActionType.Response?(responseKind==ProgramCompletedResponseKind.Nullification&&action.PhysicalCards.Count==1?CardMoveReasons.Nullification:CardMoveReasons.Respond):action.EffectiveKind==CardKind.IronChain?CardMoveReasons.IronChainUse:CardMoveReasons.Use;
  // Some native uses (equipment, delayed placement) retain their exact producer reason.
  bool Payment(CardMovementRecord m,CardActionCost c)=>m.Sequence>before&&m.Sequence<=after&&m.CardId==c.CardId&&m.CardKind==c.CardKind&&m.From==c.From&&(m.To==CardLocation.Processing||IsProgramAlternativeCost(action,c.CardId)&&m.To==CardLocation.DrawPile)&&(m.Reason==reason||action.Type==CardActionType.Use&&CardCatalog.Get(action.EffectiveKind).CategoryName=="装备牌"&&m.Reason==CardMoveReasons.EquipmentUse);
  if(hand.Any(c=>_cardMovements.Count(m=>Payment(m,c))!=1))return null;
  if(CompleteProgramEventHistory().OfType<ShanjiaHandUseDebitedEvent>().Any(e=>e.Source==debt.Source&&e.ActionId==action.ActionId))return null;
  var proof=new CaoChunHandUsePayment(debt,nativeParentId,action,before,after,responseKind){HandMaterials=hand,Payments=_cardMovements.Where(m=>hand.Any(c=>Payment(m,c))).ToArray()};
  if(action.Type==CardActionType.Use&&LifecycleCardUse(nativeParentId)is{}native)UpdateLifecycleCardUse(nativeParentId,f=>f with{CaoChunDebtQualification=proof});return proof;
 }
 private bool ValidCaoChunPayment(CaoChunHandUsePayment p)
 {
  var d=p.Debt;if(d.ActualTurn!=_turnNumber||d.PhaseInstance!=_cardUseDebitPhaseInstanceId||d.Source.OwnerSeat!=p.Action.ActorSeat||p.Action.ActorSeat!=p.Action.ProviderSeat||p.HandMaterials.Count==0||p.Before>p.After||p.After>_movementSequence||!CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Any(e=>SameOverflowAction(e.Action,p.Action))||CompleteProgramEventHistory().OfType<ShanjiaDebtArmedEvent>().Count(e=>e.Source==d.Source&&e.GameplayHash==d.GameplayHash&&e.ActivationFrameId==d.ActivationFrameId&&e.ActualTurn==d.ActualTurn&&e.PhaseInstance==d.PhaseInstance&&e.FrozenX==d.FrozenX)!=1)return false;
  if(p.Action.Type==CardActionType.Use&&(LifecycleCardUse(p.NativeParentId)is not{Action:{}a}||!SameOverflowAction(a,p.Action)))return false;
  if(p.Action.Type==CardActionType.Response&&(p.ResponseKind is not{}kind||!IsCompletedResponseUse(p.Action,kind)))return false;
  return p.Payments.Count==p.HandMaterials.Count&&p.Payments.Select(m=>m.CardId).Distinct().Count()==p.Payments.Count&&p.HandMaterials.All(c=>p.Action.PhysicalCards.Contains(c)&&ShanjiaHeld(c.From,d.Source.OwnerSeat)&&c.From.Zone!=CardZoneKind.Equipment&&p.Payments.Count(m=>_cardMovements.Contains(m)&&m.Sequence>p.Before&&m.Sequence<=p.After&&m.CardId==c.CardId&&m.CardKind==c.CardKind&&m.From==c.From)==1);
 }
 private ProgramTriggerCandidate? CaoChunDebtCandidate(CaoChunHandUsePayment p)=>!ValidCaoChunPayment(p)||!_players[p.Debt.Source.OwnerSeat].IsAlive||_players[p.Debt.Source.OwnerSeat].Shanjia?.Debt!=p.Debt?null:new(p.Debt.Source.OwnerSeat,p.Debt.Source.SkillId,"hand-use-debt",p.Debt.Source.SkillInstanceId,p.Debt.GameplayHash,0);
 private ProgramSkillWindowContext CaoChunDebtContext(CaoChunHandUsePayment p,long windowId)=>new(SkillProgramTriggerWindow.CardUseBeforeTargetEffects,windowId,p.Debt.Source.OwnerSeat,CardUse:new(p.Action.ActionId,p.NativeParentId,p.Action.ActorSeat,null,p.Action.EffectiveKind,p.Action.EffectiveSuit,p.Action.EffectiveIsRed,false,null)){CaoChunPayment=p};
 private bool HasIssuedCaoChunDebtCandidate(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)
 {
  if(context.Window!=SkillProgramTriggerWindow.CardUseBeforeTargetEffects||context.CaoChunPayment is not{}p||CaoChunDebtCandidate(p)!=candidate||_contentRegistry.GetSkill(candidate.SkillId).Program?.GameplayHash!=candidate.GameplayHash||_resolutionStack.SingleOrDefault(f=>f.Id==context.ParentFrameId)is not ProgramCardTriggerWindowFrame window||GetCardActionWindow(window)!=SkillProgramTriggerWindow.CardUseBeforeTargetEffects||!SameOverflowAction(window.Action,p.Action)||window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count)return false;
  var current=window.Candidates[window.CandidateIndex];return ToSharedCandidate(current)==candidate&&current.FrozenContext?.CaoChunPayment==p&&context.CardUse?.CardActionId==p.Action.ActionId&&window.ParentFrameId==p.NativeParentId;
 }
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 490)]
 private bool CanOfferCaoChun(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)
 {
  var features=ProgramInstructionResolver.Default.Features(t);
  if(features.HasOperation(SkillProgramEffectOp.PayShanjiaHandUseDebt))return w.CaoChunPayment is{}p&&CaoChunDebtCandidate(p)==c;
  if(features.HasOperation(SkillProgramEffectOp.RequestShanjiaEndingSlash))return w.Window==SkillProgramTriggerWindow.PlayEnding&&c.OwnerSeat==_currentSeat&&_players[c.OwnerSeat].Shanjia?.Debt is{}d&&d.ActualTurn==_turnNumber&&d.PhaseInstance==_cardUseDebitPhaseInstanceId&&!d.EndingConsumed&&(!d.DiscardedAny||d.OnlyEquipment);
  if(features.HasOperation(SkillProgramEffectOp.InitializeShanjiaLossMarkers))return _players[c.OwnerSeat].Shanjia is null;
  return true;
 }
 private bool CanContinueCaoChun(ProgramSkillFrame f)
 {
  if(f.CaoChun is{}r){if(r.Stage==CaoChunStage.InitialSlash)return ValidCaoChunPaidActivation(f);if(r.Stage is CaoChunStage.DrawChildren or CaoChunStage.DiscardChildren or CaoChunStage.NativeSlash){RequireCaoChun(f);return true;}return r.Payment is{}p&&ValidCaoChunPayment(p)&&r.Stage==CaoChunStage.DebtChoice;}
  return f.InstructionIndex==0&&f.WindowContext?.CaoChunPayment is{}issued&&ValidCaoChunPayment(issued)&&CaoChunDebtCandidate(issued)is{}c&&MountObserverCandidateMatches(f,c);
 }
 private bool CaoChunFirstChild(ProgramSkillFrame root,ResolutionFrame child)
 {
  if(!CanContinueCaoChun(root)||root.CaoChun is not{}r)return false;
  if(child is CardUseFrame native)return CaoChunNativeRidesOn(root,native);
  if(child is CardsMovedTriggerWindowFrame w)
  {
   if(w.Batch.Id!=w.Id||w.Batch.ParentFrameId!=root.Id||w.Batch.AwaitingProgramFrameId!=root.Id||w.ResumeProgramFrameId is not null||w.Batch.OriginOwnerSeat!=root.OwnerSeat||w.Batch.OriginSkillId!=root.SkillId||w.Batch.OriginSkillInstanceId!=root.SkillInstanceId||w.Batch.Movements.Count==0)return false;
   return w.Batch.Movements.All(m=>_cardMovements.Contains(m)&&(m.Sequence>r.Before&&m.Sequence<=r.After&&(r.Stage==CaoChunStage.DrawChildren?m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(root.OwnerSeat)&&m.Reason.Value==ShanjiaDrawReason:w.Batch.Id==r.PaidBatchId&&m.CardId==r.DiscardId&&m.From==r.DiscardFrom&&m.To==CardLocation.DiscardPile&&m.Reason.Value==ShanjiaDiscardReason)||r.Stage==CaoChunStage.DiscardChildren&&r.DiscardKind==CardKind.WoodenOx&&r.DiscardFrom==CardLocation.Equipment(root.OwnerSeat)&&r.PaidBatchId is >0&&w.Batch.ParentBatchId==r.PaidBatchId&&m.Sequence>r.After&&m.From==CardLocation.WoodenOxGrain(root.OwnerSeat)&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard));
  }
  if(child is ProgramLifecycleTriggerWindowFrame skills&&skills.Window==SkillProgramTriggerWindow.SkillsChanged)return skills.ResumeProgramFrameId==root.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
  if(r.Stage!=CaoChunStage.DiscardChildren||r.DiscardKind!=CardKind.SilverLion||r.DiscardFrom!=CardLocation.Equipment(root.OwnerSeat))return false;
  if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==root.Id&&hp.ResumeFrameId==root.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.SourceSeat==root.OwnerSeat&&hp.Change.TargetSeat==root.OwnerSeat&&hp.Change.Amount==1;
  return child is RecoveryReplacementFrame recovery&&RecoveryReplacementFrameRidesOn(recovery,root)&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&recovery.Attempt.SourceSeat==root.OwnerSeat&&recovery.Attempt.TargetSeat==root.OwnerSeat&&recovery.Attempt.Amount==1&&recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&recovery.Attempt.Completion.MoveReason is { Value: ShanjiaDiscardReason };

 }
 private ProgramSkillFrame? CaoChunObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.CaoChunFirstChild(r,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
 private bool HasCaoChunDamageObserver(long id)=>CaoChunObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 private bool HasCaoChunBeforeDamageObserver(long id)=>CaoChunObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 private bool IsCaoChunDying()=>ActiveDying is{}d&&CaoChunObserverRoot()is{}r&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 private bool TryAdvanceCaoChunSubtree()=>_pendingDecision is null&&CaoChunObserverRoot()is not null&&TryAdvanceObserverSubtree(advanceRecoveryReplacement:true);
 private bool AllowsCaoChunNestedDamage(ProgramSkillFrame o,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
 {
  if(sourceLess||o.AttackAttempt is not null||o.InstructionIndex<1||_resolutionStack.LastOrDefault()?.Id!=o.Id||CaoChunObserverRoot()is not{}root||root.Id==o.Id)return false;var e=GetPausedPrivateOfferEffect(o);return e is{Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(o,p):ResolveProgramEffectTarget(o,e.Target));
 }
 private void ExpireCaoChunPlayDebt(int owner,int phaseInstance)
 {if(_players[owner].Shanjia is{}s&&s.Debt?.PhaseInstance==phaseInstance)_players[owner].Shanjia=s with{Debt=null};}

}
