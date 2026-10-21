namespace CardGame.Core;
public sealed partial class GameEngine
{
 [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,36900)]private bool MouJsMovement(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation p)=>f.MouJuShou is{}r&&effect?.Op==r.Operation&&MouJuShou.Valid(f)&&p==f.PendingMovementContinuation&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
 private bool MouJsFirstChild(ProgramSkillFrame f,ResolutionFrame child)
 {
  if(!MouJuShou.Valid(f)||f.MouJuShou is not{}r)return false;
  if(child is CardUseFrame u)return MouJsNativeRidesOn(u,f);
  if(child is CardDeclarationFrame d)return MatchesMouJsDeclaration(f,d);
  if(child is CardsMovedTriggerWindowFrame moved&&f.PendingMovementContinuation is{}p&&MouJsMovement(f,GetPausedPrivateOfferEffect(f),p))return ProgramMovementReturnMatches(f,moved)&&moved.Batch.ParentFrameId==f.Id&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After);
  if(NativeDrawAttemptRidesOn(child,f))return r.Stage is MouJuShouStage.DrawingThree or MouJuShouStage.DrawingOne;
  if(r.Stage==MouJuShouStage.HealthChildren)
  {
   if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.Program&&hp.Change.Kind==HpChangeKind.Loss&&hp.Change.SourceSeat is null&&hp.Change.TargetSeat==r.Actor&&hp.Change.Amount==1&&ProgramEventHistory<ProgramSkillHpLostEvent>().Any(e=>e.FrameId==f.Id&&e.TargetSeat==r.Actor&&e.Amount==1);
   if(child is DyingFrame dying)return dying.ParentFrameId==f.Id&&dying.VictimSeat==r.Actor&&dying.Continuation==DyingContinuationKind.ProgramSkill&&dying.KillerSeat is null&&_pendingHpChanges.Any(p=>p.ParentFrameId==f.Id&&p.Kind==HpChangeKind.Loss&&p.TargetSeat==r.Actor&&p.Amount==1);
  }
  if(r.Stage==MouJuShouStage.PenaltyChildren&&_cardMovements.Any(m=>r.Discard.Contains(m.CardId)&&m.CardKind==CardKind.SilverLion&&m.From==CardLocation.Equipment(r.Actor)&&m.Sequence>r.Before&&m.Sequence<=r.After))
  {
   if(child is RecoveryReplacementFrame rr)return RecoveryReplacementFrameRidesOn(rr,f)&&rr.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&rr.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&rr.Attempt.SourceSeat==r.Actor&&rr.Attempt.TargetSeat==r.Actor;
   if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.TargetSeat==r.Actor;
  }
  return child is ProgramLifecycleTriggerWindowFrame state&&state.ResumeProgramFrameId==f.Id&&(state.Window==SkillProgramTriggerWindow.SkillsChanged&&state.Continuation==ProgramLifecycleContinuation.ResumeParentProgram||state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange&&ProgramEventHistory<CharacterStateChangedEvent>().Any(e=>e.Change.Id==state.Id&&e.Change.ParentFrameId==f.Id));
 }
 private bool MouJsEdge(int i)=>MouJsDeclarationEdge(i)||IsMovementObserverEdge(i)||IsSameNameHandObserverEdge(i)||IsYanPuDyingExitEdge(i)||TypedDamagePostEventEdge(_resolutionStack[i-1],_resolutionStack[i])||_resolutionStack[i-1]is ProgramSkillFrame f&&MouJsFirstChild(f,_resolutionStack[i])||NativeDrawAttemptRidesOn(_resolutionStack[i],_resolutionStack[i-1])||RecoveryReplacementFrameRidesOn(_resolutionStack[i],_resolutionStack[i-1]);
 private bool MouJsSuffix(int n)=>_resolutionStack[n+1]is DyingFrame d&&n+2<_resolutionStack.Count&&IsSuspendedOrPaidRescueObserverSuffix(n+1,d)||HasObserverSuffix(n,static(g,i)=>g.MouJsEdge(i),static(g,i,dying)=>g.IsSuspendedOrPaidRescueObserverSuffix(i,dying));
 private ProgramSkillFrame? MouJsObserverRoot()=>FindProgramObserverRoot(static(g,f,c)=>g.MouJsFirstChild(f,c),static(g,n)=>g.MouJsSuffix(n));
 [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,36900)][ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,36900)]private bool MouJsDamageWindow(long id)=>MouJsObserverRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==id&&x is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
 [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,36900)]private bool MouJsBeforeDamage(long id)=>MouJsObserverRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==id&&x is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
 [ProgramDyingProof(ProgramDyingProofPhase.Invariant,36900)][ProgramDyingProof(ProgramDyingProofPhase.Runtime,36900)]private bool MouJsDying()=>ActiveDying is{}d&&MouJsObserverRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==d.Id)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
 [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts,36900)][ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts,36900)]private bool MouJsNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
 {if(f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||sourceLess||MouJsObserverRoot()is not{}root||root.Id==f.Id)return false;var effect=GetPausedPrivateOfferEffect(f);return effect is{Op:SkillProgramEffectOp.Damage}&&amount==effect.Amount&&source==effect.ActorReference&&nature==effect.DamageNature&&target==(effect.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,effect.Target));}
 [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,36900)][ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,36900)]private bool MouJsRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame rr)
 {if(MouJsFirstChild(f,rr))return true;var n=_resolutionStack.FindIndex(x=>x.Id==f.Id);var c=_resolutionStack.FindIndex(x=>x.Id==rr.Id);return n>=0&&c>n+1&&MouJsFirstChild(f,_resolutionStack[n+1])&&MouJsSuffix(n)&&RecoveryReplacementFrameRidesOn(rr,_resolutionStack[c-1]);}
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,36900)]private bool MouJsAdvanceChildren()=>_pendingDecision is null&&MouJsObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,36901)]private void ObserveMouJsUse(IGameEvent payload)
 {if(payload is CardUseDeclaredEvent declared&&LifecycleCardUse(declared.ResolutionId)is{Action:{Type:CardActionType.Use}action})MouJuShou.ObserveUse(action.ActorSeat,action.PhysicalCards.Select(c=>c.CardId).ToArray());}
}
