using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
 private const string HaoMengAction = "anniversary-hao-meng";
 private bool HasAnniversaryHaoMengCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryHaoMengXiongmangConsequence);
 private static CardConversionSource HaoMengSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
 private static CardConversionSource HaoMengNativeSource(ProgramSkillFrame f, int count) => new(f.SkillId, "distinct-hand-" + count, f.OwnerSeat, f.SkillInstanceId);
 private Suit HaoMengHandSuit(int owner, int id) => GetProgramEffectiveSuit(_players[owner], GetAdvancedCard(id), CardLocation.Hand(owner));
 [ProgramCapability] private sealed class AnniversaryHaoMengHost(GameEngine e) : IAnniversaryHaoMengHost
 { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginHaoMeng(f, op); }
 private bool HaoMengMaterials(int owner, IReadOnlyList<int> ids, CardConversionSource source)
 {
  if (source.SkillId != AnniversaryHaoMengComposition.Skill || source.OwnerSeat != owner || ids.Count is < 1 or > 4 || ids.Distinct().Count() != ids.Count || ViewAsRule(source) is not { OutputKind: CardKind.Slash } rule || rule.Id != "distinct-hand-" + ids.Count || rule.InputCount != ids.Count) return false;
  return ids.All(id => _cardZones.GetLocation(id) == CardLocation.Hand(owner) && HaoMengHandSuit(owner, id) != Suit.None && !IsTurnHandCardRestricted(_players[owner], GetAdvancedCard(id)) && !HasProgramCardIdentity(_players[owner], GetAdvancedCard(id))) && ids.Select(id => HaoMengHandSuit(owner, id)).Distinct().Count() == ids.Count;
 }
 [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan, 49700)] private bool BanHaoMengConversion(ProgramViewAsLegalityQuery q) => q.Source.SkillId == AnniversaryHaoMengComposition.Skill && (q.ForResponse || !IsSlashCard(q.Kind) || !HaoMengMaterials(q.Owner.Seat, q.CardIds, q.Source));
 [ProgramCardLegality(ProgramCardLegalityStage.LegalActionProjection, 49700)] private LegalAction? ProjectHaoMengUse(ProgramLegalActionQuery q) => q.Action.ConversionSource is { SkillId: AnniversaryHaoMengComposition.Skill } s && ViewAsRule(s) is { } rule && q.Action.TargetSeats.Count > rule.InputCount ? null : q.Action;
 private int[] HaoMengNativeTargets(int actor, IReadOnlyList<int> ids, CardConversionSource source)
 {
  if (!HaoMengMaterials(actor, ids, source) || !_players[actor].IsAlive || IsTurnPhysicalUseForbidden(actor, ids)) return [];
  var cards = ids.Select(GetAdvancedCard).ToArray(); var owner = _players[actor]; var suit = PhysicalGroupSuit(owner, cards); var color = PhysicalGroupColor(owner, cards); var rank = cards.Length == 1 ? cards[0].Rank : 0;
  return _players.Where(t => CanUseVirtualSlashTarget(owner, t, CardKind.Slash, suit, color, rank, ids, conversionSource: source) && !IsDirectedCardTargetProhibited(actor, t.Seat, CardKind.Slash) && !IsCardTargetProhibited(t, CardKind.Slash, suit, color) && !HasBeneficiarySuitShield(actor, t.Seat, suit)).Select(t => t.Seat).ToArray();
 }
 private int[] HaoMengAvailableCards(ProgramSkillFrame f)
 {
  var chosen = f.AnniversaryHaoMeng!.Cards;
  if (chosen.Count == 4) return [];
  return GetHand(_players[f.OwnerSeat]).Where(c => !chosen.Contains(c.Id) && HaoMengHandSuit(f.OwnerSeat, c.Id) != Suit.None && chosen.All(id => HaoMengHandSuit(f.OwnerSeat, id) != HaoMengHandSuit(f.OwnerSeat, c.Id))).Select(c => c.Id)
   .Where(id => HaoMengNativeTargets(f.OwnerSeat, chosen.Append(id).ToArray(), HaoMengNativeSource(f, chosen.Count + 1)).Length > 0).Order().ToArray();
 }
 [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 49700, SkillProgramEffectOp.ResolveAnniversaryHaoMengXiongmangUse)] private bool CanStartHaoMeng(ProgramActivationLegalityContext c) => c.Owner.IsAlive && c.OwnerSeat == _currentSeat && _phase == TurnPhase.Play && GetSkillBindingShard(c.Owner).ProgramInstances.Where(i => i.SkillId == AnniversaryHaoMengComposition.Skill).Any(i => GetHand(c.Owner).Any(card => HaoMengNativeTargets(c.OwnerSeat, [card.Id], new(i.SkillId, "distinct-hand-1", c.OwnerSeat, i.SkillInstanceId)).Length > 0));
 private SkillProgramStepOutcome BeginHaoMeng(ProgramSkillFrame supplied, SkillProgramEffectOp op)
 {
  var f = GetActiveProgramFrame(supplied.Id);
  if (f.AnniversaryHaoMeng is not null || f.InstructionIndex != 1 || !HaoMengOriginal(f, op)) throw new InvalidOperationException("Xiongmang needs its original active binding or issued whole native Slash consequence.");
  var r = new AnniversaryHaoMengReceipt(HaoMengSource(f), f.GameplayHash, op, f.WindowContext?.ParentFrameId ?? 0, _turnNumber, op == SkillProgramEffectOp.ResolveAnniversaryHaoMengXiongmangUse ? AnniversaryHaoMengStage.Cards : AnniversaryHaoMengStage.HealthChildren) { Before = _movementSequence, After = _movementSequence };
  if (op == SkillProgramEffectOp.ResolveAnniversaryHaoMengXiongmangConsequence)
  { var native = HaoMengConsequenceUse(f.WindowContext!)!; r = r with { UseId = native.Id, ActionId = native.Action!.ActionId }; }
  ReplaceRuntimeTop(f = f with { AnniversaryHaoMeng = r });
  AdvanceEventRulesAndQueueFact(new AnniversaryHaoMengStartedEvent(f.Id, r.Source, r.Hash, op, r.ParentId, r.Turn));
  if (op == SkillProgramEffectOp.ResolveAnniversaryHaoMengXiongmangUse) { PublishHaoMeng(f); return SkillProgramStepOutcome.AwaitChoice; }
  var use = HaoMengConsequenceUse(f.WindowContext!)!; var origin = use.AnniversaryHaoMengUse!; var damaged = f.WindowContext!.Facts!.CardUseCausedDamage == true; var maximum = _players[f.OwnerSeat].MaxHp;
  if (ProgramEventHistory<AnniversaryHaoMengConsequenceEvent>().Any(e => e.UseId == use.Id)) throw new InvalidOperationException("Xiongmang cannot settle the same whole Slash twice.");
  if (damaged)
  {
   if (origin.Phase is { } phase) AdvanceEventRulesAndQueueFact(new AnniversaryHaoMengPhaseQuotaEvent(f.Id, use.Id, f.OwnerSeat, phase));
   AdvanceEventRulesAndQueueFact(new AnniversaryHaoMengConsequenceEvent(f.Id, use.Id, r.ActionId, f.OwnerSeat, true, maximum, maximum)); FinishHaoMeng(f); return SkillProgramStepOutcome.AwaitChild;
  }
  ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
  ChangeProgramMaximumHp(f, -1);
  AdvanceEventRulesAndQueueFact(new AnniversaryHaoMengConsequenceEvent(f.Id, use.Id, r.ActionId, f.OwnerSeat, false, maximum, _players[f.OwnerSeat].MaxHp)); ContinueHaoMeng(f.Id); return SkillProgramStepOutcome.AwaitChild;
 }
 private PromptChoice HaoMengChoice(ProgramSkillFrame f, string verb, string value, string label, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) => new(new($"hao-meng.{f.Id}.{verb}.{value}"), label, cards ?? [], targets ?? [], new Dictionary<string, string> { ["program-action"] = HaoMengAction, ["verb"] = verb });
 private IReadOnlyList<PromptChoice> HaoMengChoices(ProgramSkillFrame f)
 {
  var r = f.AnniversaryHaoMeng!; var choices = new List<PromptChoice>();
  if (r.Stage == AnniversaryHaoMengStage.Cards)
  {
   choices.AddRange(HaoMengAvailableCards(f).Select(id => HaoMengChoice(f, "card", id.ToString(CultureInfo.InvariantCulture), "选择【" + GetAdvancedCard(id).DisplayName + "】（" + HaoMengHandSuit(f.OwnerSeat, id) + "）", [id])));
   if (r.Cards.Count > 0 && HaoMengNativeTargets(f.OwnerSeat, r.Cards, HaoMengNativeSource(f, r.Cards.Count)).Length > 0) choices.Add(HaoMengChoice(f, "targets", "done", "完成材料选择，选择目标"));
  }
  else if (r.Stage == AnniversaryHaoMengStage.Targets)
  {
   if (r.Targets.Count < r.Cards.Count) choices.AddRange(HaoMengNativeTargets(f.OwnerSeat, r.Cards, HaoMengNativeSource(f, r.Cards.Count)).Except(r.Targets).Select(t => HaoMengChoice(f, "target", t.ToString(CultureInfo.InvariantCulture), "目标：" + _players[t].Name, targets: [t])));
   if (r.Targets.Count > 0 && r.Targets.All(HaoMengNativeTargets(f.OwnerSeat, r.Cards, HaoMengNativeSource(f, r.Cards.Count)).Contains)) choices.Add(HaoMengChoice(f, "use", "done", "使用雄莽杀", r.Cards, r.Targets));
  }
  choices.Add(HaoMengChoice(f, "cancel", "done", "取消雄莽，不支付牌")); return Array.AsReadOnly(choices.ToArray());
 }
 private void PublishHaoMeng(ProgramSkillFrame f)
 {
  var choices = HaoMengChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
  _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.AnniversaryHaoMeng!.Stage == AnniversaryHaoMengStage.Cards ? "雄莽：选择花色不同的手牌。" : $"雄莽：选择至多{f.AnniversaryHaoMeng.Cards.Count}名原生合法目标。", Array.AsReadOnly(choices.SelectMany(c => c.Cards).Distinct().ToArray()), Array.AsReadOnly(choices.SelectMany(c => c.Targets).Distinct().ToArray()), f.OwnerSeat)
   { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
  _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
 }
 [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryHaoMengXiongmangUse)] private PromptChoice HaoMengAi(PendingDecision p) => p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("verb") is "use" or "targets") ?? p.Choices.First();
 [ProgramAnswer(HaoMengAction)] private void AnswerHaoMeng(PromptChoice choice)
 {
  if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryHaoMeng: { Stage: AnniversaryHaoMengStage.Cards or AnniversaryHaoMengStage.Targets } r } f || !ValidHaoMeng(f) || _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([choice], HaoMengChoices(f).Where(c => c.Id == choice.Id).ToArray())) throw new InvalidOperationException("Xiongmang rejects another Hand entity, suit, native target or payment stage.");
  ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
  switch (choice.Parameters["verb"])
  {
   case "cancel": FinishHaoMeng(f); return;
   case "card": var card = choice.Cards.Single(); AdvanceEventRulesAndQueueFact(new AnniversaryHaoMengCardChosenEvent(f.Id, r.Cards.Count, card, HaoMengHandSuit(f.OwnerSeat, card))); ReplaceRuntimeTop(f = f with { AnniversaryHaoMeng = r with { Cards = r.Cards.Append(card).ToArray() } }); PublishHaoMeng(f); return;
   case "targets": ReplaceRuntimeTop(f = f with { AnniversaryHaoMeng = r with { Stage = AnniversaryHaoMengStage.Targets } }); PublishHaoMeng(f); return;
   case "target": var target = choice.Targets.Single(); AdvanceEventRulesAndQueueFact(new AnniversaryHaoMengTargetChosenEvent(f.Id, r.Targets.Count, target)); ReplaceRuntimeTop(f = f with { AnniversaryHaoMeng = r with { Targets = r.Targets.Append(target).ToArray() } }); PublishHaoMeng(f); return;
   case "use": ReplaceRuntimeTop(f = f with { AnniversaryHaoMeng = r with { Stage = AnniversaryHaoMengStage.Issuing, Before = _movementSequence, After = _movementSequence } }); IssueHaoMengUse(f); return;
  }
  throw new InvalidOperationException("Unknown Xiongmang choice.");
 }
 private void ContinueHaoMeng(long id)
 {
  if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryHaoMeng: { } r } f || f.Id != id) return;
  if (!ValidHaoMeng(f)) throw new InvalidOperationException("Xiongmang lost its original binding, material payment or native consequence.");
  if (r.Stage is AnniversaryHaoMengStage.Cards or AnniversaryHaoMengStage.Targets) { if (_pendingDecision is null) PublishHaoMeng(f); return; }
  if (r.Stage is not (AnniversaryHaoMengStage.NativeTail or AnniversaryHaoMengStage.HealthChildren)) return;
  if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(id) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id)) return;
  FinishHaoMeng(GetActiveProgramFrame(id));
 }
 private void FinishHaoMeng(ProgramSkillFrame f)
 { ClearPendingDecision(); ReplaceRuntimeTop(f = f with { AnniversaryHaoMeng = f.AnniversaryHaoMeng! with { Stage = AnniversaryHaoMengStage.Completed }, PendingMovementContinuation = null }); AdvanceEventRulesAndQueueFact(new AnniversaryHaoMengCompletedEvent(f.Id)); AdvanceRuntimeProgram(f.Id); }
 [ProgramContinuation(ProgramContinuationKind.Resume, 49700)] private bool ResumeHaoMeng(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryHaoMeng: { Stage: not AnniversaryHaoMengStage.Completed } } f || f.Id != id) return false; ContinueHaoMeng(id); return true; }
 [ProgramContinuation(ProgramContinuationKind.Movement, 49700)] private bool ReturnHaoMengMovement(ProgramSkillFrame f) => f.AnniversaryHaoMeng is not null && ResumeHaoMeng(f.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 49700)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 49700)] private bool KeepHaoMengIssued(ProgramSkillFrame f) => f.AnniversaryHaoMeng is { Stage: not (AnniversaryHaoMengStage.Cards or AnniversaryHaoMengStage.Targets) } && ValidHaoMeng(f) || f.TriggerId == AnniversaryHaoMengComposition.Consequence && f.WindowContext is { } c && HaoMengConsequenceUse(c) is not null;
 private IEnumerable<RuleQueryContribution> AnniversaryHaoMengSlashContributions(CharacterState player, SkillRuleQuery query) => query != SkillRuleQuery.SlashLimit || !HasAnniversaryHaoMengCapability || !player.IsAlive || CurrentActualDiscardRecoveryPhase() is not { } phase ? [] : ProgramEventHistory<AnniversaryHaoMengPhaseQuotaEvent>().Where(e => e.Owner == player.Seat && e.Phase == phase).Select(e => (RuleQueryContribution)new FiniteRuleQueryContribution("anniversary:xiongmang:" + e.FrameId, SkillRuleOperation.Add, 1)).ToArray();
}
