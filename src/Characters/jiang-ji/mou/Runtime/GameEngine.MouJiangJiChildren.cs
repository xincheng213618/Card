namespace CardGame.Core;
public sealed partial class GameEngine
{
 [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,36300)]private bool MouJjMovement(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation p)=>f.MouJiangJi is{}r&&effect?.Op==r.Operation&&MouJiangJi.Valid(f)&&p==f.PendingMovementContinuation&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
 private bool MouJjFirstChild(ProgramSkillFrame f,ResolutionFrame child)
 {
  if(!MouJiangJi.Valid(f)||f.MouJiangJi is not{}r)return false;
  if(child is CardUseFrame u)return MouJjNativeRidesOn(u,f);
  if(child is CardsMovedTriggerWindowFrame moved&&f.PendingMovementContinuation is{}p&&MouJjMovement(f,GetPausedPrivateOfferEffect(f),p))return ProgramMovementReturnMatches(f,moved)&&moved.Batch.ParentFrameId==f.Id&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After);
  if(NativeDrawAttemptRidesOn(child,f))return r.Stage==MouJiangJiStage.Drawing;
  var lion=r.Card is{Kind:CardKind.SilverLion,From.Zone:CardZoneKind.Equipment}card&&_cardMovements.Any(m=>m.CardId==card.Id&&m.From==card.From&&m.Sequence>r.Before&&m.Sequence<=r.After)?card:null;
  var paidLionHolder=lion?.From.OwnerSeat??(r.Stage==MouJiangJiStage.DiscardChildren&&_cardMovements.Any(m=>r.Selected.Contains(m.CardId)&&m.CardKind==CardKind.SilverLion&&m.From==CardLocation.Equipment(r.Target)&&m.Sequence>r.Before&&m.Sequence<=r.After)?r.Target:-1);
  if(paidLionHolder>=0)
  {
   if(child is RecoveryReplacementFrame rr)return RecoveryReplacementFrameRidesOn(rr,f)&&rr.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&rr.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&rr.Attempt.SourceSeat==paidLionHolder&&rr.Attempt.TargetSeat==paidLionHolder;
   if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.TargetSeat==paidLionHolder;
  }
  return child is ProgramLifecycleTriggerWindowFrame state&&state.ResumeProgramFrameId==f.Id&&(state.Window==SkillProgramTriggerWindow.SkillsChanged&&state.Continuation==ProgramLifecycleContinuation.ResumeParentProgram||state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange&&ProgramEventHistory<CharacterStateChangedEvent>().Any(e=>e.Change.Id==state.Id&&e.Change.ParentFrameId==f.Id));
 }
 private bool MouJjEdge(int i)=>IsMovementObserverEdge(i)||IsSameNameHandObserverEdge(i)||IsYanPuDyingExitEdge(i)||TypedDamagePostEventEdge(_resolutionStack[i-1],_resolutionStack[i])||_resolutionStack[i-1]is ProgramSkillFrame f&&MouJjFirstChild(f,_resolutionStack[i])||NativeDrawAttemptRidesOn(_resolutionStack[i],_resolutionStack[i-1])||RecoveryReplacementFrameRidesOn(_resolutionStack[i],_resolutionStack[i-1]);
 private bool MouJjSuffix(int n)=>_resolutionStack[n+1]is DyingFrame d&&n+2<_resolutionStack.Count&&IsSuspendedOrPaidRescueObserverSuffix(n+1,d)||HasObserverSuffix(n,static(g,i)=>g.MouJjEdge(i),static(g,i,dying)=>g.IsSuspendedOrPaidRescueObserverSuffix(i,dying));
 private ProgramSkillFrame? MouJjObserverRoot()=>FindProgramObserverRoot(static(g,f,c)=>g.MouJjFirstChild(f,c),static(g,n)=>g.MouJjSuffix(n));
 [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,36300)][ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,36300)]private bool MouJjDamageWindow(long id)=>MouJjObserverRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==id&&x is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
 [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,36300)]private bool MouJjBeforeDamage(long id)=>MouJjObserverRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==id&&x is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
 [ProgramDyingProof(ProgramDyingProofPhase.Invariant,36300)][ProgramDyingProof(ProgramDyingProofPhase.Runtime,36300)]private bool MouJjDying()=>ActiveDying is{}d&&MouJjObserverRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==d.Id)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
 [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts,36300)][ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts,36300)]private bool MouJjNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
 {if(f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||sourceLess||MouJjObserverRoot()is not{}root||root.Id==f.Id)return false;var effect=GetPausedPrivateOfferEffect(f);return effect is{Op:SkillProgramEffectOp.Damage}&&amount==effect.Amount&&source==effect.ActorReference&&nature==effect.DamageNature&&target==(effect.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,effect.Target));}
 [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,36300)][ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,36300)]private bool MouJjRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)
 {if(MouJjFirstChild(f,recovery))return true;var n=_resolutionStack.FindIndex(x=>x.Id==f.Id);var c=_resolutionStack.FindIndex(x=>x.Id==recovery.Id);return n>=0&&c>n+1&&MouJjFirstChild(f,_resolutionStack[n+1])&&MouJjSuffix(n)&&RecoveryReplacementFrameRidesOn(recovery,_resolutionStack[c-1]);}
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,36300)]private bool MouJjAdvanceChildren()=>_pendingDecision is null&&MouJjObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
