namespace CardGame.Core;

public sealed partial class GameEngine
{
 private bool QiaoRuiOriginal(ProgramSkillFrame f, SkillProgramEffectOp op)
 {
  if (_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.ActivationId != f.TriggerId || f.WindowContext is not { } w || w.OwnerSeat != f.OwnerSeat || !OriginalHandBindingStarted(f, w.Window)) return false;
  var index = _resolutionStack.FindIndex(x => x.Id == f.Id); if (index < 1 || _resolutionStack[index - 1].Id != w.ParentFrameId) return false;
  if (op == SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouDraw)
   return f.SkillId == AnniversaryQiaoRuiComposition.Aishou && f.TriggerId == "ending-marked-draw" && w.Window == SkillProgramTriggerWindow.TurnEnding && w.SourceSeat == f.OwnerSeat && w.TargetSeat == f.OwnerSeat && _resolutionStack[index - 1] is TurnEndingBoundaryFrame ending && ending.OwnerSeat == f.OwnerSeat && ending.TurnNumber == _turnNumber && ending.ItemIndex >= 0 && ending.ItemIndex < ending.Items.Count && ending.Items[ending.ItemIndex].Candidate is { } candidate && MountObserverCandidateMatches(f, candidate);
  if (op == SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouPrepare)
   return f.SkillId == AnniversaryQiaoRuiComposition.Aishou && f.TriggerId == "prepare-marked-discard" && ExactOriginalHandLifecycleParent(f, SkillProgramTriggerWindow.TurnStartBeforeNormalFlow);
  if (op == SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouLastLoss)
   return f.SkillId == AnniversaryQiaoRuiComposition.Aishou && f.TriggerId == "outside-last-mark-loss" && w.Window == SkillProgramTriggerWindow.CardsMoved && w.MovementBatch is { } b && _resolutionStack[index - 1] is CardsMovedTriggerWindowFrame moved && moved.Batch.Id == b.Id && moved.Batch.Movements.SequenceEqual(b.Movements) && moved.CandidateIndex >= 0 && moved.CandidateIndex < moved.Candidates.Count && MountObserverCandidateMatches(f, moved.Candidates[moved.CandidateIndex]);
  return op == SkillProgramEffectOp.ResolveAnniversaryQiaoRuiSaowei && f.SkillId == AnniversaryQiaoRuiComposition.Saowei && f.TriggerId == "completed-other-slash" && QiaoRuiOriginalSlash(w) is { Action: { } a } original && a.ActorSeat != f.OwnerSeat && _resolutionStack[index - 1] is ProgramCardTriggerWindowFrame card && card.ParentFrameId == original.Id && card.CandidateIndex >= 0 && card.CandidateIndex < card.Candidates.Count && MountObserverCandidateMatches(f, ToSharedCandidate(card.Candidates[card.CandidateIndex]));
 }
 private bool ValidQiaoRui(ProgramSkillFrame f)
 {
  if (f.AnniversaryQiaoRui is not { } r || f.InstructionIndex != 1 || !QiaoRuiOriginal(f, r.Operation) || r.Source != QiaoRuiSource(f) || r.Hash != f.GameplayHash || r.ParentId != f.WindowContext!.ParentFrameId || r.Turn != _turnNumber || !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.Requested < 0 || r.Cards.Any(id => id <= 0) || r.Cards.Distinct().Count() != r.Cards.Count || r.NativeBatches.Distinct().Count() != r.NativeBatches.Count ||
   ProgramEventHistory<AnniversaryQiaoRuiStartedEvent>().Count(e => e == new AnniversaryQiaoRuiStartedEvent(f.Id, r.Source, r.Hash, r.Operation, r.ParentId, r.Turn)) != 1) return false;
  if (r.Operation == SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouDraw)
  {
   if (r.Cards.Count != 0 || r.Plan is not null || r.UseId != 0 || r.ActionId != 0 || r.Damaged || r.Returned || r.NativeBatches.Count != 0 || r.Stage is not (AnniversaryQiaoRuiStage.Drawing or AnniversaryQiaoRuiStage.DrawChildren or AnniversaryQiaoRuiStage.Completed)) return false;
   if (r.Draw is { } d && (d.RecipientSeat != f.OwnerSeat || d.RequestedCount != r.Requested || d.SequenceBefore != r.Before || d.SequenceAfter != r.After || ProgramEventHistory<AnniversaryQiaoRuiDrawReturnedEvent>().Count(e => e == new AnniversaryQiaoRuiDrawReturnedEvent(f.Id, r.Requested, d.ActualDrawIds.Count, r.Before, r.After)) != 1)) return false;
  }
  else if (r.Operation is SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouPrepare or SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouLastLoss)
  {
   if (r.Draw is not null || r.Requested != 0 || r.Plan is not null || r.UseId != 0 || r.ActionId != 0 || r.Returned || r.Damaged || r.NativeBatches.Count != 0 || r.Stage is not (AnniversaryQiaoRuiStage.DiscardChildren or AnniversaryQiaoRuiStage.HealthChildren or AnniversaryQiaoRuiStage.Completed)) return false;
   if (r.Operation == SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouLastLoss && (r.Cards.Count != 0 || r.Stage == AnniversaryQiaoRuiStage.DiscardChildren)) return false;
   if (r.Cards.Any(id => !QiaoRuiMarkedHand(f.OwnerSeat, r.Before).Contains(id))) return false;
  }
  else
  {
   if (r.Operation != SkillProgramEffectOp.ResolveAnniversaryQiaoRuiSaowei || r.Draw is not null || r.Requested != 0 || r.Cards.Count != 0 || r.Stage is AnniversaryQiaoRuiStage.Drawing or AnniversaryQiaoRuiStage.DrawChildren or AnniversaryQiaoRuiStage.DiscardChildren or AnniversaryQiaoRuiStage.HealthChildren) return false;
   if (r.Stage == AnniversaryQiaoRuiStage.SlashChoice && (r.Plan is not null || r.UseId != 0 || r.ActionId != 0 || r.Returned || r.Damaged || r.NativeBatches.Count != 0)) return false;
   if (r.Plan is { } p && (p.CardId <= 0 || p.From != CardLocation.Hand(f.OwnerSeat) || p.Targets.Count == 0 || p.Targets.Any(t => !IsValidPlayerSeat(t) || t == f.OwnerSeat) || p.Targets.Distinct().Count() != p.Targets.Count || QiaoRuiOriginalSlash(f.WindowContext) is not { Action: { } original } || p.Targets.Any(t => !original.TargetSeats.Contains(t)))) return false;
   if (r.Stage is AnniversaryQiaoRuiStage.Issuing or AnniversaryQiaoRuiStage.NativeSlash or AnniversaryQiaoRuiStage.NativeTail or AnniversaryQiaoRuiStage.ClaimChildren && r.Plan is null) return false;
   if (r.UseId != 0 && (r.UseId <= f.Id || r.ActionId <= 0 || r.Plan is not { } plan || ProgramEventHistory<AnniversaryQiaoRuiUseIssuedEvent>().Count(e => e.Origin == new AnniversaryQiaoRuiUseOrigin(f.Id, r.Source, r.Hash, plan.CardId, plan.From, r.UseId, r.ActionId)) != 1)) return false;
   if (r.Returned && ProgramEventHistory<AnniversaryQiaoRuiUseReturnedEvent>().Count(e => e == new AnniversaryQiaoRuiUseReturnedEvent(f.Id, r.UseId, r.ActionId)) != 1) return false;
   if (r.Damaged != ProgramEventHistory<AnniversaryQiaoRuiDamageEvent>().Any(e => e.FrameId == f.Id && e.UseId == r.UseId && e.Amount > 0)) return false;
  }
  return true;
 }
 private bool HasAnniversaryQiaoRuiPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p && _resolutionStack.LastOrDefault() is ProgramSkillFrame { Step: ResolutionFrameStep.AwaitingResponse, AnniversaryQiaoRui.Stage: AnniversaryQiaoRuiStage.SlashChoice } f && p.PlayerSeat == f.OwnerSeat && p.TargetSeat == f.OwnerSeat && p.SourceSeat == f.OwnerSeat && ValidQiaoRui(f) && AssistedChoicesEqual(p.Choices, QiaoRuiChoices(f));
 private bool HasQiaoRuiCompletedUseObserver(long parentUseId)
 {
  var at = _resolutionStack.FindIndex(x => x.Id == parentUseId);
  if (at < 0 || at + 2 >= _resolutionStack.Count || _resolutionStack[at] is not CardUseFrame { Step: ResolutionFrameStep.Completed, Action: { Type: CardActionType.Use } original } use || !IsSlashCard(use.CardKind) ||
   _resolutionStack[at + 1] is not ProgramCardTriggerWindowFrame { Continuation: ProgramCardContinuation.CompletedSlash } window || window.ParentFrameId != use.Id || window.AttackOwnerFrameId != use.Id || window.Action.ActionId != original.ActionId ||
   _resolutionStack[at + 2] is not ProgramSkillFrame { AnniversaryQiaoRui: { Operation: SkillProgramEffectOp.ResolveAnniversaryQiaoRuiSaowei } r, WindowContext.CardUse: { } identity } root ||
   !ValidQiaoRui(root) || r.ParentId != window.Id || root.WindowContext.ParentFrameId != window.Id || identity.ParentCardUseFrameId != use.Id || identity.CardActionId != original.ActionId || original.ActorSeat == root.OwnerSeat ||
   ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id && e.CardId == use.CardId) != 1) return false;
  if (at + 3 < _resolutionStack.Count) return QiaoRuiFirstChild(root, _resolutionStack[at + 3]) && QiaoRuiObserverSuffix(at + 2);
  if (r.Stage == AnniversaryQiaoRuiStage.SlashChoice) return HasAnniversaryQiaoRuiPrompt();
  return r.Returned && r.Stage is AnniversaryQiaoRuiStage.NativeSlash or AnniversaryQiaoRuiStage.NativeTail or AnniversaryQiaoRuiStage.ClaimChildren &&
   LifecycleCardUse(r.UseId) is null && r.Plan is { } plan && ProgramEventHistory<AnniversaryQiaoRuiUseReturnedEvent>().Count(e => e == new AnniversaryQiaoRuiUseReturnedEvent(root.Id, r.UseId, r.ActionId)) == 1 &&
   ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == r.UseId && e.CardId == plan.CardId) == 1;
 }
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 49400)] private void AssertQiaoRui(ProgramSkillFrame f)
 { if (f.AnniversaryQiaoRui is not null && (!ValidQiaoRui(f) || f.PendingMovementContinuation is { } p && !QiaoRuiMovement(f, GetPausedPrivateOfferEffect(f), p))) throw new InvalidOperationException("Qiao Rui lost his original trigger, marked entity acquisition, paid native movement or whole Slash return."); }
 [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 49400)] [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 49400)] [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 49400)]
 private bool QiaoRuiMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) => f.AnniversaryQiaoRui is { Stage: AnniversaryQiaoRuiStage.DrawChildren or AnniversaryQiaoRuiStage.DiscardChildren or AnniversaryQiaoRuiStage.HealthChildren or AnniversaryQiaoRuiStage.NativeTail or AnniversaryQiaoRuiStage.ClaimChildren } r && effect?.Op == r.Operation && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && ValidQiaoRui(f);
 private bool QiaoRuiFirstChild(ProgramSkillFrame f, ResolutionFrame child)
 {
  if (!ValidQiaoRui(f) || f.AnniversaryQiaoRui is not { } r) return false;
  if (child is CardUseFrame use) return r.Stage == AnniversaryQiaoRuiStage.NativeSlash && IsQiaoRuiNativeUse(use);
  if (child is NativeDrawAttemptFrame draw) return r.Stage == AnniversaryQiaoRuiStage.Drawing && draw.Context.ParentFrameId == f.Id && draw.Context.RecipientSeat == f.OwnerSeat && draw.Context.RequestedCount == r.Requested && NativeDrawAttemptRidesOn(draw, f);
  if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } p && QiaoRuiMovement(f, GetPausedPrivateOfferEffect(f), p) && (ProgramMovementReturnMatches(f, moved) || IsAnniversaryQiaoRuiSettlementBatch(f, moved.Batch)) && moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m));
  if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id && recovery.Return.ResumeFrameId == f.Id && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement;
  if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
  return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id && (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.CharacterStateContinuation == CharacterStateContinuation.Program);
 }
 private ProgramSkillFrame? QiaoRuiObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.QiaoRuiFirstChild(f, c), static (e, i) => e.QiaoRuiObserverSuffix(i));
 private bool QiaoRuiNativeEdge(int child)
 {
  if (_resolutionStack[child - 1] is CardUseFrame { AnniversaryQiaoRuiUse: not null, Step: ResolutionFrameStep.AwaitingResponse, CardAttack: { Active: true } attack } responding && IsQiaoRuiNativeUse(responding) &&
   _resolutionStack[child] is ResponseWindowFrame { Step: ResolutionFrameStep.AwaitingResponse, RequiredCardKind: CardKind.Dodge } response)
   return response.ParentFrameId == responding.Id && response.SourceSeat == attack.SourceSeat && response.ResponderSeat == attack.TargetSeat && response.IncomingCard == (attack.EffectiveCardKind ?? responding.CardKind) && IsSlashCard(response.IncomingCard);
  if (_resolutionStack[child - 1] is CardUseFrame { AnniversaryQiaoRuiUse: not null } use && IsQiaoRuiNativeUse(use) && _resolutionStack[child] is ProgramCardTriggerWindowFrame window) return window.ParentFrameId == use.Id && window.Action.ActionId == use.Action!.ActionId;
  if (_resolutionStack[child - 1] is ProgramCardTriggerWindowFrame card && LifecycleCardUse(card.ParentFrameId) is { AnniversaryQiaoRuiUse: not null } native && IsQiaoRuiNativeUse(native) && _resolutionStack[child] is ProgramSkillFrame f && card.CandidateIndex >= 0 && card.CandidateIndex < card.Candidates.Count)
   return MountObserverCandidateMatches(f, ToSharedCandidate(card.Candidates[card.CandidateIndex])) && f.WindowContext == CreateCardActionProgramContext(card, card.Candidates[card.CandidateIndex]);
  return false;
 }
 private bool QiaoRuiObserverSuffix(int i) => HasObserverSuffix(i, static (e, n) => e.IsMovementObserverEdge(n) || e.IsSameNameHandObserverEdge(n) || e.IsYanPuDyingExitEdge(n) || e.TypedDamagePostEventEdge(e._resolutionStack[n - 1], e._resolutionStack[n]) ||
  e.DamageFrameRidesOn(e._resolutionStack[n], e._resolutionStack[n - 1]) || DamageObserverRidesOn(e._resolutionStack[n], e._resolutionStack[n - 1]) || e.QiaoRuiNativeEdge(n) ||
  e._resolutionStack[n - 1] is ProgramSkillFrame f && e.QiaoRuiFirstChild(f, e._resolutionStack[n]) || NativeDrawAttemptRidesOn(e._resolutionStack[n], e._resolutionStack[n - 1]) || e.RecoveryReplacementFrameRidesOn(e._resolutionStack[n], e._resolutionStack[n - 1]),
  static (e, n, d) => e.IsSuspendedOrPaidRescueObserverSuffix(n, d) || e.TieredRoundZeroDyingRescueRide(n, d) || e.DrawFundedDistinctBasicDyingRescueRide(n, d) || e.HuanpeiDyingRescueRide(n, d));
 [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 49400)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 49400)] private bool QiaoRuiDamageObserver(long id) => QiaoRuiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
 [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 49400)] private bool QiaoRuiBeforeDamage(long id) => QiaoRuiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
 [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 49400)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 49400)] private bool QiaoRuiDying() => ActiveDying is { } d && QiaoRuiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 49400)] private bool AdvanceQiaoRuiChildren() => _pendingDecision is null && QiaoRuiObserverRoot() is not null && TryAdvanceObserverSubtree(true);
 [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 49400)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 49400)] private bool QiaoRuiRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
 { if (f.AnniversaryQiaoRui is null) return false; var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id); return parent >= 0 && child > parent && QiaoRuiFirstChild(f, _resolutionStack[parent + 1]) && (child == parent + 1 || QiaoRuiObserverSuffix(parent)) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]); }
}
