using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
 private const string QiaoRuiAction = "anniversary-qiao-rui", QiaoRuiDraw = "skill-program.anniversary:aishou.draw", QiaoRuiDiscard = "skill-program.anniversary:aishou.discard", QiaoRuiClaim = "skill-program.anniversary:saowei.obtain";
 private static CardConversionSource QiaoRuiSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
 private bool HasAnniversaryQiaoRuiCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouDraw);
 [ProgramCapability] private sealed class AnniversaryQiaoRuiHost(GameEngine e) : IAnniversaryQiaoRuiHost
 { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginQiaoRui(f, op); }

 // An Ai is this particular acquisition's Hand residency, not a permanent tag
 // on every future holder of the physical entity. Movement cutoffs make a
 // suspended loss window independent of later nested moves or reacquisition.
 private int[] QiaoRuiMarkedHand(int owner, long cutoff) => !HasAnniversaryQiaoRuiCapability ? [] :
  ProgramEventHistory<AnniversaryQiaoRuiMarkedEvent>().Where(mark => mark.Source.OwnerSeat == owner && mark.EntrySequence <= cutoff &&
   _cardMovements.Any(m => m.Sequence == mark.EntrySequence && m.CardId == mark.CardId && m.CardKind == mark.PrintedKind && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(owner)) &&
   !_cardMovements.Any(m => m.CardId == mark.CardId && m.Sequence > mark.EntrySequence && m.Sequence <= cutoff && m.From == CardLocation.Hand(owner) && m.To != m.From))
   .Select(mark => mark.CardId).Distinct().Order().ToArray();
 private bool QiaoRuiLastLoss(int owner, CardMovementBatchContext b)
 {
  if (b.TurnNumber <= 0 || b.MovementTiming is not { } timing || timing.ActualTurnOwnerSeat == owner || b.Movements.Count == 0 || b.Movements.Any(m => !_cardMovements.Contains(m)) ||
   ProgramEventHistory<AnniversaryQiaoRuiMaximumHpEvent>().Any(e => e.LossBatchId == b.Id && ProgramEventHistory<AnniversaryQiaoRuiStartedEvent>().Any(s => s.FrameId == e.FrameId && s.Source.OwnerSeat == owner))) return false;
  var before = b.Movements.Min(m => m.Sequence) - 1; var after = b.Movements.Max(m => m.Sequence); var marks = QiaoRuiMarkedHand(owner, before);
  return marks.Length > 0 && QiaoRuiMarkedHand(owner, after).Length == 0 && b.Movements.Any(m => marks.Contains(m.CardId) && m.From == CardLocation.Hand(owner) && m.To != m.From);
 }
 private CardUseFrame? QiaoRuiOriginalSlash(ProgramSkillWindowContext? w) => w is { Window: SkillProgramTriggerWindow.CardUseCompleted, CardUse: { } c } && LifecycleCardUse(c.ParentCardUseFrameId) is { Step: ResolutionFrameStep.Completed, Action: { Type: CardActionType.Use } a } u && a.ActionId == c.CardActionId && IsSlashCard(a.EffectiveKind) ? u : null;
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 49400, SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouDraw, SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouPrepare, SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouLastLoss, SkillProgramEffectOp.ResolveAnniversaryQiaoRuiSaowei)]
 private bool CanOfferQiaoRui(ProgramTriggerLegalityContext c) => c.Trigger.Effects[0].Op switch
 {
  SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouDraw => c.Context.Window == SkillProgramTriggerWindow.TurnEnding && c.OwnerSeat == _currentSeat,
  SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouPrepare => c.Context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && c.OwnerSeat == _currentSeat && QiaoRuiMarkedHand(c.OwnerSeat, _movementSequence).Length > 0,
  SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouLastLoss => c.Context.MovementBatch is { } b && QiaoRuiLastLoss(c.OwnerSeat, b),
  _ => QiaoRuiOriginalSlash(c.WindowContext) is { Action: { } a } && a.ActorSeat != c.OwnerSeat && QiaoRuiPlans(c.OwnerSeat, QiaoRuiSourceForCandidate(c.Candidate), a.TargetSeats).Count > 0
 };
 private static CardConversionSource QiaoRuiSourceForCandidate(ProgramTriggerCandidate c) => new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId);
 private SkillProgramStepOutcome BeginQiaoRui(ProgramSkillFrame supplied, SkillProgramEffectOp op)
 {
  var f = GetActiveProgramFrame(supplied.Id);
  if (f.AnniversaryQiaoRui is not null || f.InstructionIndex != 1 || !QiaoRuiOriginal(f, op)) throw new InvalidOperationException("Qiao Rui needs his original preparation, ending, actual loss or completed Slash candidate.");
  var stage = op == SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouDraw ? AnniversaryQiaoRuiStage.Drawing : op == SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouPrepare ? AnniversaryQiaoRuiStage.DiscardChildren : op == SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouLastLoss ? AnniversaryQiaoRuiStage.HealthChildren : AnniversaryQiaoRuiStage.SlashChoice;
  var r = new AnniversaryQiaoRuiReceipt(QiaoRuiSource(f), f.GameplayHash, op, f.WindowContext!.ParentFrameId, _turnNumber, stage) { Before = _movementSequence, After = _movementSequence };
  if (op == SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouDraw) r = r with { Requested = Math.Max(0, _players[f.OwnerSeat].MaxHp) };
  if (op == SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouPrepare) r = r with { Cards = QiaoRuiMarkedHand(f.OwnerSeat, _movementSequence).Where(id => _cardZones.GetLocation(id) == CardLocation.Hand(f.OwnerSeat) && !IsSelfHandCategoryDiscardForbidden(f.OwnerSeat, GetAdvancedCard(id), CardLocation.Hand(f.OwnerSeat), OwnedCardMoveIntent.Discard)).ToArray() };
  ReplaceRuntimeTop(f = f with { AnniversaryQiaoRui = r });
  AdvanceEventRulesAndQueueFact(new AnniversaryQiaoRuiStartedEvent(f.Id, r.Source, r.Hash, op, r.ParentId, r.Turn));
  if (stage == AnniversaryQiaoRuiStage.Drawing)
  { if (r.Requested == 0) FinishQiaoRui(f); else BeginOperationProgramDraw(f, f.OwnerSeat, r.Requested, new(QiaoRuiDraw)); return SkillProgramStepOutcome.AwaitChild; }
  if (stage == AnniversaryQiaoRuiStage.DiscardChildren)
  {
   ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
   MoveProgramLogicalDiscard(f, f.OwnerSeat, r.Cards, new(QiaoRuiDiscard), (_, _) => { var current = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(current with { AnniversaryQiaoRui = current.AnniversaryQiaoRui! with { After = _movementSequence } }); });
   ContinueQiaoRui(f.Id); return SkillProgramStepOutcome.AwaitChild;
  }
  if (stage == AnniversaryQiaoRuiStage.HealthChildren)
  {
   if (!QiaoRuiLastLoss(f.OwnerSeat, f.WindowContext.MovementBatch!)) throw new InvalidOperationException("Aishou lost the exact final outside-turn marked Hand departure.");
   ChangeQiaoRuiMaximum(f, -1, f.WindowContext.MovementBatch!.Id); ContinueQiaoRui(f.Id); return SkillProgramStepOutcome.AwaitChild;
  }
  PublishQiaoRui(f); return SkillProgramStepOutcome.AwaitChoice;
 }
 private void ChangeQiaoRuiMaximum(ProgramSkillFrame f, int amount, long lossBatch)
 {
  var before = _players[f.OwnerSeat].MaxHp;
  ReplaceRuntimeTop(f = f with { AnniversaryQiaoRui = f.AnniversaryQiaoRui! with { Stage = AnniversaryQiaoRuiStage.HealthChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
  ChangeProgramMaximumHp(f, amount);
  AdvanceEventRulesAndQueueFact(new AnniversaryQiaoRuiMaximumHpEvent(f.Id, lossBatch, before, _players[f.OwnerSeat].MaxHp));
 }
 [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouDraw)] private void ReturnQiaoRuiDraw(ProgramSkillFrame supplied, NativeDrawOutcome d)
 {
  var f = GetActiveProgramFrame(supplied.Id); var r = f.AnniversaryQiaoRui!;
  if (!ValidQiaoRui(f) || r.Stage != AnniversaryQiaoRuiStage.Drawing || d.RecipientSeat != f.OwnerSeat || d.RequestedCount != r.Requested || d.SequenceBefore != r.Before || f.NativeDraw is not null) throw new InvalidOperationException("Aishou requires its exact once-paid original Draw return.");
  ReplaceRuntimeTop(f = f with { AnniversaryQiaoRui = r with { Stage = AnniversaryQiaoRuiStage.DrawChildren, Draw = d, After = d.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
  AdvanceEventRulesAndQueueFact(new AnniversaryQiaoRuiDrawReturnedEvent(f.Id, d.RequestedCount, d.ActualDrawIds.Count, d.SequenceBefore, d.SequenceAfter));
  foreach (var id in d.ActualDrawIds)
  {
   var m = _cardMovements.Where(m => m.CardId == id && m.Sequence > d.SequenceBefore && m.Sequence <= d.SequenceAfter && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat)).ToArray();
   if (m is not [var entry]) throw new InvalidOperationException("Aishou may mark only this paid draw's actual Hand entities.");
   AdvanceEventRulesAndQueueFact(new AnniversaryQiaoRuiMarkedEvent(f.Id, r.Source, r.Hash, id, entry.CardKind, entry.Sequence));
  }
  ContinueQiaoRui(f.Id);
 }
 private void ContinueQiaoRui(long id)
 {
  if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryQiaoRui: { } r } f || f.Id != id) return;
  if (!ValidQiaoRui(f)) throw new InvalidOperationException("Qiao Rui lost his original candidate, exact payment or native return.");
  if (r.Stage == AnniversaryQiaoRuiStage.SlashChoice) { if (_pendingDecision is null) PublishQiaoRui(f); return; }
  if (r.Stage is not (AnniversaryQiaoRuiStage.DrawChildren or AnniversaryQiaoRuiStage.DiscardChildren or AnniversaryQiaoRuiStage.HealthChildren or AnniversaryQiaoRuiStage.NativeTail or AnniversaryQiaoRuiStage.ClaimChildren)) return;
  if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(id) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id)) return;
  f = GetActiveProgramFrame(id); r = f.AnniversaryQiaoRui!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
  if (r.Stage == AnniversaryQiaoRuiStage.DiscardChildren)
  {
   var actual = ProgramEventHistory<NativeLogicalDiscardMaterialCompletedEvent>().Count(m => m.From == CardLocation.Hand(f.OwnerSeat) && r.Cards.Contains(m.CardId) && ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Any(i => i.InvocationId == m.InvocationId && i.ParentFrameId == f.Id && i.Reason.Value == QiaoRuiDiscard && i.SequenceBefore >= r.Before && i.SequenceAfter <= r.After));
   AdvanceEventRulesAndQueueFact(new AnniversaryQiaoRuiDiscardedEvent(f.Id, actual, r.Before, r.After));
   if (actual > _players[f.OwnerSeat].Hp && _players[f.OwnerSeat].IsAlive) { ChangeQiaoRuiMaximum(f, 1, 0); ContinueQiaoRui(id); return; }
  }
  if (r.Stage == AnniversaryQiaoRuiStage.NativeTail && r.Damaged && _players[f.OwnerSeat].IsAlive && r.Plan is { } p && _cardZones.GetLocation(p.CardId) is var from && from is { Zone: CardZoneKind.DiscardPile or CardZoneKind.Processing })
  {
   var before = _movementSequence;
   ReplaceRuntimeTop(f = f with { AnniversaryQiaoRui = r with { Stage = AnniversaryQiaoRuiStage.ClaimChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
   MoveProgramCardsFromMultipleSources([p.CardId], CardLocation.Hand(f.OwnerSeat), new(QiaoRuiClaim), (_, _) => { var current = GetActiveProgramFrame(id); ReplaceRuntimeTop(current with { AnniversaryQiaoRui = current.AnniversaryQiaoRui! with { After = _movementSequence } }); AdvanceEventRulesAndQueueFact(new AnniversaryQiaoRuiClaimedEvent(id, r.UseId, p.CardId, before, _movementSequence)); });
   ContinueQiaoRui(id); return;
  }
  FinishQiaoRui(f);
 }
 private void FinishQiaoRui(ProgramSkillFrame f)
 { ClearPendingDecision(); ReplaceRuntimeTop(f = f with { AnniversaryQiaoRui = f.AnniversaryQiaoRui! with { Stage = AnniversaryQiaoRuiStage.Completed }, PendingMovementContinuation = null }); AdvanceEventRulesAndQueueFact(new AnniversaryQiaoRuiCompletedEvent(f.Id)); AdvanceRuntimeProgram(f.Id); }
 [ProgramContinuation(ProgramContinuationKind.Resume, 49400)] private bool ResumeQiaoRui(long id)
 { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryQiaoRui: { Stage: not AnniversaryQiaoRuiStage.Completed } } f || f.Id != id) return false; ContinueQiaoRui(id); return true; }
 [ProgramContinuation(ProgramContinuationKind.Movement, 49400)] private bool ReturnQiaoRuiMovement(ProgramSkillFrame f) => f.AnniversaryQiaoRui is not null && ResumeQiaoRui(f.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 49400)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 49400)] private bool KeepQiaoRuiPaid(ProgramSkillFrame f) => f.AnniversaryQiaoRui is { Stage: not AnniversaryQiaoRuiStage.SlashChoice } && ValidQiaoRui(f);
}
