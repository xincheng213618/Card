namespace CardGame.Core;

public sealed partial class GameEngine
{
 private bool YanXiangOriginal(ProgramSkillFrame f,SkillProgramEffectOp operation)
 {
  if(_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash!=f.GameplayHash)return false;
  if(operation==SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzheng)return f.SkillId==AnniversaryYanXiangComposition.Yuzheng&&f.ActivationId=="admonish"&&f.TriggerId is null&&f.WindowContext is null&&f.OwnerSeat==_currentSeat&&_phase==TurnPhase.Play&&f.SelectedTargetSeats is[var actor]&&IsValidPlayerSeat(actor);
  if(f.WindowContext is not{}w||w.OwnerSeat!=f.OwnerSeat||f.ActivationId!=f.TriggerId)return false;
  var index=_resolutionStack.FindIndex(x=>x.Id==f.Id);if(index<1)return false;
  if(operation==SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzhengReward)
   return f.SkillId==AnniversaryYanXiangComposition.Yuzheng&&f.TriggerId is"issued-use-reward" or"issued-response-reward"&&w.Window is SkillProgramTriggerWindow.CardUseCompleted or SkillProgramTriggerWindow.CardResponseCompleted&&
    _resolutionStack[index-1] is ProgramCardTriggerWindowFrame p&&p.Id==w.ParentFrameId&&p.CandidateIndex>=0&&p.CandidateIndex<p.Candidates.Count&&MountObserverCandidateMatches(f,ToSharedCandidate(p.Candidates[p.CandidateIndex]));
  return operation==SkillProgramEffectOp.ResolveAnniversaryYanXiangSuishi&&f.SkillId==AnniversaryYanXiangComposition.Suishi&&f.TriggerId is"own-ending-damage-name" or"other-ending-damage-name"&&w.Window==SkillProgramTriggerWindow.TurnEnding&&
   _resolutionStack[index-1] is TurnEndingBoundaryFrame ending&&ending.Id==w.ParentFrameId&&ending.OwnerSeat==_currentSeat&&ending.TurnNumber==_turnNumber&&w.SourceSeat==ending.OwnerSeat&&w.TargetSeat==ending.OwnerSeat&&
   ending.ItemIndex>=0&&ending.ItemIndex<ending.Items.Count&&ending.Items[ending.ItemIndex].Candidate is{}candidate&&MountObserverCandidateMatches(f,candidate)&&w.OccurrenceIndex==candidate.OccurrenceIndex&&w.Facts==(ending.Items[ending.ItemIndex].Facts??ending.Facts);
 }
 private bool ValidYanXiang(ProgramSkillFrame f)
 {
  if(f.AnniversaryYanXiang is not{}r||f.InstructionIndex!=1||!YanXiangOriginal(f,r.Operation)||r.Source!=YanXiangSource(f)||r.Hash!=f.GameplayHash||r.ParentId!=(f.WindowContext?.ParentFrameId??0)||
   r.Turn!=_turnNumber||r.Round!=_roundNumber||r.Phase!=_cardUseDebitPhaseInstanceId||!IsValidPlayerSeat(r.Actor)||!Enum.IsDefined(r.Stage)||f.SelectedCardIds.Count!=0||r.Before<0||r.After<r.Before||r.After>_movementSequence||
   r.Cards.Any(id=>id<=0)||r.Cards.Distinct().Count()!=r.Cards.Count||r.Required<0||r.Cards.Count>r.Required||r.Requested<0||r.Cursor<0||r.RewardLeases.Distinct().Count()!=r.RewardLeases.Count||r.NativeBatches.Distinct().Count()!=r.NativeBatches.Count||
   ProgramEventHistory<AnniversaryYanXiangStartedEvent>().Count(e=>e==new AnniversaryYanXiangStartedEvent(f.Id,r.Source,r.Hash,r.Operation,r.ParentId,r.Turn,r.Round,r.Phase,r.Actor))!=1)return false;
  if(r.Operation==SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzheng)
  {
   if(f.SelectedTargetSeats is not[var actor]||actor!=r.Actor||r.Option is<0 or>2||r.Plan is not null||r.Name is not null||r.RewardLeases.Count!=0||r.UseId!=0||r.ActionId!=0)return false;
   if(r.Stage==AnniversaryYanXiangStage.Option)return r.Option==0&&r.Cards.Count==0&&r.Draw is null&&YanXiangAvailableOptions(f.OwnerSeat,f.SkillInstanceId).Length>0;
   if(r.Option==0||ProgramEventHistory<AnniversaryYanXiangOptionChosenEvent>().Count(e=>e==new AnniversaryYanXiangOptionChosenEvent(f.Id,r.Source,r.Hash,r.Turn,r.Phase,r.Actor,r.Option))!=1)return false;
   if(r.Stage is not(AnniversaryYanXiangStage.HandCards or AnniversaryYanXiangStage.Drawing or AnniversaryYanXiangStage.MovementChildren or AnniversaryYanXiangStage.Completed))return false;
   if(r.Option==1&&(r.Draw is not null||r.Requested!=0||r.Stage==AnniversaryYanXiangStage.Drawing))return false;
   if(r.Option==2&&(r.Cards.Count!=0||r.Required!=0||r.Moves.Count!=0||r.Requested>5))return false;
  }
  else if(r.Operation==SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzhengReward)
  {
   var action=YanXiangWindowAction(f.WindowContext);
   if(f.SelectedTargetSeats.Count!=0||action is null||r.Actor!=action.ActorSeat||r.RewardAction!=action.ActionId||r.Requested!=2||r.RewardLeases.Count==0||r.Cursor>=r.RewardLeases.Count||r.Option!=0||r.Cards.Count!=0||r.Plan is not null||r.Name is not null||
    r.Stage is not(AnniversaryYanXiangStage.Drawing or AnniversaryYanXiangStage.MovementChildren or AnniversaryYanXiangStage.Completed)||
    r.RewardLeases.Any(id=>!ProgramEventHistory<AnniversaryYanXiangLeaseEvent>().Any(l=>l.Id==id&&l.Option==1&&l.Actor==r.Actor&&l.Source.OwnerSeat==f.OwnerSeat&&l.Source.SkillInstanceId==f.SkillInstanceId&&l.Round==r.Round&&YanXiangLeaseProof(l))||
     ProgramEventHistory<AnniversaryYanXiangRewardRequestedEvent>().Count(e=>e==new AnniversaryYanXiangRewardRequestedEvent(f.Id,id,r.RewardAction))!=1))return false;
  }
  else
  {
   if(f.SelectedTargetSeats.Count!=0||r.Actor!=f.WindowContext?.SourceSeat||r.Option!=0||r.Cards.Count!=0||r.Draw is not null||r.Requested!=0||r.RewardLeases.Count!=0||
    r.Stage is AnniversaryYanXiangStage.Option or AnniversaryYanXiangStage.HandCards or AnniversaryYanXiangStage.Drawing or AnniversaryYanXiangStage.MovementChildren)return false;
   if(r.Stage!=AnniversaryYanXiangStage.DamageName&&(r.Name is not{}name||!YanXiangDamageNames().Contains(name)||ProgramEventHistory<AnniversaryYanXiangNameDeclaredEvent>().Count(e=>e==new AnniversaryYanXiangNameDeclaredEvent(f.Id,r.Actor,name))!=1))return false;
   if(r.Plan is{}p&&(p.CardId<=0||p.From.OwnerSeat!=r.Actor||p.From.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment)||ProgramBasicCardName(p.Kind)!=r.Name||p.Targets.Distinct().Count()!=p.Targets.Count||p.Targets.Any(t=>!IsValidPlayerSeat(t))))return false;
   if(r.Stage is AnniversaryYanXiangStage.IssuingUse or AnniversaryYanXiangStage.NativeUse or AnniversaryYanXiangStage.NativeTail&&r.Plan is null)return false;
   if(r.Stage is AnniversaryYanXiangStage.NativeUse or AnniversaryYanXiangStage.NativeTail&&(r.UseId<=f.Id||r.ActionId<=0||r.Plan is not{}p2||ProgramEventHistory<AnniversaryYanXiangUseIssuedEvent>().Count(e=>e==new AnniversaryYanXiangUseIssuedEvent(f.Id,r.UseId,r.ActionId,r.Actor,p2.CardId,p2.Kind))!=1))return false;
   if(r.Returned&&ProgramEventHistory<AnniversaryYanXiangUseReturnedEvent>().Count(e=>e==new AnniversaryYanXiangUseReturnedEvent(f.Id,r.UseId,r.ActionId))!=1)return false;
  }
  if(r.Moves.Any(m=>!_cardMovements.Contains(m)||m.From!=CardLocation.Hand(r.Actor)||!r.Cards.Contains(m.CardId)||m.Reason.Value!=YanXiangDiscardReason||m.Sequence<=r.Before||m.Sequence>r.After||m.To!=CardLocation.DiscardPile&&!IsRedirectedPaidDiscardMovement(m,m.From,new(YanXiangDiscardReason))))return false;
  if(r.Draw is{}d&&(d.RecipientSeat!=r.Actor||d.RequestedCount!=r.Requested||d.SequenceBefore!=r.Before||d.SequenceAfter!=r.After||ProgramEventHistory<AnniversaryYanXiangDrawReturnedEvent>().Count(e=>e==new AnniversaryYanXiangDrawReturnedEvent(f.Id,r.Cursor,r.Actor,r.Requested,d.ActualDrawIds.Count,r.Before,r.After))!=1))return false;
  return true;
 }
 private bool HasAnniversaryYanXiangPrompt()=>_pendingDecision is{Kind:DecisionKind.ProgramTrigger,IsPrivate:true}p&&_resolutionStack.LastOrDefault() is ProgramSkillFrame{Step:ResolutionFrameStep.AwaitingResponse,AnniversaryYanXiang.Stage:AnniversaryYanXiangStage.Option or AnniversaryYanXiangStage.HandCards or AnniversaryYanXiangStage.DamageName or AnniversaryYanXiangStage.UseChoice}f&&
  p.PlayerSeat==YanXiangChooser(f)&&p.TargetSeat==p.PlayerSeat&&p.SourceSeat==f.OwnerSeat&&ValidYanXiang(f)&&AssistedChoicesEqual(p.Choices,YanXiangChoices(f));
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,46800)]private void AssertYanXiang(ProgramSkillFrame f)
 {if(f.AnniversaryYanXiang is not null&&(!ValidYanXiang(f)||f.PendingMovementContinuation is{}p&&!YanXiangMovement(f,GetPausedPrivateOfferEffect(f),p)))throw new InvalidOperationException("Yan Xiang lost his original candidate, recipient, paid movement, issued lease or whole native return.");}
 [ProgramMovementProof(ProgramMovementProofPhase.NativeCost,46800)] [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment,46800)] [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments,46800)]
 private bool YanXiangMovement(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation p)=>f.AnniversaryYanXiang is{Stage:AnniversaryYanXiangStage.MovementChildren or AnniversaryYanXiangStage.NativeTail}r&&effect?.Op==r.Operation&&p==f.PendingMovementContinuation&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null&&ValidYanXiang(f);
 private bool YanXiangFirstChild(ProgramSkillFrame f,ResolutionFrame child)
 {
  if(!ValidYanXiang(f)||f.AnniversaryYanXiang is not{}r)return false;
  if(child is CardUseFrame use)return r.Stage==AnniversaryYanXiangStage.NativeUse&&IsYanXiangNativeUse(use);
  if(child is NativeDrawAttemptFrame draw)return r.Stage==AnniversaryYanXiangStage.Drawing&&draw.Context.ParentFrameId==f.Id&&draw.Context.RecipientSeat==r.Actor&&draw.Context.RequestedCount==r.Requested&&NativeDrawAttemptRidesOn(draw,f);
  if(child is CardsMovedTriggerWindowFrame moved)return f.PendingMovementContinuation is{}p&&YanXiangMovement(f,GetPausedPrivateOfferEffect(f),p)&&ProgramMovementReturnMatches(f,moved)&&moved.Batch.Movements.All(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&_cardMovements.Contains(m));
  if(child is RecoveryReplacementFrame recovery)return recovery.ParentFrameId==f.Id&&recovery.Return.ResumeFrameId==f.Id&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement;
  if(child is HpChangedTriggerWindowFrame hp)return hp.ResumeFrameId==f.Id&&hp.Change.ParentFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement;
  return child is ProgramLifecycleTriggerWindowFrame state&&state.ResumeProgramFrameId==f.Id&&(state.Window==SkillProgramTriggerWindow.SkillsChanged&&state.Continuation==ProgramLifecycleContinuation.ResumeParentProgram||state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange&&state.CharacterStateContinuation==CharacterStateContinuation.Program);
 }
 private ProgramSkillFrame? YanXiangObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.YanXiangFirstChild(f,c),static(e,i)=>e.YanXiangObserverSuffix(i));
 private bool YanXiangObserverSuffix(int i)=>HasObserverSuffix(i,static(e,n)=>e.IsSameNameHandObserverEdge(n),static(e,n,dying)=>e.IsSuspendedOrPaidRescueObserverSuffix(n,dying)||e.TieredRoundZeroDyingRescueRide(n,dying)||e.DrawFundedDistinctBasicDyingRescueRide(n,dying)||e.HuanpeiDyingRescueRide(n,dying));
 [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,46800)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,46800)]private bool YanXiangDamageObserver(long id)=>YanXiangObserverRoot() is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,46800)]private bool YanXiangBeforeDamage(long id)=>YanXiangObserverRoot() is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 [ProgramDyingProof(ProgramDyingProofPhase.Invariant,46800)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime,46800)]private bool YanXiangDying()=>ActiveDying is{}dying&&YanXiangObserverRoot() is{}root&&_resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,46800)]private bool AdvanceYanXiangChildren()=>_pendingDecision is null&&YanXiangObserverRoot() is not null&&TryAdvanceObserverSubtree(true);
 [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,46800)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,46800)]private bool YanXiangRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)
 {if(f.AnniversaryYanXiang is null)return false;var parent=_resolutionStack.FindIndex(x=>x.Id==f.Id);var child=_resolutionStack.FindIndex(x=>x.Id==recovery.Id);return parent>=0&&child>parent&&YanXiangFirstChild(f,_resolutionStack[parent+1])&&(child==parent+1||YanXiangObserverSuffix(parent))&&RecoveryReplacementFrameRidesOn(recovery,_resolutionStack[child-1]);}
}
