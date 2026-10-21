namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool HasMouZyFireTargets(CharacterState actor)=>MouZyAnniversary.Budget(actor.Seat)>0;
 private bool IsMouZyTargetAction(CharacterState actor,LegalAction action)=>action.ProgramSkillId==MouZhouYuRuntime.Ronghuo&&action.ProgramActivationId==MouZhouYuRuntime.Extra&&HasMouZyFireTargets(actor)&&(action.PlayedCardKind??(action.CardId is{}id?AdvancedEffectiveHandKind(actor,GetAdvancedCard(id)):CardKind.Dodge))is CardKind.FireSlash or CardKind.FireAttack;
 private int MouZyTargetLimit(CharacterState actor)=>MouZyAnniversary.Budget(actor.Seat);
 [ProgramCardAction(ProgramCardActionPhase.AfterProgram,36000)]private void AddMouZyFireTargets(List<LegalAction> actions,CharacterState actor)
 {
  var budget=MouZyAnniversary.Budget(actor.Seat);if(budget<=0)return;
  var keys=actions.Select(a=>(a.CardId,a.Kind,a.PlayedCardKind,a.ConversionSource,a.TargetCardId,Targets:string.Join(",",a.TargetSeats))).ToHashSet();
  foreach(var a in actions.ToArray().Where(a=>a.CardId is not null&&a.ProgramActivationId is null&&a.Kind is LegalActionKind.Slash or LegalActionKind.FireAttack))
  {
   var(kind,suit)=TargetAdjustmentAppearance(actor,a);if(kind is not(CardKind.FireSlash or CardKind.FireAttack))continue;
   var normal=a.TargetSeats;var extra=_players.Where(p=>!normal.Contains(p.Seat)&&CanBeExtraNextCardTarget(actor,p,a,kind,suit)&&(kind!=CardKind.FireSlash||CanUseSlashTarget(actor,p,GetAdvancedCard(a.CardId!.Value),a.ConversionSource,kind))&&(kind!=CardKind.FireAttack||GetHand(p).Any(c=>p.Seat!=actor.Seat||c.Id!=a.CardId))).Select(p=>p.Seat).ToArray();
   foreach(var group in MouZyExtraGroups(extra,budget).Where(g=>g.Count>0))
   {var targets=Array.AsReadOnly(normal.Concat(group).ToArray());if(!keys.Add((a.CardId,a.Kind,a.PlayedCardKind,a.ConversionSource,a.TargetCardId,string.Join(",",targets))))continue;actions.Add(a with{TargetSeat=targets[0],TargetSeats=targets,ProgramSkillId=MouZhouYuRuntime.Ronghuo,ProgramActivationId=MouZhouYuRuntime.Extra,Description=a.Description+"（融火额外目标）"});}
  }
 }
 [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,36000)]private bool MouZyMovement(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation p)=>f.MouZhouYuAnniversary is{}r&&effect?.Op==r.Operation&&MouZyAnniversary.Valid(f)&&p==f.PendingMovementContinuation&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
 private bool MouZyFirstChild(ProgramSkillFrame f,ResolutionFrame child)
 {
  if(!MouZyAnniversary.Valid(f)||f.MouZhouYuAnniversary is not{}r)return false;
  if(child is CardUseFrame u)return MouZyNativeRidesOn(u,f);
  if(child is CardsMovedTriggerWindowFrame moved&&f.PendingMovementContinuation is{}p&&MouZyMovement(f,GetPausedPrivateOfferEffect(f),p))return ProgramMovementReturnMatches(f,moved)&&moved.Batch.ParentFrameId==f.Id&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After);
  if(NativeDrawAttemptRidesOn(child,f))return r.Stage==MouZhouYuStage.Drawing;
  if(r.Stage==MouZhouYuStage.DamageChildren&&f.AttackAttempt is{}a&&f.AttackReturn is not null&&a.TargetSeat==r.Target&&a.SourceSeat==f.OwnerSeat&&!a.SourceLess)return DyingSuitsStructuralEdge(f,child);
  return child is ProgramLifecycleTriggerWindowFrame lifecycle&&lifecycle.ResumeProgramFrameId==f.Id&&(lifecycle.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange||lifecycle.Window==SkillProgramTriggerWindow.SkillsChanged&&lifecycle.Continuation==ProgramLifecycleContinuation.ResumeParentProgram);
 }
 private bool MouZyEdge(int i)=>IsMovementObserverEdge(i)||IsSameNameHandObserverEdge(i)||IsYanPuDyingExitEdge(i)||TypedDamagePostEventEdge(_resolutionStack[i-1],_resolutionStack[i])||_resolutionStack[i-1]is ProgramSkillFrame f&&MouZyFirstChild(f,_resolutionStack[i])||NativeDrawAttemptRidesOn(_resolutionStack[i],_resolutionStack[i-1])||RecoveryReplacementFrameRidesOn(_resolutionStack[i],_resolutionStack[i-1]);
 private bool MouZySuffix(int i)=>_resolutionStack[i+1]is DyingFrame d&&i+2<_resolutionStack.Count&&IsSuspendedOrPaidRescueObserverSuffix(i+1,d)||HasObserverSuffix(i,static(g,n)=>g.MouZyEdge(n),static(g,n,dying)=>g.IsSuspendedOrPaidRescueObserverSuffix(n,dying));
 private ProgramSkillFrame? MouZyObserverRoot()=>FindProgramObserverRoot(static(g,f,c)=>g.MouZyFirstChild(f,c),static(g,n)=>g.MouZySuffix(n));
 [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,36000)][ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,36000)]private bool MouZyPaidDamage(long id)=>MouZyObserverRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==id&&x is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
 [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,36000)]private bool MouZyBeforeDamage(long id)=>MouZyObserverRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==id&&x is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
 [ProgramDyingProof(ProgramDyingProofPhase.Runtime,36000)][ProgramDyingProof(ProgramDyingProofPhase.Invariant,36000)]private bool MouZyDying()=>ActiveDying is{}d&&MouZyObserverRoot()is{}f&&_resolutionStack.FindIndex(x=>x.Id==d.Id)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
 [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts,36000)][ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts,36000)]private bool MouZyNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
 {if(f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||sourceLess||MouZyObserverRoot()is not{}root||root.Id==f.Id)return false;var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));}
 [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,36000)][ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,36000)]private bool MouZyRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)
 {if(MouZyFirstChild(f,recovery))return true;var i=_resolutionStack.FindIndex(x=>x.Id==f.Id);var n=_resolutionStack.FindIndex(x=>x.Id==recovery.Id);return i>=0&&n>i+1&&MouZyFirstChild(f,_resolutionStack[i+1])&&MouZySuffix(i)&&RecoveryReplacementFrameRidesOn(recovery,_resolutionStack[n-1]);}
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,36000)]private bool MouZyAdvanceChildren()=>_pendingDecision is null&&MouZyObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
