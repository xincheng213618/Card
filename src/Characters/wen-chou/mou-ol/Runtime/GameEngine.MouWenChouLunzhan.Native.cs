namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void IssueMouWenChouDuel(ProgramSkillFrame f, ProgramOrdinaryTrickUseOption option)
    {
        var r = f.MouWenChouOl!; var cards = MouWenChouMaterials(f);
        if (!ValidMouWenChouOl(f) || r.Stage != MouWenChouOlStage.Targets || MouWenChouCountUsed(f.OwnerSeat, cards.Length) || !MouWenChouOptions(f).Any(o => o.Id == option.Id)) throw new InvalidOperationException("Lunzhan must issue its exact current unpaid native Duel option.");
        var suitSet = cards.Select(c => EffectiveSuit(_players[f.OwnerSeat], c)).Distinct().ToArray(); var suit = suitSet.Length == 1 ? suitSet[0] : Suit.None;
        var colorSet = cards.Select(c => CapturePhysicalCardColor(f.OwnerSeat, c)).Distinct().ToArray(); var color = colorSet.Length == 1 ? colorSet[0] : null;
        var rank = cards.Length == 1 ? cards[0].Rank : 0;
        var card = cards.FirstOrDefault() ?? new(0, CardKind.Duel, Suit.None, 0);
        var origin = new MouWenChouLunzhanOrigin(f.Id, r.Source, f.GameplayHash, _resolutionSequence + 1, _cardActionSequence + 1, _turnNumber, cards.Length, card.Id, suit, rank, color);
        ReplaceRuntimeTop(f = f with { MouWenChouOl = r with { Stage = MouWenChouOlStage.Issuing, UseId = origin.UseId, ActionId = origin.ActionId, NativeTargets = option.TargetSeats, Before = _movementSequence, After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new MouWenChouLunzhanIssuedEvent(origin));
        if (option.NextActualUseAdjusted) _selectedNextCardTargetSeats = option.TargetSeats;
        var id = BeginCardUse(card, f.OwnerSeat, option.TargetSeats, CardKind.Duel, physicalCardIds: f.SelectedCardIds, conversionSource: cards.Length == 0 ? null : r.Source);
        if (id != origin.UseId || !IsMouWenChouDuel(id)) throw new InvalidOperationException("Lunzhan changed its signed native Use identity.");
        foreach (var group in cards.GroupBy(c => _cardZones.GetLocation(c.Id))) MoveCards(group.ToArray(), group.Key, CardLocation.Processing, CardMoveReasons.Use, disclosure: CardMovementDisclosure.FaceUp);
        AdvanceEventRulesAndQueueFact(new ProgramViewAsConvertedEvent(id, f.SkillId, f.ActivationId, f.OwnerSeat, f.SelectedCardIds, CardKind.Duel, true, option.TargetSeats));
        BeginJizhiOrNullificationWindow(id, card, f.OwnerSeat, option.TargetSeats, LegalActionKind.Duel, playedCardKind: CardKind.Duel);
    }
    // A signed appearance preserves the native physical group and source; it
    // does not grant distance, target-count or use-count exceptions.
    private CardActionContext PrepareMouWenChouAction(CardActionContext action)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouWenChouOl: { Stage: MouWenChouOlStage.Issuing } r } f) return action;
        var o = ProgramEventHistory<MouWenChouLunzhanIssuedEvent>().Single(e => e.Origin.ProgramFrameId == f.Id).Origin;
        if (!ValidMouWenChouOl(f) || action.ActionId != o.ActionId || action.ActorSeat != f.OwnerSeat || action.ProviderSeat != f.OwnerSeat || action.Type != CardActionType.Use || action.EffectiveKind != CardKind.Duel || !action.TargetSeats.SequenceEqual(r.NativeTargets) || !action.PhysicalCards.Select(c => c.CardId).SequenceEqual(f.SelectedCardIds) ||
            !action.ConversionChain.SequenceEqual(o.MaterialCount == 0 ? [] : [o.Source])) throw new InvalidOperationException("Lunzhan's declaration lost its original materials, participant or accepted native targets.");
        return CaptureFactionAction(new(action.ActionId, action.ParentActionId, action.Type, action.ActorSeat, action.ProviderSeat, action.RequesterSeat, action.ResponderSeat, action.OpponentSeat, action.EffectiveKind,
            action.TargetSeats, action.PhysicalCards, action.ConversionChain, action.DesignatedTargetSeats, effectiveSuit: o.Suit, effectiveRank: o.Rank, effectiveIsRed: o.IsRed));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 33100)]
    private void ObserveMouWenChouDeclaration(IGameEvent fact)
    {
        if (!HasMouWenChouOl || fact is not CardUseDeclaredEvent d || LifecycleCardUse(d.ResolutionId) is not { Action: { } action } use) return;
        var index = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (index > 0 && _resolutionStack[index - 1] is ProgramSkillFrame { MouWenChouOl: { Stage: MouWenChouOlStage.Issuing } r } f)
        {
            var o = ProgramEventHistory<MouWenChouLunzhanIssuedEvent>().Single(e => e.Origin.ProgramFrameId == f.Id).Origin;
            if (!ValidMouWenChouOl(f) || o.UseId != use.Id || o.ActionId != action.ActionId || use.CardKind != CardKind.Duel || use.CardId != o.CardId || !action.PhysicalCards.Select(c => c.CardId).SequenceEqual(f.SelectedCardIds)) throw new InvalidOperationException("Lunzhan cannot attach to another producer's declaration.");
            UpdateLifecycleCardUse(use.Id, u => u with { MouWenChouLunzhan = o });
            ReplaceRuntimeFrame(f.Id, f with { MouWenChouOl = r with { Stage = MouWenChouOlStage.NativeUse } });
        }
        if (action.Type == CardActionType.Use && action.ActorSeat == action.ProviderSeat && action.PhysicalCards.Count > 0 && action.PhysicalCards.All(c => c.From == CardLocation.Hand(action.ActorSeat)) &&
            GetHand(_players[action.ActorSeat]).Select(c => c.Id).Order().SequenceEqual(action.PhysicalCards.Select(c => c.CardId).Order()))
            UpdateLifecycleCardUse(use.Id, u => u with { MouWenChouAllHand = true });
    }
    private bool IsMouWenChouDuel(long id)
    {
        if (LifecycleCardUse(id) is not { MouWenChouLunzhan: { } o, Action: { Type: CardActionType.Use } a } use || use.Id != o.UseId || use.CardId != o.CardId || use.CardKind != CardKind.Duel || a.ActionId != o.ActionId || a.EffectiveKind != CardKind.Duel ||
            a.ProviderSeat != o.Source.OwnerSeat || a.RequesterSeat is not null || a.EffectiveSuit != o.Suit || a.EffectiveRank != o.Rank || a.EffectiveIsRed != o.IsRed || a.PhysicalCards.Count != o.MaterialCount ||
            !a.ConversionChain.SequenceEqual(o.MaterialCount == 0 ? [] : [o.Source]) || _resolutionStack.SingleOrDefault(f => f.Id == o.ProgramFrameId) is not ProgramSkillFrame { MouWenChouOl: { Stage: MouWenChouOlStage.NativeUse, NativeReturned: false } r } parent ||
            !ValidMouWenChouOl(parent) || r.UseId != id || r.ActionId != a.ActionId || r.Source != o.Source || parent.GameplayHash != o.GameplayHash || !a.PhysicalCards.Select(c => c.CardId).SequenceEqual(parent.SelectedCardIds) ||
            ProgramEventHistory<MouWenChouLunzhanIssuedEvent>().Count(e => e.Origin == o) != 1) return false;
        return ShownEntityUseActorMatches(use, o.Source.OwnerSeat, o.Source.OwnerSeat);
    }
    private bool IsMouWenChouZeroDuel(long id) => LifecycleCardUse(id) is { CardId: 0, PhysicalCardIds.Count: 0 } && IsMouWenChouDuel(id);
    private long? MouWenChouDuelProgramParent(long id) => IsMouWenChouDuel(id) ? LifecycleCardUse(id)!.MouWenChouLunzhan!.ProgramFrameId : null;
    private bool IsMouWenChouDuelAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing) => IsMouWenChouDuel(attack.ResolutionId) && LifecycleCardUse(attack.ResolutionId) is { MouWenChouLunzhan: { } o, Action: { } a } use &&
        attack.ProgramSkillCardUseFrameId == o.ProgramFrameId && attack.EffectiveCardKind == CardKind.Duel && attack.PhysicalCards.Select(c => c.Id).Order().SequenceEqual(use.PhysicalCardIds!.Order()) &&
        processing.Select(c => c.Id).Order().SequenceEqual(use.PhysicalCardIds.Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).Order()) && (o.MaterialCount == 0 ? attack.Card is null : attack.Card?.Id == o.CardId);
    private Card ReadMouWenChouUseAppearance(long id, CardAppearanceReference appearance)
    {
        if (!IsMouWenChouZeroDuel(id)) return ReadMouXuHuangUseAppearance(id, appearance);
        if (appearance.Id != 0 || appearance.Kind != CardKind.Duel || appearance.Suit != Suit.None || appearance.Rank != 0) throw new InvalidOperationException("Lunzhan's empty Duel lost its signed neutral appearance.");
        return new(0, CardKind.Duel, Suit.None, 0);
    }
    [NativeUseValidation(33100)]
    private void ValidateMouWenChouDuel(CardUseFrame use)
    { if (use.MouWenChouLunzhan is not null && (!IsMouWenChouDuel(use.Id) || use.Step != ResolutionFrameStep.Completed || use.PendingRecoveryAttempts is { Count: > 0 } || use.RecoveryPaidContinuation is not null)) throw new InvalidOperationException("Lunzhan must complete its whole original native Use and children before returning."); }
    [NativeUseReturn(33100)]
    private void ReturnMouWenChouDuel(CardUseFrame use)
    {
        if (use.MouWenChouLunzhan is not { } o) return;
        var f = GetActiveProgramFrame(o.ProgramFrameId); var r = f.MouWenChouOl!;
        if (!ValidMouWenChouOl(f) || r.UseId != use.Id || r.ActionId != o.ActionId || r.NativeReturned || LifecycleCardUse(use.Id) is not null) throw new InvalidOperationException("Lunzhan's native Use can return exactly once to its own parent.");
        var pendingAttack = use.CardAttack is { Active: true };
        var batches = _pendingCardsMovedBatches.Where(b => b.ParentFrameId == use.Id && b.AwaitingProgramFrameId is null && b.OriginOwnerSeat == f.OwnerSeat && b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId).ToArray();
        foreach (var b in batches) AdvanceEventRulesAndQueueFact(new MouWenChouNativeBatchSignedEvent(f.Id, use.Id, b.Id, b.ParentBatchId, b.Movements.Count));
        ReplaceRuntimeTop(f = f with { MouWenChouOl = r with { NativeReturned = true, AttackReturned = !pendingAttack, NativeBatchIds = batches.Select(b => b.Id).ToArray() } });
        if (!pendingAttack) CompleteMouWenChouDuel(f.Id);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 33100)]
    private bool ReturnMouWenChouDuelAttack(AttackCompletionReceipt c)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouWenChouOl: { Stage: MouWenChouOlStage.NativeUse, NativeReturned: true, AttackReturned: false } r } f || r.UseId != c.ResolutionId) return false;
        if (c.ProgramFrameId != f.Id || !ValidMouWenChouOl(f)) throw new InvalidOperationException("Lunzhan's Duel cannot return to another program.");
        ReplaceRuntimeTop(f with { MouWenChouOl = r with { AttackReturned = true } }); CompleteMouWenChouDuel(f.Id); return true;
    }
    private void CompleteMouWenChouDuel(long id)
    {
        var f = GetActiveProgramFrame(id); var r = f.MouWenChouOl!;
        if (!r.NativeReturned || !r.AttackReturned || LifecycleCardUse(r.UseId) is not null) throw new InvalidOperationException("Lunzhan cannot finish before both native returns.");
        AdvanceEventRulesAndQueueFact(new MouWenChouLunzhanReturnedEvent(f.Id, r.UseId, r.ActionId));
        ReplaceRuntimeTop(f with { MouWenChouOl = r with { Stage = MouWenChouOlStage.Children }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); ContinueMouWenChouOl(id);
    }
    private bool IsMouWenChouNativeSettlementBatch(ProgramSkillFrame f, CardMovementBatchContext b) => f.MouWenChouOl is { Stage: MouWenChouOlStage.Children, NativeReturned: true, AttackReturned: true } r && ValidMouWenChouOl(f) && r.NativeBatchIds.Contains(b.Id) && b.ParentFrameId == r.UseId && b.AwaitingProgramFrameId is null && b.Movements.Count > 0 &&
        b.OriginOwnerSeat == f.OwnerSeat && b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId && b.Movements.All(m => _cardMovements.Contains(m)) &&
        ProgramEventHistory<MouWenChouNativeBatchSignedEvent>().Count(e => e == new MouWenChouNativeBatchSignedEvent(f.Id, r.UseId, b.Id, b.ParentBatchId, b.Movements.Count)) == 1;

}
