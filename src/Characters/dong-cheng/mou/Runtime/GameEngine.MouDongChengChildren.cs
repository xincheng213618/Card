namespace CardGame.Core;
public sealed partial class GameEngine
{
 [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,37400)]private bool MouDcMovement(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation p)=>f.MouDongCheng is{}r&&MouDongCheng.Valid(f)&&effect?.Op==r.Operation&&p==f.PendingMovementContinuation&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
 private bool MouDcFirstChild(ProgramSkillFrame f,ResolutionFrame child)
 {
  if(!MouDongCheng.Valid(f)||f.MouDongCheng is not{}r)return false;
  if(child is CardUseFrame use)return MouDcUseRidesOn(f,use);
  if(child is CardsMovedTriggerWindowFrame moved&&f.PendingMovementContinuation is{}p&&MouDcMovement(f,GetPausedPrivateOfferEffect(f),p))return ProgramMovementReturnMatches(f,moved)&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After);
  if(child is RecoveryReplacementFrame replacement)return RecoveryReplacementFrameRidesOn(replacement,f);
  if(child is HpChangedTriggerWindowFrame hp)return ProgramRecoveryReceiptMatches(f,hp,CompleteProgramEventHistory().ToArray())||hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&r.Stage==MouDongChengStage.SlotChildren&&hp.Change.Kind==HpChangeKind.Recovery&&ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(e=>e.PlayerSeat==hp.Change.TargetSeat&&e.RecoveredAmount==hp.Change.Amount&&e.RemainingHp==hp.Change.HpAfter&&e.Reason.Value=="equipment.slot-abolished");
  return child is ProgramLifecycleTriggerWindowFrame state&&state.ResumeProgramFrameId==f.Id&&(state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange&&ProgramEventHistory<CharacterStateChangedEvent>().Any(e=>e.Change.Id==state.Id&&e.Change.ParentFrameId==f.Id)||state.Window==SkillProgramTriggerWindow.SkillsChanged&&state.Continuation==ProgramLifecycleContinuation.ResumeParentProgram);
 }
 private bool MouDcEdge(int i)=>IsMovementObserverEdge(i)||IsSameNameHandObserverEdge(i)||IsYanPuDyingExitEdge(i)||TypedDamagePostEventEdge(_resolutionStack[i-1],_resolutionStack[i])||_resolutionStack[i-1]is ProgramSkillFrame f&&MouDcFirstChild(f,_resolutionStack[i])||NativeDrawAttemptRidesOn(_resolutionStack[i],_resolutionStack[i-1])||RecoveryReplacementFrameRidesOn(_resolutionStack[i],_resolutionStack[i-1]);
 private bool MouDcSuffix(int i)=>_resolutionStack[i+1]is DyingFrame d&&i+2<_resolutionStack.Count&&IsSuspendedOrPaidRescueObserverSuffix(i+1,d)||HasObserverSuffix(i,static(e,n)=>e.MouDcEdge(n),static(e,n,dying)=>e.IsSuspendedOrPaidRescueObserverSuffix(n,dying));
 private ProgramSkillFrame? MouDcRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.MouDcFirstChild(f,c),static(e,n)=>e.MouDcSuffix(n));
 [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,37400)][ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,37400)][ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,37400)]private bool MouDcDamageObserver(long id)=>MouDcRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 [ProgramDyingProof(ProgramDyingProofPhase.Runtime,37400)][ProgramDyingProof(ProgramDyingProofPhase.Invariant,37400)]private bool MouDcDying()=>ActiveDying is{}d&&MouDcRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,37400)]private bool AdvanceMouDcChildren()=>_pendingDecision is null&&MouDcRoot()is not null&&TryAdvanceObserverSubtree(true);
 [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,37400)][ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,37400)]private bool MouDcRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)
 {var i=_resolutionStack.FindIndex(x=>x.Id==f.Id);var c=_resolutionStack.FindIndex(x=>x.Id==recovery.Id);return i>=0&&c>i&&MouDongCheng.Valid(f)&&MouDcFirstChild(f,_resolutionStack[i+1])&&(c==i+1||MouDcSuffix(i))&&RecoveryReplacementFrameRidesOn(recovery,_resolutionStack[c-1]);}
}
