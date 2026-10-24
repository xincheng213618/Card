namespace CardGame.Core;

public sealed partial class GameEngine
{
 private const string MobileHeQiAction = "mobile-he-qi-view";
 private static CardConversionSource MobileHeQiSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
 [ProgramCapability] private sealed class MobileHeQiHost(GameEngine e) : IMobileHeQiHost
 { public SkillProgramStepOutcome View(ProgramSkillFrame f) => e.BeginMobileHeQiView(f); }
 private bool MobileHeQiOriginal(ProgramSkillFrame f) => f.SkillId == MobileHeQiComposition.Skill && f.TriggerId is null && f.ActivationId == MobileHeQiComposition.Activation && f.InstructionIndex == 3 && f.SelectedCardIds.Count == 1 && f.SelectedTargetSeats is [var target] && target != f.OwnerSeat && IsValidPlayerSeat(target) && _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash == f.GameplayHash && GetPausedPrivateOfferEffect(f)?.Op == SkillProgramEffectOp.ResolveMobileHeQiShanxiView &&
  ProgramEventHistory<ProgramSkillStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId && e.ActivationId == f.ActivationId) == 1;
 private NativeLogicalDiscardMaterialCompletedEvent? MobileHeQiDiscard(ProgramSkillFrame f, out NativeLogicalDiscardInvocationRecordedEvent? invoice)
 {
  invoice = null; if (!MobileHeQiOriginal(f)) return null;
  var selected = f.CardSetBindings.SingleOrDefault(b => b.Name == MobileHeQiComposition.DiscardBind);
  if (selected?.CardIds is not [var id]) return null;
  var invocations = ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Where(i => i.ParentFrameId == f.Id && i.InitiatorSeat == f.OwnerSeat && i.ExecutorSeat == f.OwnerSeat && i.ActualCount == 1 && i.Reason.Value == "skill-program." + f.SkillId + ".SelectAndMoveOwnedCard").ToArray();
  if (invocations is not [var exact]) return null;
  var material = ProgramEventHistory<NativeLogicalDiscardMaterialCompletedEvent>().Where(e => e.InvocationId == exact.InvocationId && e.CardId == id && e.From.OwnerSeat == f.SelectedTargetSeats[0] && e.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment).ToArray();
  if (material is not [var paid] || !_cardMovements.Any(m => m.CardId == paid.CardId && m.Sequence == paid.CompletionSequence && m.Sequence > exact.SequenceBefore && m.Sequence <= exact.SequenceAfter)) return null;
  invoice = exact; return paid;
 }
 private SkillProgramStepOutcome BeginMobileHeQiView(ProgramSkillFrame supplied)
 {
  var f = GetActiveProgramFrame(supplied.Id);
  if (!MobileHeQiOriginal(f) || f.MobileHeQiView is not null) throw new InvalidOperationException("Mobile Shanxi requires its original completed native payment and selected target.");
  if (MobileHeQiDiscard(f, out var invoice) is not { } paid) return SkillProgramStepOutcome.Continue;
  var target = f.SelectedTargetSeats[0]; var kind = GetAdvancedCard(paid.CardId).Kind; var dodge = kind == CardKind.Dodge;
  var viewer = dodge ? f.OwnerSeat : target; var holder = dodge ? target : f.OwnerSeat;
  if (!_players[viewer].IsAlive || !_players[holder].IsAlive) return SkillProgramStepOutcome.Continue;
  var r = new MobileHeQiViewReceipt(MobileHeQiSource(f), f.GameplayHash, target, invoice!.InvocationId, paid.CardId, paid.From, kind, viewer, holder) { Hand = GetHand(_players[holder]).Select(c => c.Id).ToArray() };
  ReplaceRuntimeTop(f = f with { MobileHeQiView = r, Step = ResolutionFrameStep.AwaitingResponse });
  AdvanceEventRulesAndQueueFact(new MobileHeQiHandViewedEvent(f.Id, r.Source, r.InvocationId, r.CardId, r.DiscardedKind, r.Viewer, r.Holder, r.Hand.Count));
  PublishMobileHeQiView(f); return SkillProgramStepOutcome.AwaitChoice;
 }
 private PromptChoice MobileHeQiConfirm(ProgramSkillFrame f) => new(new("mobile-he-qi.confirm." + f.Id), "确认已观看手牌", [], [], new Dictionary<string, string> { ["program-action"] = MobileHeQiAction });
 private void PublishMobileHeQiView(ProgramSkillFrame f)
 {
  var r = f.MobileHeQiView!; var skill = _contentRegistry.GetSkill(f.SkillId);
  _pendingDecision = new(DecisionKind.ProgramTrigger, r.Viewer, "闪袭：观看该角色的当前手牌。", [], [], f.OwnerSeat) { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = r.Holder, Choices = Array.AsReadOnly(new[] { MobileHeQiConfirm(f) }), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
  _status = _players[r.Viewer].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
 }
 private bool ValidMobileHeQiView(ProgramSkillFrame f)
 {
  if (f.MobileHeQiView is not { } r || !MobileHeQiOriginal(f) || r.Source != MobileHeQiSource(f) || r.Hash != f.GameplayHash || r.Target != f.SelectedTargetSeats[0] || MobileHeQiDiscard(f, out var invoice) is not { } paid || r.InvocationId != invoice!.InvocationId || r.CardId != paid.CardId || r.From != paid.From || r.DiscardedKind != GetAdvancedCard(paid.CardId).Kind || r.Viewer != (r.DiscardedKind == CardKind.Dodge ? f.OwnerSeat : r.Target) || r.Holder != (r.DiscardedKind == CardKind.Dodge ? r.Target : f.OwnerSeat) || r.Hand.Distinct().Count() != r.Hand.Count || !r.Hand.SequenceEqual(GetHand(_players[r.Holder]).Select(c => c.Id))) return false;
  return ProgramEventHistory<MobileHeQiHandViewedEvent>().Count(e => e == new MobileHeQiHandViewedEvent(f.Id, r.Source, r.InvocationId, r.CardId, r.DiscardedKind, r.Viewer, r.Holder, r.Hand.Count)) == 1;
 }
 private bool HasMobileHeQiPrompt() => _resolutionStack.LastOrDefault() is ProgramSkillFrame { MobileHeQiView: { } r, Step: ResolutionFrameStep.AwaitingResponse } f && ValidMobileHeQiView(f) && _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p && p.PlayerSeat == r.Viewer && p.SourceSeat == f.OwnerSeat && p.TargetSeat == r.Holder && AssistedChoicesEqual(p.Choices, [MobileHeQiConfirm(f)]);
 [ProgramAnswer(MobileHeQiAction)] private void AnswerMobileHeQiView(PromptChoice c)
 {
  if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileHeQiView: { } r } f || !HasMobileHeQiPrompt() || !AssistedChoicesEqual([c], [MobileHeQiConfirm(f)])) throw new InvalidOperationException("Mobile Shanxi lost its exact private viewer or hand confirmation.");
  ClearPendingDecision(); ReplaceRuntimeTop(f with { MobileHeQiView = null, Step = ResolutionFrameStep.ResolvingEffect }); AdvanceEventRulesAndQueueFact(new MobileHeQiViewConfirmedEvent(f.Id, r.Viewer)); AdvanceRuntimeProgram(f.Id);
 }
 [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileHeQiShanxiView)] private PromptChoice MobileHeQiAi(PendingDecision p) => p.Choices.Single();
 [ProgramViewContributor(ProgramViewContribution.PrivateCards, 50100)] private CardSnapshot[] MobileHeQiPrivateHand(int viewer) => _resolutionStack.LastOrDefault() is ProgramSkillFrame { MobileHeQiView: { } r } f && r.Viewer == viewer && ValidMobileHeQiView(f) ? r.Hand.Select(id => ToSnapshot(GetAdvancedCard(id))).ToArray() : [];
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 50100)] private void AssertMobileHeQiView(ProgramSkillFrame f) { if (f.MobileHeQiView is not null && (!ValidMobileHeQiView(f) || !HasMobileHeQiPrompt())) throw new InvalidOperationException("Mobile Shanxi changed its paid discard, private hand viewer or owning frame."); }
}
