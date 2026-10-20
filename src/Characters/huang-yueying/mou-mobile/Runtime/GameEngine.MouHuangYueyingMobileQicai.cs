namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IEnumerable<(Card Card, CardLocation From)> MouHyEquipmentChoices(int owner, int target)
    {
        if (!IsValidPlayerSeat(owner) || !IsValidPlayerSeat(target) || owner == target || !_players[owner].IsAlive || !_players[target].IsAlive || _players[target].EquipmentAreaAbolished) return [];
        return GetHand(_players[owner]).Select(c => (Card: c, From: CardLocation.Hand(owner)))
            .Concat(_cardZones.CardsAt(CardLocation.DiscardPile).Select(c => (Card: c, From: CardLocation.DiscardPile)))
            .Where(p => EquipmentCatalog.IsEquipment(p.Card.Kind) && !p.Card.IsGeneralWeapon &&
                !IsActiveProgramSourceEquipmentCard(owner, MouHuangYueyingMobileComposition.Qicai, GetRuntimeSkillInstanceId(_players[owner], MouHuangYueyingMobileComposition.Qicai), p.Card) &&
                _players[target].EquipmentSlotCapacity(EquipmentCatalog.Get(p.Card.Kind).Slot) > 0).ToArray();
    }
    private int MouHyQicaiPhaseUses(int owner, ActualDiscardRecoveryPhaseKey phase) => ProgramEventHistory<MouHuangYueyingQicaiReservedEvent>().Count(e => e.Source.OwnerSeat == owner && e.Phase == phase);
    [ProgramPhaseUsedProjection(90)]
    private int MouHyQicaiProjectedUses(int owner, string skill, int fallback) => skill == MouHuangYueyingMobileComposition.Qicai && CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p ? MouHyQicaiPhaseUses(owner, p) : fallback;
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 31500, SkillProgramEffectOp.PlaceMouHuangYueyingQicaiEquipment)]
    private bool CanMouHyQicai(ProgramActivationLegalityContext c) => CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p &&
        p.ActorSeat == c.OwnerSeat && _currentSeat == c.OwnerSeat && _phase == TurnPhase.Play && MouHyQicaiPhaseUses(c.OwnerSeat, p) == 0;
    private SkillProgramStepOutcome BeginMouHyQicai(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouHuangYueyingMobile is not null) { ContinueMouHy(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.SkillId != MouHuangYueyingMobileComposition.Qicai || f.TriggerId is not null || f.ActivationId != "place-equipment" || f.InstructionIndex != 1 ||
            f.SelectedTargetSeats is not [var target] || f.SelectedCardIds.Count != 0 || CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } p ||
            p.ActorSeat != f.OwnerSeat || MouHyQicaiPhaseUses(f.OwnerSeat, p) != 0 || !MouHyEquipmentChoices(f.OwnerSeat, target).Any())
            throw new InvalidOperationException("Qicai needs its actual-Play once native equipment placement entry.");
        var source = MouHySource(f);
        ReplaceRuntimeTop(f = f with { MouHuangYueyingMobile = new(source, f.GameplayHash, SkillProgramEffectOp.PlaceMouHuangYueyingQicaiEquipment,
            MouHuangYueyingMobileStage.Choosing, _turnNumber, _turnProgression.OwnerSeat, 0, target, p) });
        AdvanceEventRulesAndQueueFact(new MouHuangYueyingQicaiReservedEvent(f.Id, source, f.GameplayHash, p, target));
        PublishParticipantHandChoice(f, f.OwnerSeat, MouHyQicaiChoices(f), "选择手牌或弃牌堆的一张装备牌置入目标装备区。", target);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> MouHyQicaiChoices(ProgramSkillFrame f) => Array.AsReadOnly(MouHyEquipmentChoices(f.OwnerSeat, f.MouHuangYueyingMobile!.RecipientSeat)
        .Select(p => new PromptChoice(new($"mou-huang-yueying.qicai.{f.Id}.{p.Card.Id}"), $"置入【{p.Card.DisplayName}】", [p.Card.Id], [f.MouHuangYueyingMobile.RecipientSeat],
            new Dictionary<string, string> { ["program-action"] = "mou-huang-yueying-qicai", ["frame-id"] = f.Id.ToString(), ["source-zone"] = p.From.Zone.ToString() })).ToArray());
    [ProgramAnswer("mou-huang-yueying-qicai")]
    private void AnswerMouHyQicai(PromptChoice c)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouHuangYueyingMobile: { Stage: MouHuangYueyingMobileStage.Choosing } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([c], MouHyQicaiChoices(f).Where(p => p.Id == c.Id).ToArray()))
            throw new InvalidOperationException("Qicai requires its current real Hand/public-discard entity and original recipient.");
        ClearPendingDecision();
        if (!HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || !_players[f.OwnerSeat].IsAlive || !_players[r.RecipientSeat].IsAlive)
        { FinishMouHy(f); return; }
        var pair = MouHyEquipmentChoices(f.OwnerSeat, r.RecipientSeat).Single(p => p.Card.Id == c.Cards.Single()); var card = pair.Card; var from = pair.From;
        var slot = EquipmentCatalog.Get(card.Kind).Slot; var current = GetEquipment(_players[r.RecipientSeat]).Where(x => EquipmentCatalog.Get(x.Kind).Slot == slot).ToArray();
        var replaced = current.Length >= _players[r.RecipientSeat].EquipmentSlotCapacity(slot) ? current[0] : null; var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { MouHuangYueyingMobile = r = r with { Stage = MouHuangYueyingMobileStage.MovementChildren, Paid = true, CardId = card.Id, From = from, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (replaced is not null) { CopyFirstReplacedWeapon(card, replaced); MoveCard(replaced, CardLocation.Equipment(r.RecipientSeat), CardLocation.DiscardPile, CardMoveReasons.EquipmentReplace); }
        MoveCard(card, from, CardLocation.Equipment(r.RecipientSeat), CardMoveReasons.EquipmentEnter);
        var paid = _cardMovements.Single(m => m.CardId == card.Id && m.From == from && m.Sequence > before);
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { MouHuangYueyingMobile = f.MouHuangYueyingMobile! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new MouHuangYueyingQicaiEquipmentPlacedEvent(f.Id, r.Source, r.GameplayHash, r.RecipientSeat, card.Id, from, paid.To, paid.Sequence, replaced?.Id));
        if (paid.To == CardLocation.Equipment(r.RecipientSeat))
        {
            AdvanceEventRulesAndQueueFact(new EquipmentChangedEvent(f.Id, r.RecipientSeat, slot, card.Id, card.Kind, replaced?.Id));
            var old = CurrentMouHyQi(r.RecipientSeat);
            AdvanceEventRulesAndQueueFact(new MouHuangYueyingQiGrantedEvent(f.Id, r.Source, r.GameplayHash, r.RecipientSeat, paid.Sequence, old?.GrantId));
            SyncMouHyQiMarker(f.Id, r.RecipientSeat);
        }
        ContinueMouHy(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.PlaceMouHuangYueyingQicaiEquipment)]
    private PromptChoice AiMouHyQicai(PendingDecision p, ProgramSkillFrame f) => p.Choices.OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[f.OwnerSeat])).First();
    private MouHuangYueyingQiGrantedEvent? CurrentMouHyQi(int target) => ProgramEventHistory<MouHuangYueyingQiGrantedEvent>().LastOrDefault(e => e.RecipientSeat == target);
    private int MouHyQiRemaining(MouHuangYueyingQiGrantedEvent grant) => Math.Max(0, 3 - ProgramEventHistory<MouHuangYueyingQiGainClaimedEvent>().Count(e => e.GrantId == grant.GrantId));
    private void SyncMouHyQiMarker(long frame, int target)
    {
        var grant = CurrentMouHyQi(target); var count = grant is null ? 0 : MouHyQiRemaining(grant);
        var before = _players[target].Markers.GetValueOrDefault(PlayerMarkerKind.MouHuangYueyingQi);
        if (count == before) return;
        if (count == 0) _players[target].Markers.Remove(PlayerMarkerKind.MouHuangYueyingQi); else _players[target].Markers[PlayerMarkerKind.MouHuangYueyingQi] = count;
        AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(frame, target, PlayerMarkerKind.MouHuangYueyingQi, count - before, count, grant?.Source.OwnerSeat, "skill-program.mou-mobile:qicai.mark"));
    }
}
