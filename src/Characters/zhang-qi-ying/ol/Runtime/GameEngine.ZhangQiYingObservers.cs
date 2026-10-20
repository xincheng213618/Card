namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool HasZhangQiYingCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.InitializeFaluSuitTokens)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GainFaluTokensAfterActualDiscard)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ReorderTopByCurrentFaluTokens)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ChooseFinalJudgmentFiveByZiwei)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.IncreaseActualSourceDamageByYuqing)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GainThreeDeckCategoriesAfterElementalDamage);
 private void AssertZhangQiYing(ProgramSkillFrame f)
 {
  if(f.ZhangQiYing is not{}r||r.Source!=ZhangQiYingSource(f)||r.GameplayHash!=f.GameplayHash||r.Instruction!=f.InstructionIndex||r.ParentId!=f.WindowContext?.ParentFrameId||r.Before>r.After||r.After>_movementSequence)throw new InvalidOperationException("Zhang Qi Ying lost its exact owning source and instruction.");
  if(r.Stage=="gain-children"&&(CompleteProgramEventHistory().OfType<ZhenyiDeckGainPaidEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Before==r.Before&&e.After==r.After&&e.ActualCount==r.Cards.Count)!=1||r.Cards.Count>3||r.Cards.Distinct().Count()!=r.Cards.Count||f.PendingMovementContinuation is not{BeforeCount:0,CoverageResultBind:null,SubjectSeat:var seat}||seat!=f.OwnerSeat||_cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==ZhenyiGainReason&&r.Cards.Contains(m.CardId))!=r.Cards.Count))throw new InvalidOperationException("Gouchen needs one exact already-paid three-category gain invoice.");
 }

 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 300)]
 private void AssertRegisteredZhangQiYing(ProgramSkillFrame frame)
 {
     if (frame.ZhangQiYing is not null) AssertZhangQiYing(frame);
 }
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 260)]
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 260)]
 private bool CanContinueZhangQiYing(ProgramSkillFrame f){if(f.ZhangQiYing is not{Stage:"gain-children"})return false;AssertZhangQiYing(f);return true;}
 [ProgramContinuation(ProgramContinuationKind.Resume,24405)] private bool ResumeZhangQiYing(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{ZhangQiYing:{Stage:"gain-children"}}f||f.Id!=id)return false;AssertZhangQiYing(f);
  if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id))return true;
  ReplaceRuntimeTop(f=f with{ZhangQiYing=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,24405)] private bool ReturnZhangQiYingMovement(ProgramSkillFrame f)=>CanContinueZhangQiYing(f)&&ResumeZhangQiYing(f.Id);
 [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 260)]
 private bool IsZhangQiYingMovement(ProgramSkillFrame f,SkillProgramEffect?e,ProgramMovementContinuation p)=>e?.Op==SkillProgramEffectOp.GainThreeDeckCategoriesAfterElementalDamage&&CanContinueZhangQiYing(f)&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
 private bool ZhangQiYingFirstChild(ProgramSkillFrame f,ResolutionFrame child)=>f.ZhangQiYing is{Stage:"gain-children"}r&&CanContinueZhangQiYing(f)&&child is CardsMovedTriggerWindowFrame moved&&moved.Batch.ParentFrameId==f.Id&&moved.Batch.AwaitingProgramFrameId==f.Id&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After&&r.Cards.Contains(m.CardId)&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==ZhenyiGainReason);
 private ProgramSkillFrame? ZhangQiYingObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.ZhangQiYingFirstChild(r,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
 [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 220)]
 [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 330)]
 private bool HasZhangQiYingDamageObserver(long id)=>ZhangQiYingObserverRoot()is not null&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame);
 [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 420)]
 private bool HasZhangQiYingBeforeDamageObserver(long id)=>ZhangQiYingObserverRoot()is not null&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame);
 [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 190)]
 private bool IsZhangQiYingDying()=>ActiveDying is not null&&ZhangQiYingObserverRoot()is not null;
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 330)]
 private bool TryAdvanceZhangQiYingSubtree()=>_pendingDecision is null&&ZhangQiYingObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
 [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 560)]
 private bool AllowsZhangQiYingNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
 {
  if(sourceLess||f.AttackAttempt is not null||f.InstructionIndex<1||_resolutionStack.LastOrDefault()?.Id!=f.Id||ZhangQiYingObserverRoot()is null||f.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged))return false;
  var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is{}r?ResolveProgramParticipant(f,r):ResolveProgramEffectTarget(f,e.Target));
 }
}
