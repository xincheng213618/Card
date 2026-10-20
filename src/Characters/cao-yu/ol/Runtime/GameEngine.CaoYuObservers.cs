namespace CardGame.Core;
public sealed partial class GameEngine
{
 private static bool CaoYuChildren(CaoYuStage stage) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_cao_yu_ol_CaoYuObservers.CaoYuChildren(stage);
 private bool CaoYuParent(ProgramSkillFrame f)
 {
  if(f.CaoYu is not{}r||f.WindowContext is not{}c||c.ParentFrameId!=r.WindowId)return false;var i=_resolutionStack.FindIndex(x=>x.Id==f.Id);if(i<1||_resolutionStack[i-1].Id!=r.WindowId)return false;
  if(r.Op==SkillProgramEffectOp.ResolveXiangxuIssuedTurnEndAdjustment)return IsIssuedXiangxuEndingCandidate(new(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,0,c.OccurrenceIndex),c)&&c.CaoYuOpportunityId==r.OpportunityId;
  if(r.Op==SkillProgramEffectOp.ResolveXiangzuDyingGiftAndRecovery)return CaoYuDyingEntry(f,out var dying)&&dying.Id==r.DyingId;
  return r.Op==SkillProgramEffectOp.ResolveGongjieRoundFirstGifts&&c.Window==SkillProgramTriggerWindow.RoundStarting&&_resolutionStack[i-1]is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.RoundStarting}w&&w.OwnerSeat==r.TurnOwner&&w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&MountObserverCandidateMatches(f,w.Candidates[w.CandidateIndex])&&ProgramEventHistory<RoundStartedEvent>().Any(e=>e.RoundNumber==r.Round&&e.ActorSeat==r.TurnOwner);
 }
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27440)]
 private void AssertCaoYu(ProgramSkillFrame f)
 {
  if(f.CaoYu is not{}r)return;
  if(f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||r.Source!=CaoYuSource(f)||r.GameplayHash!=f.GameplayHash||!CaoYuComposition.Handles(r.Op)||GetPausedPrivateOfferEffect(f)?.Op!=r.Op||!CaoYuParent(f)||r.ActualTurn!=_turnNumber||r.TurnOwner!=_turnProgression.OwnerSeat||r.Index<0||r.Index>r.Targets.Count||r.Requested<0||r.Recovery<0||r.Before<0||r.After<r.Before||r.After>_movementSequence||r.Count<0||!Enum.IsDefined(r.Stage)||r.Targets.Distinct().Count()!=r.Targets.Count||r.Selected.Distinct().Count()!=r.Selected.Count||r.Materials.Select(c=>c.CardId).Distinct().Count()!=r.Materials.Count||r.GrainIds.Distinct().Count()!=r.GrainIds.Count||ProgramEventHistory<CaoYuStartedEvent>().Count(e=>e==new CaoYuStartedEvent(f.Id,r.Source,r.GameplayHash,r.Op,r.WindowId,r.ActualTurn,r.TurnOwner,r.Round,r.OpportunityId,r.DyingId))!=1)throw new InvalidOperationException("CaoYu lost its exact owning instruction, original source/window, scalar start receipt or actual turn.");
  if(r.Op==SkillProgramEffectOp.ResolveGongjieRoundFirstGifts)
  {
   if(f.SkillId!="ol:gongjie"||f.TriggerId!="round-first"||r.OpportunityId!=0||r.DyingId!=0||r.Targets.Any(s=>s==f.OwnerSeat)||r.Stage is not(CaoYuStage.GongjieTargets or CaoYuStage.GongjieTake or CaoYuStage.GongjieGiftChildren or CaoYuStage.GongjieDrawChildren)||ProgramEventHistory<CaoYuStartedEvent>().Count(e=>e.Source.OwnerSeat==f.OwnerSeat&&e.Source.SkillId==f.SkillId&&e.Round==r.Round)!=1)throw new InvalidOperationException("Gongjie lost its round-first participant scope.");
   var choices=new List<int>();foreach(var e in ProgramEventHistory<CaoYuTargetChoiceEvent>().Where(e=>e.FrameId==f.Id).OrderBy(e=>e.Sequence)){if(e.Selected){if(choices.Contains(e.Seat))throw new InvalidOperationException("Gongjie repeated a selected person.");choices.Add(e.Seat);}else if(!choices.Remove(e.Seat))throw new InvalidOperationException("Gongjie removed an unselected person.");}
   if(r.Stage==CaoYuStage.GongjieTargets?!choices.SequenceEqual(r.Targets):!ProgramEventHistory<CaoYuTargetFrozenEvent>().Where(e=>e.FrameId==f.Id).OrderBy(e=>e.Index).Select(e=>e.Seat).SequenceEqual(r.Targets)||!choices.Order().SequenceEqual(r.Targets.Order()))throw new InvalidOperationException("Gongjie changed its selected or frozen native action-order participants.");
  }
  else if(r.Op==SkillProgramEffectOp.ResolveXiangxuIssuedTurnEndAdjustment)
  {
   if(f.SkillId!="ol:xiangxu"||r.OpportunityId<1||r.DyingId!=0||r.Stage is not(CaoYuStage.XiangxuChoice or CaoYuStage.XiangxuDiscardChoice or CaoYuStage.XiangxuDrawChildren or CaoYuStage.XiangxuDiscardChildren or CaoYuStage.XiangxuRecoveryChildren))throw new InvalidOperationException("Xiangxu lost its issued independent ending opportunity.");
   if(r.Paid){var p=ProgramEventHistory<CaoYuXiangxuComparisonEvent>().SingleOrDefault(e=>e.FrameId==f.Id);if(p is null||p.OpportunityId!=r.OpportunityId||p.ReferenceSeat!=r.TurnOwner||p.Requested!=r.Requested||p.Requested!=(p.Branch=="discard"?p.OwnHand-p.ReferenceHand:Math.Max(0,Math.Min(5,p.ReferenceHand)-p.OwnHand))||ProgramEventHistory<CaoYuOpportunityConsumedEvent>().Count(e=>e.FrameId==f.Id&&e.OpportunityId==r.OpportunityId&&e.ResolverSource==r.Source&&e.Adjust)!=1)throw new InvalidOperationException("Xiangxu changed its exact late comparison or consumed the original opportunity twice.");}
  }
  else if(f.SkillId!="ol:xiangzu"||f.TriggerId!="dying-gift"||r.DyingId<1||r.OpportunityId!=0||r.Stage is not(CaoYuStage.XiangzuRecipient or CaoYuStage.XiangzuCards or CaoYuStage.XiangzuGiftChildren or CaoYuStage.XiangzuRecoveryChildren)||r.Paid&&(ProgramEventHistory<CaoYuXiangzuSpentEvent>().Where(e=>e.Source.OwnerSeat==f.OwnerSeat&&e.Source.SkillId==f.SkillId).ToArray()is not[var spent]||spent.FrameId!=f.Id||spent.Source!=r.Source||spent.GameplayHash!=r.GameplayHash||spent.DyingId!=r.DyingId||spent.RecipientSeat!=r.Target||spent.Count<1||spent.HistoryQualified!=r.HistoryQualified))throw new InvalidOperationException("Xiangzu lost its permanent limited spend, genuine dying occurrence or original recipient/history qualification.");
  var children=CaoYuChildren(r.Stage);if(children?f.PendingMovementContinuation is not{SubjectSeat:var seat,BeforeCount:0,CoverageResultBind:null}||seat!=f.OwnerSeat:f.PendingMovementContinuation is not null)throw new InvalidOperationException("CaoYu lost its own native movement/recovery return.");
  if(children)
  {
   if(ProgramEventHistory<CaoYuMovementPaidEvent>().Count(e=>e==new CaoYuMovementPaidEvent(f.Id,r.Stage,r.Index,f.OwnerSeat,r.Before,r.After,r.Count))!=1||_cardMovements.Where(m=>m.Sequence>r.Before&&m.Sequence<=r.After).Any(m=>!IsCaoYuIssuedMovement(f,m)))throw new InvalidOperationException("CaoYu lost its exact paid movement interval, primary cards or native spill/shuffle.");
   var frozen=ProgramEventHistory<CaoYuMaterialFrozenEvent>().Where(e=>e.FrameId==f.Id&&e.Stage==r.Stage&&e.PaymentIndex==r.Index).OrderBy(e=>e.Index).ToArray();if(frozen.Length!=r.Materials.Count||frozen.Where((e,i)=>e.Index!=i||e.CardId!=r.Materials[i].CardId||e.Kind!=r.Materials[i].Kind||e.From!=r.Materials[i].From||e.Suit!=r.Materials[i].Suit).Any())throw new InvalidOperationException("CaoYu replaced a frozen original payment entity.");
   foreach(var card in r.Materials)if(ProgramEventHistory<CaoYuCardPaidEvent>().Count(e=>e.FrameId==f.Id&&e.Stage==r.Stage&&e.Index==r.Index&&e.CardId==card.CardId&&e.Kind==card.Kind&&e.From==card.From&&e.Suit==card.Suit&&e.Sequence>r.Before&&e.Sequence<=r.After&&_cardMovements.Any(m=>m.Sequence==e.Sequence&&m.CardId==e.CardId&&m.From==e.From&&m.To==e.To&&IsCaoYuIssuedMovement(f,m)))!=1)throw new InvalidOperationException("CaoYu's real primary card payment is missing or repeated.");
   if(!ProgramEventHistory<CaoYuOxGrainFrozenEvent>().Where(e=>e.FrameId==f.Id&&e.Stage==r.Stage&&e.PaymentIndex==r.Index).OrderBy(e=>e.Index).Select(e=>e.CardId).SequenceEqual(r.GrainIds))throw new InvalidOperationException("CaoYu changed its independently frozen native Ox spill.");
   if(r.Stage is CaoYuStage.XiangxuRecoveryChildren or CaoYuStage.XiangzuRecoveryChildren&&ProgramEventHistory<CaoYuRecoveryIssuedEvent>().Count(e=>e==new CaoYuRecoveryIssuedEvent(f.Id,r.Stage,r.Recovery))!=1)throw new InvalidOperationException("CaoYu cannot duplicate its paid native recovery.");
  }
  if(_resolutionStack.LastOrDefault()?.Id==f.Id&&_pendingDecision is{}prompt&&!children&&(prompt.PlayerSeat!=(r.Stage==CaoYuStage.GongjieTake?r.Targets[r.Index]:f.OwnerSeat)||!AssistedChoicesEqual(prompt.Choices,CaoYuChoices(f))))throw new InvalidOperationException("CaoYu changed the real chooser's frozen public/private frontier.");
 }
 private bool IsCaoYuIssuedMovement(ProgramSkillFrame f,CardMovementRecord m)
 {
  var r=f.CaoYu!;if(!_cardMovements.Contains(m)||m.Sequence<=r.Before||m.Sequence>r.After)return false;
  if(r.Stage is CaoYuStage.GongjieDrawChildren or CaoYuStage.XiangxuDrawChildren)return m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==(r.Stage==CaoYuStage.GongjieDrawChildren?GongjieDrawReason:XiangxuDrawReason)||m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile&&m.Reason==CardMoveReasons.Reshuffle;
  var reason=r.Stage switch{CaoYuStage.GongjieGiftChildren=>GongjieGiftReason,CaoYuStage.XiangxuDiscardChildren=>XiangxuDiscardReason,CaoYuStage.XiangzuGiftChildren=>XiangzuGiftReason,_=>null};if(reason is null)return false;
  return r.Materials.Any(c=>c.CardId==m.CardId&&c.From==m.From&&m.To==(r.Stage==CaoYuStage.XiangxuDiscardChildren?CardLocation.DiscardPile:CardLocation.Hand(r.Target!.Value))&&m.Reason.Value==reason)||r.GrainIds.Contains(m.CardId)&&m.From==CardLocation.WoodenOxGrain(f.OwnerSeat)&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard&&r.Materials.Any(c=>c.Kind==CardKind.WoodenOx&&c.From==CardLocation.Equipment(f.OwnerSeat));
 }
 [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 370)]
 private bool IsCaoYuMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>f.CaoYu is{}r&&CaoYuChildren(r.Stage)&&p==f.PendingMovementContinuation&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null&&e is not null&&e.Op==r.Op;
 [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 170)]
 [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 160)]
 private bool IsCaoYuRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)
 {
  if(f.CaoYu is not{}r||!RecoveryReplacementFrameRidesOn(recovery,f)||recovery.Return.Continuation!=PostEventContinuation.AwaitedProgramMovement)return false;
  return r.Stage is CaoYuStage.XiangxuRecoveryChildren or CaoYuStage.XiangzuRecoveryChildren&&recovery.Attempt.SourceSeat==f.OwnerSeat&&recovery.Attempt.TargetSeat==f.OwnerSeat&&recovery.Attempt.Amount==r.Recovery&&recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.Program&&recovery.Attempt.Completion.InstructionIndex==1||r.Stage is CaoYuStage.GongjieGiftChildren or CaoYuStage.XiangzuGiftChildren&&r.Materials.Any(c=>c.Kind==CardKind.SilverLion&&c.From==CardLocation.Equipment(f.OwnerSeat))&&recovery.Attempt.SourceSeat==f.OwnerSeat&&recovery.Attempt.TargetSeat==f.OwnerSeat&&recovery.Attempt.Amount==1&&recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&recovery.Attempt.Completion.MoveReason?.Value==(r.Stage==CaoYuStage.GongjieGiftChildren?GongjieGiftReason:XiangzuGiftReason);
 }
 private bool CaoYuFirstChild(ProgramSkillFrame f,ResolutionFrame child)
 {
  if(f.CaoYu is not{}r||!CaoYuChildren(r.Stage))return false;AssertCaoYu(f);
  if(child is RecoveryReplacementFrame recovery)return IsCaoYuRecoveryParent(f,recovery);
  if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement;
  if(child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}changed)return changed.ResumeProgramFrameId==f.Id&&changed.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
  if(child is ProgramLifecycleTriggerWindowFrame state&&state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange)return state.ResumeProgramFrameId==f.Id&&ProgramEventHistory<CharacterStateChangedEvent>().Any(e=>e.Change.Id==state.Id&&e.Change.ParentFrameId==f.Id);
  return child is CardsMovedTriggerWindowFrame moved&&ProgramMovementReturnMatches(f,moved)&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>IsCaoYuIssuedMovement(f,m));
 }
 private ProgramSkillFrame? CaoYuObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.CaoYuFirstChild(f,c),static(e,i)=>e.HasObserverSuffix(i,static(g,n)=>g.IsMovementObserverEdge(n)||g.IsYanPuDyingExitEdge(n)||g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n],g._resolutionStack[n-1]),static(g,n,d)=>g.IsSuspendedOrPaidRescueObserverSuffix(n,d)));
 [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 880)]
 [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 990)]
 private bool HasCaoYuDamageObserver(long id)=>CaoYuObserverRoot()is{}root&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1020)]
 private bool HasCaoYuBeforeDamageObserver(long id)=>CaoYuObserverRoot()is{}root&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 850)]
 private bool IsCaoYuDying()=>ActiveDying is{}d&&CaoYuObserverRoot()is{}root&&(root.CaoYu!.DyingId==d.Id&&CaoYuDyingEntry(root,out var original)&&original.Id==d.Id||_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id));
 [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1420)]
 private bool AllowsCaoYuNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess){if(f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||CaoYuObserverRoot()is not{}root||root.Id==f.Id)return false;var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));}
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27440)]private bool AdvanceCaoYuSubtree()=>_pendingDecision is null&&CaoYuObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
