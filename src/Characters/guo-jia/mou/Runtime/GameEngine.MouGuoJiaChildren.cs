namespace CardGame.Core;
public sealed partial class GameEngine
{
 [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,37100)]private bool MouGjMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>f.MouGuoJia is{}r&&MouGuoJia.Valid(f)&&e?.Op==r.Operation&&p==f.PendingMovementContinuation&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
 private bool MouGjFirstChild(ProgramSkillFrame f,ResolutionFrame child)
 {
  if(!MouGuoJia.Valid(f)||f.MouGuoJia is not{}r)return false;
  if(child is JudgmentFrame j)return IsMouGjJudgment(j,f);
  if(child is CardsMovedTriggerWindowFrame moved&&f.PendingMovementContinuation is{}p&&MouGjMovement(f,GetPausedPrivateOfferEffect(f),p))return ProgramMovementReturnMatches(f,moved)&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&(moved.Batch.ParentFrameId==f.Id&&m.Sequence>r.Before&&m.Sequence<=r.After||r.Stage==MouGuoJiaStage.JudgmentChildren&&moved.Batch.ParentFrameId==r.JudgmentId&&m.Sequence==r.JudgmentHandoff&&m.CardId==r.JudgmentCard&&m.From==CardLocation.Judgment(f.OwnerSeat)&&m.To==CardLocation.Processing&&m.Reason.Value=="skill-program.judgment.result"));
  if(r.Stage==MouGuoJiaStage.DamageChildren&&f.AttackAttempt is{TargetSeat:var target,SourceSeat:var source,SourceLess:true}a&&target==f.OwnerSeat&&source==f.OwnerSeat&&a.Nature==DamageNature.Thunder&&f.AttackReturn is not null&&ProgramEventHistory<MouGjLightningIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.JudgmentId==r.JudgmentId&&e.Target==target&&e.Amount==3)==1)
  {
   if(child is BeforeDamageProgramWindowFrame before)return before.ParentFrameId==f.Id&&before.SourceSeat==f.OwnerSeat&&before.TargetSeat==f.OwnerSeat&&before.Nature==DamageNature.Thunder;
   if(DyingSuitsStructuralEdge(f,child)||TypedDamagePostEventEdge(f,child))return true;
  }
  return child is ProgramLifecycleTriggerWindowFrame state&&state.ResumeProgramFrameId==f.Id&&(state.Window==SkillProgramTriggerWindow.SkillsChanged&&state.Continuation==ProgramLifecycleContinuation.ResumeParentProgram||state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange&&ProgramEventHistory<CharacterStateChangedEvent>().Any(e=>e.Change.Id==state.Id&&e.Change.ParentFrameId==f.Id));
 }
 private bool MouGjJudgmentEdge(int i)=>i>0&&_resolutionStack[i-1]is JudgmentFrame j&&IsMouGjLightningJudgment(j)&&_resolutionStack[i]is ProgramJudgmentTriggerWindowFrame p&&p.ParentFrameId==j.Id&&p.Judgment.JudgmentFrameId==j.Id;
 private bool MouGjEdge(int i)=>MouGjJudgmentEdge(i)||IsMovementObserverEdge(i)||IsSameNameHandObserverEdge(i)||IsYanPuDyingExitEdge(i)||TypedDamagePostEventEdge(_resolutionStack[i-1],_resolutionStack[i])||_resolutionStack[i-1]is ProgramSkillFrame f&&MouGjFirstChild(f,_resolutionStack[i])||NativeDrawAttemptRidesOn(_resolutionStack[i],_resolutionStack[i-1])||RecoveryReplacementFrameRidesOn(_resolutionStack[i],_resolutionStack[i-1]);
 private bool MouGjSuffix(int n)=>_resolutionStack[n+1]is DyingFrame d&&n+2<_resolutionStack.Count&&IsSuspendedOrPaidRescueObserverSuffix(n+1,d)||HasObserverSuffix(n,static(g,i)=>g.MouGjEdge(i),static(g,i,dying)=>g.IsSuspendedOrPaidRescueObserverSuffix(i,dying));
 private ProgramSkillFrame? MouGjRoot()=>FindProgramObserverRoot(static(g,f,c)=>g.MouGjFirstChild(f,c),static(g,n)=>g.MouGjSuffix(n));
 [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,37100)][ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,37100)]private bool MouGjDamageWindow(long id)=>MouGjRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==id&&x is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
 [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,37100)]private bool MouGjBeforeDamage(long id)=>MouGjRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==id&&x is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
 [ProgramDyingProof(ProgramDyingProofPhase.Invariant,37100)][ProgramDyingProof(ProgramDyingProofPhase.Runtime,37100)]private bool MouGjDying()=>ActiveDying is{}d&&MouGjRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==d.Id)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
 [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts,37100)][ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts,37100)]private bool MouGjNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
 {if(f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||sourceLess||MouGjRoot()is not{}root||root.Id==f.Id)return false;var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));}
 [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,37100)][ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,37100)]private bool MouGjRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)
 {var i=_resolutionStack.FindIndex(x=>x.Id==f.Id);var c=_resolutionStack.FindIndex(x=>x.Id==recovery.Id);return i>=0&&c>i+1&&MouGjFirstChild(f,_resolutionStack[i+1])&&MouGjSuffix(i)&&RecoveryReplacementFrameRidesOn(recovery,_resolutionStack[c-1]);}
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,37100)]private bool MouGjAdvanceChildren()=>_pendingDecision is null&&MouGjRoot()is not null&&TryAdvanceObserverSubtree(true);
}
