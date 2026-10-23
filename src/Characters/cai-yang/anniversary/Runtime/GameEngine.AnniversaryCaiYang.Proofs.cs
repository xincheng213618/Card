namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool ValidCaiYangDuelUse(CardUseFrame use,ProgramSelectedActorDuelOrigin native)
 {
  if(native.CaiYangXunji is not{}o||use.SelectedActorDuelOrigin!=native||use.CardId!=0||use.CardKind!=CardKind.Duel||use.PhysicalCardIds is not{Count:0}||
   use.Action is not{Type:CardActionType.Use,EffectiveKind:CardKind.Duel,PhysicalCards.Count:0,ConversionChain.Count:0,EffectiveSuit:Suit.None,EffectiveRank:0}a||
   use.Id!=o.UseId||a.ActionId!=o.ActionId||a.ProviderSeat!=o.Source.OwnerSeat||native.OrderedPairPayment is not null||native.ParticipantMode is not null||native.MouDiaoChanRing is not null||native.XiahouEnShiwu is not null||
   native.ParentProgramFrameId!=o.FrameId||native.InstructionIndex!=o.Instruction||native.OwnerSeat!=o.Source.OwnerSeat||native.InitialActorSeat!=o.Source.OwnerSeat||native.InitialTargetSeat!=o.Target||
   native.SkillId!=o.Source.SkillId||native.ActivationId!=o.Source.BindingId||native.SkillInstanceId!=o.Source.SkillInstanceId||native.GameplayHash!=o.Hash||native.TurnNumber!=o.ActualTurn||o.ActualTurn!=_turnNumber||
   o.Source.SkillId!=AnniversaryCaiYangComposition.Xunji||o.Source.BindingId!=AnniversaryCaiYangComposition.Ending||ProgramEventHistory<CaiYangXunjiIssuedEvent>().Count(e=>e.Origin==o)!=1)return false;
  var i=_resolutionStack.FindIndex(f=>f.Id==use.Id);
  return i>0&&_resolutionStack[i-1]is ProgramSkillFrame{AnniversaryCaiYang:{Stage:AnniversaryCaiYangStage.Duel,Duel:{}bound}}p&&p.Id==o.FrameId&&bound==o&&p.InstructionIndex==o.Instruction&&
   p.OwnerSeat==o.Source.OwnerSeat&&p.SkillId==o.Source.SkillId&&p.SkillInstanceId==o.Source.SkillInstanceId&&p.GameplayHash==o.Hash&&p.WindowContext is{}w&&CaiYangEndingWitness(CaiYangCandidate(p),w,true)is{FrameId:var grant}&&grant==o.GrantId;
 }
 private void AssertCaiYangXunjiDuelUse(CardUseFrame use,ProgramSelectedActorDuelOrigin origin)
 {if(!ValidCaiYangDuelUse(use,origin))throw new InvalidOperationException("Xunji Duel lost its original issued next-turn obligation, native empty-material Action or true ending parent.");}
 private CaiYangXunjiOrigin? CaiYangDuelDamage(int owner,ProgramSkillWindowContext w)
 {
  if(w.Window!=SkillProgramTriggerWindow.AfterDamageApplied||w.Amount<=0||w.SourceSeat!=owner||w.TargetSeat is not{}target||w.DamageFrameId is not{}damageId||
   CurrentDamageAttempt is not{IsSourceLess:false,DamageWasApplied:true,EffectiveCardKind:CardKind.Duel}a||a.SourceSeat!=owner||a.TargetSeat!=target||
   LifecycleCardUse(a.ResolutionId)is not{SelectedActorDuelOrigin:{CaiYangXunji:{}o}native}use||o.Source.OwnerSeat!=owner||o.Target!=target||!ValidCaiYangDuelUse(use,native)||
   _resolutionStack.OfType<DamageFrame>().SingleOrDefault(x=>x.Id==damageId)is not{}d||d.ParentFrameId!=use.Id||d.SourceSeat!=owner||d.TargetSeat!=target||d.Amount!=w.Amount||
   ProgramEventHistory<CaiYangXunjiDamageBoundEvent>().Any(e=>e.UseId==o.UseId&&e.DamageId==damageId))return null;
  return o;
 }
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState,45600)] private void RequireCaiYang(ProgramSkillFrame f)
 {
  if(f.AnniversaryCaiYang is not{}r)return;
  var i=_resolutionStack.FindIndex(p=>p.Id==f.Id);
  if(f.InstructionIndex!=1||r.Source!=CaiYangSource(f)||r.Hash!=f.GameplayHash||r.ActualTurn!=_turnNumber||f.WindowContext is not{}w||w.ParentFrameId!=r.ParentId||
   i<1||_resolutionStack[i-1].Id!=r.ParentId||!Enum.IsDefined(r.Stage)||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0)
   throw new InvalidOperationException("Cai Yang lost his original skill instance, actual turn or owning native window.");
  if(r.FirstDamage is{}first)
  {
   if(r.Duel is not null||f.SkillId!=AnniversaryCaiYangComposition.Jiaofeng||f.TriggerId!=AnniversaryCaiYangComposition.First||r.Source!=first.Source||r.ParentId!=first.WindowId||!ValidCaiYangFirstOrigin(first)||
    r.Stage is not(AnniversaryCaiYangStage.Drawing or AnniversaryCaiYangStage.DrawChildren or AnniversaryCaiYangStage.RecoveryChildren)||
    _resolutionStack[i-1]is not BeforeDamageProgramWindowFrame{CaiYangJiaofeng:{}bound}p||bound!=first||p.ParentFrameId!=first.ParentId||p.SourceSeat!=f.OwnerSeat||p.TargetSeat!=first.Target||p.Amount!=first.OriginalAmount||
    p.CandidateIndex<0||p.CandidateIndex>=p.Candidates.Count||!MountObserverCandidateMatches(f,p.Candidates[p.CandidateIndex].Candidate)||
    ProgramEventHistory<CaiYangJiaofengStartedEvent>().Count(e=>e==new CaiYangJiaofengStartedEvent(f.Id,first))!=1||
    (r.Stage==AnniversaryCaiYangStage.Drawing)!=(r.Draw is null)||r.RecoveryIssued!=(r.Stage==AnniversaryCaiYangStage.RecoveryChildren)||r.RecoveryIssued&&first.LostHp<=2)
    throw new InvalidOperationException("Jiaofeng lost its frozen original first damage and single trigger-time lost-HP tiers.");
   if(r.Draw is{}d&&(d.RecipientSeat!=f.OwnerSeat||d.RequestedCount!=1||d.SequenceBefore<0||d.SequenceAfter<d.SequenceBefore||d.SequenceAfter>_movementSequence||d.ActualDrawIds.Count>1||
    ProgramEventHistory<CaiYangJiaofengDrawReturnedEvent>().Count(e=>e==new CaiYangJiaofengDrawReturnedEvent(f.Id,d.ActualDrawIds.Count,d.AttemptFrameId,d.SequenceBefore,d.SequenceAfter))!=1||
    d.ActualDrawIds.Any(id=>_cardMovements.Count(m=>m.CardId==id&&m.Sequence>d.SequenceBefore&&m.Sequence<=d.SequenceAfter&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason==CaiYangJiaofengDrawReason)!=1)))
    throw new InvalidOperationException("Jiaofeng lost its exact typed native draw return or once-paid actual materials.");
   if(r.RecoveryIssued&&ProgramEventHistory<CaiYangJiaofengRecoveryIssuedEvent>().Count(e=>e==new CaiYangJiaofengRecoveryIssuedEvent(f.Id,1))!=1)
    throw new InvalidOperationException("Jiaofeng cannot repeat its frozen third-tier recovery.");
   return;
  }
  if(r.Duel is not{}o||o.Source.SkillId!=AnniversaryCaiYangComposition.Xunji||o.Hash!=f.GameplayHash||o.Source.OwnerSeat!=f.OwnerSeat||o.Source.SkillInstanceId!=f.SkillInstanceId||
   ProgramEventHistory<CaiYangXunjiIssuedEvent>().Count(e=>e.Origin==o)!=1)throw new InvalidOperationException("Xunji lost its original actual Duel invoice.");
  if(r.Stage==AnniversaryCaiYangStage.Duel)
  {
   if(f.TriggerId!=AnniversaryCaiYangComposition.Ending||o.FrameId!=f.Id||o.ParentId!=r.ParentId||CaiYangEndingWitness(CaiYangCandidate(f),w,true)is not{}g||g.FrameId!=o.GrantId||r.Draw is not null||r.RecoveryIssued)
    throw new InvalidOperationException("Xunji lost its once-consumed original next-turn ending.");
   return;
  }
  if(r.Stage is not(AnniversaryCaiYangStage.Damage or AnniversaryCaiYangStage.DamageReturned)||f.TriggerId!=AnniversaryCaiYangComposition.Retaliate||w.Window!=SkillProgramTriggerWindow.AfterDamageApplied||w.SourceSeat!=f.OwnerSeat||
   w.DamageFrameId!=r.DamageId||w.TargetSeat!=r.DamageTarget||w.Amount!=r.DamageAmount||r.DamageTarget!=o.Target||r.DamageAmount<=0||
   _resolutionStack[i-1]is not DamageTriggerWindowFrame pDamage||pDamage.ParentFrameId!=r.DamageId||pDamage.CandidateIndex<0||pDamage.CandidateIndex>=pDamage.Candidates.Count||!MountObserverCandidateMatches(f,pDamage.Candidates[pDamage.CandidateIndex].ToProgramCandidate())||
   _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d=>d.Id==r.DamageId)is not{}original||original.ParentFrameId!=o.UseId||original.SourceSeat!=f.OwnerSeat||original.TargetSeat!=r.DamageTarget||original.Amount!=r.DamageAmount||
   ProgramEventHistory<CaiYangXunjiDamageBoundEvent>().Count(e=>e==new CaiYangXunjiDamageBoundEvent(f.Id,o.UseId,r.DamageId,f.OwnerSeat,r.DamageTarget,r.DamageAmount))!=1)
   throw new InvalidOperationException("Xunji retaliation lost the actual original Duel damage and reverse real damage participants.");
 }
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,45600)]
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,45600)] private bool KeepCaiYangIssued(ProgramSkillFrame f)
 {if(f.AnniversaryCaiYang is not null){RequireCaiYang(f);return true;}return f.InstructionIndex is 0 or 1&&f.WindowContext is{}w&&AdmitCaiYangXunji(CaiYangCandidate(f),w);}
 private bool CaiYangFirstChild(ProgramSkillFrame f,ResolutionFrame child)
 {
  if(f.AnniversaryCaiYang is not{}r)return false;RequireCaiYang(f);
  if(r.Stage==AnniversaryCaiYangStage.Drawing&&NativeDrawAttemptRidesOn(child,f))return true;
  if(child is CardsMovedTriggerWindowFrame moved)return r.Stage==AnniversaryCaiYangStage.DrawChildren&&r.Draw is{}d&&moved.Batch.ParentFrameId==f.Id&&
   (moved.ResumeProgramFrameId==f.Id||moved.Batch.AwaitingProgramFrameId==f.Id)&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&
   moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>d.SequenceBefore&&m.Sequence<=d.SequenceAfter);
  if(child is HpChangedTriggerWindowFrame hp)return r.Stage==AnniversaryCaiYangStage.RecoveryChildren&&r.RecoveryIssued&&hp.ResumeFrameId==f.Id&&hp.Change.ParentFrameId==f.Id&&hp.Continuation==PostEventContinuation.Program&&hp.Change.TargetSeat==f.OwnerSeat;
  if(child is RecoveryReplacementFrame recovery)return r.Stage==AnniversaryCaiYangStage.RecoveryChildren&&r.RecoveryIssued&&recovery.ParentFrameId==f.Id;
  if(child is DamageFrame damage)return r.Stage==AnniversaryCaiYangStage.Damage&&f.AttackAttempt is{}a&&damage.ParentFrameId==f.Id&&damage.SourceSeat==r.DamageTarget&&damage.TargetSeat==f.OwnerSeat&&a.SourceSeat==r.DamageTarget&&a.TargetSeat==f.OwnerSeat;
  return child is ProgramLifecycleTriggerWindowFrame state&&state.ResumeProgramFrameId==f.Id&&state.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
 }
 private ProgramSkillFrame? CaiYangObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.CaiYangFirstChild(r,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
 [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,45600)]private bool HasCaiYangBeforeDamageObserver(long id)=>CaiYangObserverRoot()is{AnniversaryCaiYang.FirstDamage:not null}r&&r.WindowContext?.ParentFrameId==id;
 [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,45600)]private bool HasCaiYangPaidDamageObserver(long id)=>CaiYangObserverRoot()is{AnniversaryCaiYang.Stage:AnniversaryCaiYangStage.Damage}r&&r.WindowContext?.ParentFrameId==id;
 [ProgramDyingProof(ProgramDyingProofPhase.Invariant,45600,group:10)]
 [ProgramDyingProof(ProgramDyingProofPhase.Runtime,45600)]private bool HasCaiYangObserverDying()=>ActiveDying is{}dying&&CaiYangObserverRoot()is{}r&&_resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,45600)]private bool AdvanceCaiYangObserver()=>_pendingDecision is null&&CaiYangObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
