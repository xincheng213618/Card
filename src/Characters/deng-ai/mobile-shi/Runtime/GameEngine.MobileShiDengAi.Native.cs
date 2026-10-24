namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.CaptureMovement, 50300)]
    private void CaptureShiDaEquipmentLoss(CardMovementBatchContext batch)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMobileShiDengAiGain) ||
            _resolutionStack.LastOrDefault() is not CardUseFrame { Action: { Type: CardActionType.Use } action } use || batch.ParentFrameId != use.Id ||
            !EquipmentCatalog.IsEquipment(use.CardKind) || action.EffectiveKind != use.CardKind) return;
        if (use.ShiDengAiEquipmentLoss is { } captured)
        {
            if (!ValidShiDaEquipmentLoss(use, captured)) throw new InvalidOperationException("Tuntian's equipment transfer lost its original native material.");
            var placements = batch.Movements.Where(m => m.CardId == captured.CardId && m.Sequence > captured.PaymentSequence && m.From == CardLocation.Processing &&
                m.To == CardLocation.Equipment(captured.Source.OwnerSeat) && m.Reason == CardMoveReasons.EquipmentEnter).ToArray();
            if (placements.Length == 0) return;
            if (placements.Length != 1 || captured.PlacementSequence is not null || captured.Settled) throw new InvalidOperationException("A native equipment material cannot enter its original owner's slot twice.");
            var placed = placements[0];
            UpdateLifecycleCardUse(use.Id, current => current with { ShiDengAiEquipmentLoss = captured with { PlacementSequence = placed.Sequence } });
            AdvanceEventRulesAndQueueFact(new ShiDaEquipmentPlacedEvent(use.Id, action.ActionId, captured.CardId, captured.PaymentSequence, placed.Sequence, batch.Id));
            return;
        }
        if (action.PhysicalCards is not [var cost] || use.CardId != cost.CardId || !use.PhysicalCardIds!.SequenceEqual([cost.CardId]) ||
            cost.From.OwnerSeat is not { } owner || owner != action.ActorSeat || owner != action.ProviderSeat || !ShiDaOwns(cost.From, owner) ||
            ShiDaDamageCard(cost.CardKind) || batch.Movements is not [var entry] || entry.CardId != cost.CardId || entry.CardKind != cost.CardKind || entry.From != cost.From ||
            entry.To != CardLocation.Processing || entry.Reason != CardMoveReasons.EquipmentUse || !_cardMovements.Contains(entry)) return;
        var source = GetSkillBindingShard(_players[owner]).ProgramInstances.Where(i => i.SkillId == ShiDaTuntian).OrderBy(i => i.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault();
        if (source is null) return;
        var origin = new CardConversionSource(ShiDaTuntian, "non-damage-loss", owner, source.SkillInstanceId);
        var hash = _contentRegistry.GetSkill(ShiDaTuntian).Program!.GameplayHash;
        var receipt = new ShiDaEquipmentLossReceipt(use.Id, action.ActionId, origin, hash, batch.Id, cost.CardId, cost.CardKind, cost.From, entry.Sequence);
        UpdateLifecycleCardUse(use.Id, current => current with { ShiDengAiEquipmentLoss = receipt });
        AdvanceEventRulesAndQueueFact(new ShiDaEquipmentCostCapturedEvent(use.Id, action.ActionId, origin, hash, batch.Id, cost.CardId, cost.CardKind, cost.From, entry.Sequence));
    }
    private bool IsShiDaDeferredEquipmentEntry(CardMovementBatchContext batch, CardMovementRecord movement) =>
        ProgramEventHistory<ShiDaEquipmentCostCapturedEvent>().Any(e => e.BatchId == batch.Id && e.UseId == batch.ParentFrameId && e.PaymentSequence == movement.Sequence &&
            e.CardId == movement.CardId && e.PrintedKind == movement.CardKind && e.From == movement.From && movement.To == CardLocation.Processing && movement.Reason == CardMoveReasons.EquipmentUse &&
            e.Source.OwnerSeat == movement.From.OwnerSeat && e.Source.SkillId == ShiDaTuntian && _contentRegistry.GetSkill(ShiDaTuntian).Program?.GameplayHash == e.Hash &&
            (LifecycleCardUse(e.UseId) is { Action: { } action, ShiDengAiEquipmentLoss: { } receipt } ? action.ActionId == e.ActionId && receipt.ActionId == e.ActionId && receipt.PaymentSequence == e.PaymentSequence && ValidShiDaEquipmentLoss(LifecycleCardUse(e.UseId)!, receipt) :
                ProgramEventHistory<ShiDaEquipmentLossSettledEvent>().Any(s => s.UseId == e.UseId && s.ActionId == e.ActionId && s.CardId == e.CardId && s.PaymentSequence == e.PaymentSequence)) &&
            ProgramEventHistory<CardUseDeclaredEvent>().Any(d => d.ResolutionId == e.UseId && d.CardId == e.CardId && EquipmentCatalog.IsEquipment(d.CardKind) && d.SourceSeat == e.Source.OwnerSeat));
    private bool ValidShiDaEquipmentLoss(CardUseFrame use, ShiDaEquipmentLossReceipt receipt) =>
        receipt.UseId == use.Id && use.Action is { Type: CardActionType.Use, PhysicalCards: [var cost] } action && receipt.ActionId == action.ActionId &&
        EquipmentCatalog.IsEquipment(use.CardKind) && action.EffectiveKind == use.CardKind && use.CardId == receipt.CardId && use.PhysicalCardIds is [var id] && id == receipt.CardId &&
        cost.CardId == receipt.CardId && cost.CardKind == receipt.PrintedKind && cost.From == receipt.From && receipt.From.OwnerSeat == receipt.Source.OwnerSeat &&
        action.ProviderSeat == receipt.Source.OwnerSeat && ShiDaOwns(receipt.From, receipt.Source.OwnerSeat) && !ShiDaDamageCard(receipt.PrintedKind) &&
        receipt.Source.SkillId == ShiDaTuntian && receipt.Source.BindingId == "non-damage-loss" && !string.IsNullOrWhiteSpace(receipt.Source.SkillInstanceId) &&
        _contentRegistry.GetSkill(ShiDaTuntian).Program?.GameplayHash == receipt.Hash &&
        ProgramEventHistory<ShiDaEquipmentCostCapturedEvent>().Count(e => e == new ShiDaEquipmentCostCapturedEvent(receipt.UseId, receipt.ActionId, receipt.Source, receipt.Hash, receipt.BatchId,
            receipt.CardId, receipt.PrintedKind, receipt.From, receipt.PaymentSequence)) == 1 &&
        _cardMovements.Any(m => m.Sequence == receipt.PaymentSequence && m.CardId == receipt.CardId && m.CardKind == receipt.PrintedKind && m.From == receipt.From && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.EquipmentUse) &&
        (receipt.PlacementSequence is not { } sequence || ProgramEventHistory<ShiDaEquipmentPlacedEvent>().Count(e => e.UseId == use.Id && e.ActionId == action.ActionId && e.CardId == receipt.CardId &&
            e.PaymentSequence == receipt.PaymentSequence && e.PlacementSequence == sequence) == 1 && _cardMovements.Any(m => m.Sequence == sequence && m.Sequence > receipt.PaymentSequence &&
                m.CardId == receipt.CardId && m.From == CardLocation.Processing && m.To == CardLocation.Equipment(receipt.Source.OwnerSeat) && m.Reason == CardMoveReasons.EquipmentEnter));
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 50300)]
    private void SettleShiDaEquipmentLoss(IGameEvent payload)
    {
        if (payload is not CardUseFinishedEvent completed || LifecycleCardUse(completed.ResolutionId) is not { ShiDengAiEquipmentLoss: { } receipt } use) return;
        if (_resolutionStack.LastOrDefault()?.Id != use.Id || use.Step != ResolutionFrameStep.Completed || receipt.Settled || !ValidShiDaEquipmentLoss(use, receipt))
            throw new InvalidOperationException("Tuntian's equipment net loss must settle once on its original completed native Use.");
        var retained = receipt.PlacementSequence is not null && ProgramEventHistory<EquipmentChangedEvent>().Any(e => e.ResolutionId == use.Id && e.CardId == receipt.CardId && e.PlayerSeat == receipt.Source.OwnerSeat);
        UpdateLifecycleCardUse(use.Id, current => current with { ShiDengAiEquipmentLoss = receipt = receipt with { Settled = true } });
        AdvanceEventRulesAndQueueFact(new ShiDaEquipmentLossSettledEvent(use.Id, receipt.ActionId, receipt.CardId, receipt.PaymentSequence, receipt.PlacementSequence, !retained));
        if (!retained) CreditShiDaEquipmentLoss(use.Id);
    }
    private void CreditShiDaEquipmentLoss(long useId)
    {
        if (LifecycleCardUse(useId) is not { Step: ResolutionFrameStep.Completed, ShiDengAiEquipmentLoss: { Settled: true } receipt } use ||
            _resolutionStack.LastOrDefault()?.Id != useId || !ValidShiDaEquipmentLoss(use, receipt) ||
            ProgramEventHistory<ShiDaEquipmentLossSettledEvent>().Count(e => e == new ShiDaEquipmentLossSettledEvent(use.Id, receipt.ActionId, receipt.CardId, receipt.PaymentSequence, receipt.PlacementSequence, true)) != 1 ||
            ProgramEventHistory<ShiDaChargeChangedEvent>().Any(e => e.FrameId == useId && e.Source == receipt.Source && e.MovementBatchId == receipt.BatchId))
            throw new InvalidOperationException("A deferred equipment loss cannot borrow another native frame, source or completed credit.");
        ChangeShiDaCharge(use.Id, receipt.Source, receipt.Hash, 1, 0, receipt.BatchId);
    }
    [NativeUseValidation(50300)]
    private void ValidateShiDaEquipmentLossReturn(CardUseFrame use)
    {
        if (use.ShiDengAiEquipmentLoss is not { } receipt) return;
        if (!receipt.Settled || !ValidShiDaEquipmentLoss(use, receipt) || ProgramEventHistory<ShiDaEquipmentLossSettledEvent>().Count(e =>
            e.UseId == use.Id && e.ActionId == receipt.ActionId && e.CardId == receipt.CardId && e.PaymentSequence == receipt.PaymentSequence && e.PlacementSequence == receipt.PlacementSequence) != 1)
            throw new InvalidOperationException("A native equipment Use cannot retire an unresolved Tuntian material transfer.");
    }
    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 50300)]
    private bool ShiDaJixiDistance(ProgramCardDistanceQuery query)
    {
        if (query.Kind != CardKind.Snatch || query.PhysicalCardIds is { Count: > 0 }) return false;
        var frame = query.CardUseFrameId is { } id && LifecycleCardUse(id)?.VirtualOrdinaryTrickOrigin is { SkillId: ShiDaJixi } origin
            ? _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == origin.ParentProgramFrameId) : _resolutionStack.LastOrDefault() as ProgramSkillFrame;
        return frame is { ShiDengAi: { Op: SkillProgramEffectOp.ResolveMobileShiDengAiJixi, Paid: true } r } && ValidShiDa(frame) && frame.OwnerSeat == query.Actor.Seat &&
            r.Stage is ShiDaStage.Targets or ShiDaStage.NativeUse && r.History.Contains(query.Target.Seat);
    }
    private IReadOnlyList<int> ShiDaSnatchTargets(ProgramSkillFrame f) => BuildProgramOrdinaryTrickUseOptions(_players[f.OwnerSeat], CardKind.Snatch,
        Suit.None, enforceUsePermission: true, beneficiaryShieldSuit: Suit.None, actualEffectiveColor: null, hasActualColor: true, physicalCardIds: [])
        .SelectMany(option => option.TargetSeats).Where(seat => f.ShiDengAi!.History.Contains(seat)).Distinct().ToArray();
    private void IssueShiDaSnatch(ProgramSkillFrame f)
    {
        var r = f.ShiDengAi!;
        if (!ValidShiDa(f) || r.Stage != ShiDaStage.Targets || !r.Paid || r.Targets.Count == 0 || r.Targets.Distinct().Count() != r.Targets.Count || r.Targets.Any(seat => !ShiDaSnatchTargets(f).Contains(seat)))
            throw new InvalidOperationException("Jixi must use one Snatch on its selected legal historical targets after its original real discard.");
        var origin = new ProgramVirtualOrdinaryTrickOrigin(f.Id, f.InstructionIndex, r.WindowId, f.OwnerSeat, f.OwnerSeat, r.Targets[0], f.SkillId, f.TriggerId!, f.SkillInstanceId,
            f.GameplayHash, r.Turn, CardKind.Snatch, SkillProgramConditionKind.Always, null, null);
        var expected = _resolutionSequence + 1;
        ReplaceRuntimeTop(f = f with { ShiDengAi = r with { Stage = ShiDaStage.NativeUse, NativeUseId = expected, Origin = origin } });
        var card = new Card(0, CardKind.Snatch, Suit.None, 0);
        var id = BeginCardUse(card, f.OwnerSeat, r.Targets, CardKind.Snatch, physicalCardIds: [], virtualOrdinaryTrickOrigin: origin);
        if (id != expected) throw new InvalidOperationException("Jixi's native Snatch changed its reserved owner.");
        var use = LifecycleCardUse(id)!; origin = use.VirtualOrdinaryTrickOrigin!;
        var index = _resolutionStack.FindIndex(frame => frame.Id == id);
        if (index <= 0 || _resolutionStack[index - 1] is not ProgramSkillFrame { ShiDengAi: { Stage: ShiDaStage.NativeUse } parentReceipt } parent || parent.Id != f.Id ||
            parentReceipt.Source != r.Source || parentReceipt.Hash != r.Hash || parentReceipt.NativeUseId != id || !ValidShiDa(parent))
            throw new InvalidOperationException("Jixi's declared native child must remain directly above its exact frozen parent.");
        ReplaceRuntimeFrame(f.Id, parent with { ShiDengAi = parent.ShiDengAi! with { Origin = origin, NativeActionId = use.Action!.ActionId } });
        AdvanceEventRulesAndQueueFact(new ShiDaSnatchIssuedEvent(f.Id, id, use.Action!.ActionId, origin));
        BeginJizhiOrNullificationWindow(id, card, f.OwnerSeat, use.TargetSeats, LegalActionKind.Snatch, playedCardKind: CardKind.Snatch);
    }
    private bool ValidShiDaVirtualSnatchUse(CardUseFrame use)
    {
        if (use.VirtualOrdinaryTrickOrigin is not { SkillId: ShiDaJixi, OutputKind: CardKind.Snatch } origin || use.CardId != 0 || use.CardKind != CardKind.Snatch ||
            use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0, ConversionChain.Count: 0,
                EffectiveSuit: Suit.None, EffectiveRank: 0, RequesterSeat: null, ResponderSeat: null, OpponentSeat: null } action || action.ActionId != origin.CardActionId || action.ProviderSeat != origin.InitialActorSeat ||
            !action.TargetSeats.SequenceEqual(use.TargetSeats) || origin.ConditionKind != SkillProgramConditionKind.Always || origin.PindianBind is not null || origin.FrozenSourceWon is not null ||
            _resolutionStack.SingleOrDefault(x => x.Id == origin.ParentProgramFrameId) is not ProgramSkillFrame { ShiDengAi: { Stage: ShiDaStage.NativeUse } r } f ||
            !ValidShiDa(f) || r.Op != SkillProgramEffectOp.ResolveMobileShiDengAiJixi || f.SkillId != ShiDaJixi || f.TriggerId != "ending-jixi" || !r.Paid ||
            origin.InstructionIndex != f.InstructionIndex || origin.OwnerSeat != f.OwnerSeat || origin.InitialActorSeat != f.OwnerSeat || origin.BindingId != f.TriggerId || origin.SkillInstanceId != f.SkillInstanceId ||
            origin.GameplayHash != f.GameplayHash || origin.TurnNumber != r.Turn || origin.LifecycleFrameId != r.WindowId || r.NativeUseId != use.Id ||
            r.Origin is not { } saved || (saved with { CardActionId = 0 }) != (origin with { CardActionId = 0 }) || r.Targets.Count == 0 || origin.InitialTargetSeat != r.Targets[0] || r.NativeActionId != 0 && r.NativeActionId != action.ActionId)
            return false;
        var index = _resolutionStack.FindIndex(frame => frame.Id == use.Id);
        return index > 0 && _resolutionStack[index - 1].Id == f.Id && (ProgramEventHistory<CardUseDeclaredEvent>().Any(e => e.ResolutionId == use.Id)
            ? ShownEntityUseActorMatches(use, f.OwnerSeat, f.OwnerSeat) : action.ActorSeat == f.OwnerSeat);
    }
    private bool TryReturnShiDaVirtualSnatch(CardUseFrame use)
    {
        if (use.VirtualOrdinaryTrickOrigin is not { SkillId: ShiDaJixi } origin) return false;
        var f = GetActiveProgramFrame(origin.ParentProgramFrameId); var r = f.ShiDengAi!;
        if (!ValidShiDa(f) || r.Stage != ShiDaStage.NativeUse || use.Step != ResolutionFrameStep.Completed || LifecycleCardUse(use.Id) is not null ||
            r.NativeUseId != use.Id || r.NativeActionId != use.Action?.ActionId || r.Origin != origin || ProgramEventHistory<ShiDaSnatchReturnedEvent>().Any(e => e.UseId == use.Id))
            throw new InvalidOperationException("Jixi's whole native Snatch must return once to its same paid parent.");
        var batches = _pendingCardsMovedBatches.Where(b => b.ParentFrameId == use.Id && b.AwaitingProgramFrameId is null && b.OriginOwnerSeat == f.OwnerSeat && b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId).ToArray();
        foreach (var batch in batches) AdvanceEventRulesAndQueueFact(new ShiDaNativeBatchEvent(f.Id, use.Id, batch.Id, batch.ParentBatchId, batch.Movements.Count));
        ReplaceRuntimeTop(f = f with { ShiDengAi = r with { Stage = ShiDaStage.NativeChildren, NativeBatches = batches.Select(b => b.Id).ToArray(), Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new ShiDaSnatchReturnedEvent(f.Id, use.Id, r.NativeActionId)); ContinueShiDa(f.Id); return true;
    }
}
