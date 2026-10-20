namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool LiJueFirstChild(ProgramSkillFrame root,ResolutionFrame child)
 {
  if(!CanContinueLiJue(root)||root.LiJue is not{}r)return false;
  if(child is DeathFrame died)return r.Stage==LiJueStage.MaximumHpChildren&&r.MaxAfter==0&&died.ParentFrameId==root.Id&&died.VictimSeat==root.OwnerSeat;
  if(child is HpChangedTriggerWindowFrame hp)return r.Stage==LiJueStage.MaximumHpChildren&&root.PendingMovementContinuation is null&&hp.Continuation==PostEventContinuation.Program&&hp.ResumeFrameId==root.Id&&hp.Change.TargetSeat==root.OwnerSeat&&hp.Change.ParentFrameId==root.Id;
  if(child is ProgramLifecycleTriggerWindowFrame skills)return skills.ResumeProgramFrameId==root.Id&&skills.Window==SkillProgramTriggerWindow.SkillsChanged;
  if(child is not CardsMovedTriggerWindowFrame moved||r.Stage!=LiJueStage.GainChildren)return false;
  return moved.Batch.ParentFrameId==root.Id&&moved.Batch.AwaitingProgramFrameId==root.Id&&moved.Batch.OriginOwnerSeat==root.OwnerSeat&&moved.Batch.OriginSkillId==root.SkillId&&moved.Batch.OriginSkillInstanceId==root.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.Hand(root.OwnerSeat)&&m.Reason.Value==YisuanGainReason);
 }
 private ProgramSkillFrame? LiJueObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.LiJueFirstChild(r,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
 [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 160)]
 [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 270)]
 private bool HasLiJueDamageObserver(long id)=>LiJueObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 360)]
 private bool HasLiJueBeforeDamageObserver(long id)=>LiJueObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 130)]
 private bool IsLiJueDying()=>ActiveDying is{}d&&LiJueObserverRoot()is{}r&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 270)]
 private bool TryAdvanceLiJueSubtree()=>_pendingDecision is null&&LiJueObserverRoot()is not null&&TryAdvanceObserverSubtree(advanceRecoveryReplacement:true);
 [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 500)]
 private bool AllowsLiJueNestedDamage(ProgramSkillFrame observer,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
 {
  if(sourceLess||observer.AttackAttempt is not null||observer.InstructionIndex<1||_resolutionStack.LastOrDefault()?.Id!=observer.Id||observer.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged)||LiJueObserverRoot()is not{}r||r.Id==observer.Id)return false;
  var e=GetPausedPrivateOfferEffect(observer);return e is{Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is{}reference?ResolveProgramParticipant(observer,reference):ResolveProgramEffectTarget(observer,e.Target));
 }
}
