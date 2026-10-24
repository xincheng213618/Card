namespace CardGame.Core;

public sealed partial class GameEngine
{
 private void IssueHaoMengUse(ProgramSkillFrame f)
 {
  var r = f.AnniversaryHaoMeng!; var source = HaoMengNativeSource(f, r.Cards.Count);
  if (!ValidHaoMeng(f) || r.Stage != AnniversaryHaoMengStage.Issuing || r.Targets.Count is < 1 || r.Targets.Count > r.Cards.Count || !r.Targets.All(HaoMengNativeTargets(f.OwnerSeat, r.Cards, source).Contains)) throw new InvalidOperationException("Xiongmang lost its distinct real Hand materials or native legal target group.");
  var cards = r.Cards.Select(GetAdvancedCard).ToArray(); var owner = _players[f.OwnerSeat];
  var id = BeginCardUse(cards[0], f.OwnerSeat, r.Targets, CardKind.Slash, physicalCardIds: r.Cards, conversionSource: source);
  if (LifecycleCardUse(id)?.AnniversaryHaoMengUse is not { ProducerFrameId: var producer } || producer != f.Id) throw new InvalidOperationException("Xiongmang must bind its exact original paid native producer before moving materials.");
  CompleteNativeMaterialSlash(id, owner, cards, CardKind.Slash, LifecycleCardUse(id)!.TargetSeats, source, HasCardArmorBypass(owner, _players[r.Targets[0]], CardKind.Slash));
 }
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 49700)] private void BindHaoMengUse(CardUseDeclaredEvent e)
 {
  if (!HasAnniversaryHaoMengCapability || LifecycleCardUse(e.ResolutionId) is not { Action: { Type: CardActionType.Use } a } use || !IsSlashCard(a.EffectiveKind)) return;
  var source = a.ConversionChain.SingleOrDefault(s => s.SkillId == AnniversaryHaoMengComposition.Skill); if (source is null) return;
  var ids = a.PhysicalCards.Select(c => c.CardId).ToArray();
  if (use.AnniversaryHaoMengUse is not null || source.OwnerSeat != a.ProviderSeat || a.ActorSeat != source.OwnerSeat || !HaoMengMaterials(a.ProviderSeat, ids, source) || a.PhysicalCards.Any(c => c.From != CardLocation.Hand(a.ProviderSeat)) || use.TargetSeats.Count < 1 || use.TargetSeats.Count > ids.Length || !use.PhysicalCardIds!.SequenceEqual(ids)) throw new InvalidOperationException("Xiongmang cannot declare another provider, repeated suit, non-Hand entity or excess target group.");
  var at = _resolutionStack.FindIndex(x => x.Id == use.Id); long producer = 0;
  if (at > 0 && _resolutionStack[at - 1] is ProgramSkillFrame { AnniversaryHaoMeng: { Operation: SkillProgramEffectOp.ResolveAnniversaryHaoMengXiongmangUse, Stage: AnniversaryHaoMengStage.Issuing } r } f)
  {
   if (!ValidHaoMeng(f) || source != HaoMengNativeSource(f, r.Cards.Count) || !r.Cards.SequenceEqual(ids) || !r.Targets.SequenceEqual(use.TargetSeats)) throw new InvalidOperationException("Xiongmang changed its original activation's materials or target group.");
   producer = f.Id; ReplaceRuntimeFrame(f.Id, f with { AnniversaryHaoMeng = r with { Stage = AnniversaryHaoMengStage.NativeSlash, UseId = use.Id, ActionId = a.ActionId } });
  }
  var origin = new AnniversaryHaoMengUseOrigin(use.Id, a.ActionId, source, _contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash, ids.Length, producer, _turnNumber, CurrentActualDiscardRecoveryPhase());
  UpdateLifecycleCardUse(use.Id, u => u with { AnniversaryHaoMengUse = origin }); AdvanceEventRulesAndQueueFact(new AnniversaryHaoMengUseIssuedEvent(origin));
  for (var i = 0; i < a.PhysicalCards.Count; i++) AdvanceEventRulesAndQueueFact(new AnniversaryHaoMengMaterialEvent(use.Id, i, a.PhysicalCards[i], HaoMengHandSuit(a.ProviderSeat, a.PhysicalCards[i].CardId)));
 }
 private bool IsHaoMengNativeUse(CardUseFrame use)
 {
  if (use.AnniversaryHaoMengUse is not { } o || use.Action is not { Type: CardActionType.Use } a || o.UseId != use.Id || o.ActionId != a.ActionId || o.MaterialCount is < 1 or > 4 || o.Source.SkillId != AnniversaryHaoMengComposition.Skill || a.ProviderSeat != o.Source.OwnerSeat || !ShownEntityUseActorMatches(use, o.Source.OwnerSeat, o.Source.OwnerSeat) || !IsSlashCard(use.CardKind) || a.EffectiveKind != use.CardKind || !a.ConversionChain.Contains(o.Source) || a.PhysicalCards.Count != o.MaterialCount || !use.PhysicalCardIds!.SequenceEqual(a.PhysicalCards.Select(c => c.CardId)) || _contentRegistry.GetSkill(o.Source.SkillId).Program?.GameplayHash != o.Hash || ViewAsRule(o.Source) is not { } rule || rule.Id != "distinct-hand-" + o.MaterialCount || ProgramEventHistory<AnniversaryHaoMengUseIssuedEvent>().Count(e => e.Origin == o) != 1) return false;
  var materials = ProgramEventHistory<AnniversaryHaoMengMaterialEvent>().Where(e => e.UseId == use.Id).OrderBy(e => e.Index).ToArray();
  return materials.Length == o.MaterialCount && materials.Select(e => e.Index).SequenceEqual(Enumerable.Range(0, o.MaterialCount)) && materials.Select(e => e.Material).SequenceEqual(a.PhysicalCards) && materials.All(e => e.Material.From == CardLocation.Hand(o.Source.OwnerSeat) && e.EffectiveSuit != Suit.None && Enum.IsDefined(e.EffectiveSuit)) && materials.Select(e => e.EffectiveSuit).Distinct().Count() == o.MaterialCount;
 }
 private bool IsHaoMengMaterialSlashAttack(FangtianHalberdHandle pending, CardAttackHandle attack, IReadOnlyList<Card> processing)
 {
  if (LifecycleCardUse(pending.ResolutionId) is not { AnniversaryHaoMengUse: { ProducerFrameId: > 0 } o, Action: { } action, PhysicalCardIds: { } materials } use || !IsHaoMengNativeUse(use) ||
      _resolutionStack.SingleOrDefault(x => x.Id == o.ProducerFrameId) is not ProgramSkillFrame { AnniversaryHaoMeng: { Stage: AnniversaryHaoMengStage.NativeSlash, Returned: false } r } f || !ValidHaoMeng(f) ||
      r.UseId != use.Id || r.ActionId != action.ActionId || !r.Cards.SequenceEqual(materials) || o.Turn != _turnNumber ||
      attack.ResolutionId != use.Id || pending.SourceSeat != action.ActorSeat || pending.Card.Id != materials[0] || pending.ConversionSource != o.Source ||
      !pending.TargetSeats.SequenceEqual(use.TargetSeats) || use.TargetIndex != pending.TargetIndex || pending.TargetIndex < 0 || pending.TargetIndex >= pending.TargetSeats.Count ||
      !(attack.SourceSeat == action.ActorSeat && attack.CardUserSeat == action.ActorSeat && attack.EffectiveCardKind == use.CardKind && attack.TargetSeat == GetFangtianEffectiveTarget(pending, pending.TargetIndex) || IsNativeSlashTargetDuelAttack(attack)) ||
      !attack.PhysicalCards.Select(c => c.Id).SequenceEqual(materials) || action.PhysicalCards.Any(cost => _cardMovements.Count(m => m.Sequence > r.Before && m.TurnNumber == o.Turn && m.CardId == cost.CardId && m.From == cost.From && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) != 1)) return false;
  var retained = materials.Where(id => !IsCurrentUsePhysicalCardClaim(use.Id, id)).ToHashSet();
  return processing.All(c => retained.Contains(c.Id)) && retained.All(id => _cardZones.GetLocation(id).Zone is CardZoneKind.Processing or CardZoneKind.Hand or CardZoneKind.DrawPile or CardZoneKind.DiscardPile);
 }
 private CardUseFrame? HaoMengConsequenceUse(ProgramSkillWindowContext c) => c.Window == SkillProgramTriggerWindow.CardUseCompleted && c.CardUse is { } identity && LifecycleCardUse(identity.ParentCardUseFrameId) is { Step: ResolutionFrameStep.Completed } use && use.Action?.ActionId == identity.CardActionId && IsHaoMengNativeUse(use) ? use : null;
 [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 49700)] private void CollectHaoMengConsequence(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result)
 {
  if (window != SkillProgramTriggerWindow.CardUseCompleted || _resolutionStack.LastOrDefault() is not CardUseFrame { AnniversaryHaoMengUse: { } o } use || use.Action?.ActionId != action.ActionId || !IsHaoMengNativeUse(use) || !_players[o.Source.OwnerSeat].IsAlive || ProgramEventHistory<AnniversaryHaoMengConsequenceEvent>().Any(e => e.UseId == use.Id)) return;
  if (!result.Any(c => c.OwnerSeat == o.Source.OwnerSeat && c.SkillId == o.Source.SkillId && c.SkillInstanceId == o.Source.SkillInstanceId && c.TriggerId == AnniversaryHaoMengComposition.Consequence)) result.Add(new(o.Source.OwnerSeat, action.ActorSeat, o.Source.SkillId, AnniversaryHaoMengComposition.Consequence, o.Hash, o.Source.SkillInstanceId, FrozenContext: CreateCardActionProgramContext(action, window, 0, o.Source.OwnerSeat, action.ActorSeat, CaptureProgramTriggerFacts(_players[o.Source.OwnerSeat], action) with { CardUseCausedDamage = use.CausedDamage })));
 }
 [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 49700)] private bool IsIssuedHaoMengConsequence(ProgramTriggerCandidate c, ProgramSkillWindowContext w) => c.SkillId == AnniversaryHaoMengComposition.Skill && c.BindingId == AnniversaryHaoMengComposition.Consequence && HaoMengConsequenceUse(w) is { AnniversaryHaoMengUse: { } o } && c.OwnerSeat == o.Source.OwnerSeat && c.SkillInstanceId == o.Source.SkillInstanceId && c.GameplayHash == o.Hash;
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 49700, SkillProgramEffectOp.ResolveAnniversaryHaoMengXiongmangConsequence)] private bool OfferHaoMengConsequence(ProgramTriggerLegalityContext c) => c.WindowContext is { } w && IsIssuedHaoMengConsequence(c.Candidate, w) && !ProgramEventHistory<AnniversaryHaoMengConsequenceEvent>().Any(e => e.UseId == w.CardUse!.ParentCardUseFrameId);
 [NativeUseValidation(49700)] private void ValidateHaoMengNative(CardUseFrame use) { if (use.AnniversaryHaoMengUse is not null && (use.Step != ResolutionFrameStep.Completed || !IsHaoMengNativeUse(use))) throw new InvalidOperationException("Xiongmang must finish the exact entire native Slash before returning."); }
 [NativeUseReturn(49700)] private void ReturnHaoMengNative(CardUseFrame use)
 {
  if (use.AnniversaryHaoMengUse is not { ProducerFrameId: > 0 } o) return;
  var f = GetActiveProgramFrame(o.ProducerFrameId); var r = f.AnniversaryHaoMeng!;
  if (!IsHaoMengNativeUse(use) || r.Stage != AnniversaryHaoMengStage.NativeSlash || r.Returned || r.UseId != use.Id || r.ActionId != o.ActionId || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id && e.CardId == use.CardId) != 1) throw new InvalidOperationException("Xiongmang lost its original once-paid producer's whole native return.");
  ReplaceRuntimeFrame(f.Id, f with { AnniversaryHaoMeng = r with { Returned = true } }); AdvanceEventRulesAndQueueFact(new AnniversaryHaoMengUseReturnedEvent(f.Id, use.Id, o.ActionId));
  if (use.CardAttack is not { Active: true }) SettleHaoMengNative(GetActiveProgramFrame(f.Id));
 }
 [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 49700)] private bool ReturnHaoMengAttack() { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryHaoMeng: { Stage: AnniversaryHaoMengStage.NativeSlash, Returned: true } } f) return false; SettleHaoMengNative(f); return true; }
 private void SettleHaoMengNative(ProgramSkillFrame f)
 {
  var r = f.AnniversaryHaoMeng!; if (!r.Returned || LifecycleCardUse(r.UseId) is not null) throw new InvalidOperationException("Xiongmang must await all original native targets and material cleanup.");
  var batches = _pendingCardsMovedBatches.Where(b => b.ParentFrameId == r.UseId && b.AwaitingProgramFrameId is null && b.OriginOwnerSeat == f.OwnerSeat && b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId).ToArray(); foreach (var b in batches) AdvanceEventRulesAndQueueFact(new AnniversaryHaoMengNativeBatchEvent(f.Id, r.UseId, b.Id));
  ReplaceRuntimeTop(f = f with { AnniversaryHaoMeng = r with { Stage = AnniversaryHaoMengStage.NativeTail, After = _movementSequence, NativeBatches = batches.Select(b => b.Id).ToArray() }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); ContinueHaoMeng(f.Id);
 }
 private bool IsAnniversaryHaoMengSettlementBatch(ProgramSkillFrame f, CardMovementBatchContext b) => f.AnniversaryHaoMeng is { Stage: AnniversaryHaoMengStage.NativeTail, Returned: true } r && ValidHaoMeng(f) && r.NativeBatches.Contains(b.Id) && b.ParentFrameId == r.UseId && b.AwaitingProgramFrameId is null && b.OriginOwnerSeat == f.OwnerSeat && b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId && b.Movements.Count > 0 && b.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After) && ProgramEventHistory<AnniversaryHaoMengNativeBatchEvent>().Count(e => e == new AnniversaryHaoMengNativeBatchEvent(f.Id, r.UseId, b.Id)) == 1;
}
