namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsDuKuiFanyinSlashSource(long? programId, int source, int target, Card card, CardKind kind, int provider,
        CardLocation from, IReadOnlyList<Card>? physical, bool counts) => from == CardLocation.Processing && counts && source == provider &&
        (card.Kind == kind || card.Kind == CardKind.Slash && kind == CardKind.FireSlash && HasZhuqueFan(_players[source])) && IsSlashCard(kind) &&
        (physical is null || physical is [var material] && material.Id == card.Id) && _resolutionStack.LastOrDefault() is ProgramSkillFrame
        { DuKui: { Operation: SkillProgramEffectOp.ResolveAnniversaryDuKuiFanyin, Stage: DuKuiStage.NativeIssuing } r } f &&
        f.Id == programId && f.OwnerSeat == source && r.CardId == card.Id && r.UseTargets.Count > 0 && r.UseTargets[0] == target &&
        _cardZones.Contains(CardLocation.Processing, card.Id) && ValidDuKuiFrame(f) && DuKuiShownPaid(f);

    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 43200)]
    private bool DuKuiSlashDistance(ProgramSlashDistanceQuery q) => q.PhysicalCard is { } card && DuKuiQuery(q.Actor.Seat, card.Id) is not null;
    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 43200)]
    private bool DuKuiCardDistance(ProgramCardDistanceQuery q) => DuKuiQuery(q.Actor.Seat) is { DuKui: { } r } && GetAdvancedCard(r.CardId).Kind == q.Kind;

    private void IssueDuKuiUse(ProgramSkillFrame f, LegalAction selected)
    {
        var r = f.DuKui!; var card = GetAdvancedCard(r.CardId); var actor = _players[f.OwnerSeat];
        if (r.Stage != DuKuiStage.Choice || !DuKuiShownPaid(f) || !_cardZones.Contains(CardLocation.Processing, card.Id))
            throw new InvalidOperationException("Fanyin must use exactly its current already-public Processing entity.");
        var action = DuKuiUseActions(f).Single(a => a.Kind == selected.Kind && a.CardId == selected.CardId && a.PlayedCardKind == selected.PlayedCardKind &&
            a.TargetCardId == selected.TargetCardId && a.TargetSeats.SequenceEqual(selected.TargetSeats) && a.ProgramActivationId == selected.ProgramActivationId);
        var kind = action.PlayedCardKind ?? card.Kind;
        ReplaceRuntimeTop(f = f with { DuKui = r with { Stage = DuKuiStage.NativeIssuing, UseAction = action.Kind, UseKind = kind, TargetCardId = action.TargetCardId,
            UseTargets = GetImplicitSelfCardUseTargets(kind, actor.Seat, action.TargetSeats), Before = _movementSequence, After = _movementSequence, NativeBatches = [] } });
        if (action.ProgramActivationId is "next-card-target-adjustment" or "next-actual-use-target-adjustment") _selectedNextCardTargetSeats = action.TargetSeats;
        if (IsSlashCard(card.Kind))
        {
            ResolveSlashCore(actor, _players[action.TargetSeats[0]], card, kind, actor.Seat, countsTowardSlashLimit: true,
                usesZhuqueFan: kind == CardKind.FireSlash && card.Kind == CardKind.Slash && HasZhuqueFan(actor),
                programSkillCardUseFrameId: f.Id, physicalSourceLocation: CardLocation.Processing); return;
        }
        var useId = BeginCardUse(card, actor.Seat, action.TargetSeats, card.Kind, physicalCardIds: [card.Id],
            designatedTargetSeats: card.Kind == CardKind.BorrowedSword ? action.TargetSeats.Where((_, i) => i % 2 == 0).ToArray() : null);
        if (LifecycleCardUse(useId)?.DuKuiOrigin is null) throw new InvalidOperationException("Fanyin must freeze the true native use before resolving any effect.");
        if (EquipmentCatalog.IsEquipment(card.Kind))
        {
            if (!TryBeginEquipmentTargetPrograms(actor, card, useId)) CompleteEquipmentUse(actor, card, useId); return;
        }
        if (card.Kind == CardKind.Peach) { BeginSimpleCardUse(useId, new(card.Id, SimpleCardUseEffect.Recovery)); return; }
        if (card.Kind == CardKind.Alcohol) { BeginSimpleCardUse(useId, new(card.Id, SimpleCardUseEffect.Alcohol)); return; }
        if (card.Kind == CardKind.Lightning && !HasSoleTargetProtectionCapability && TryBeginDelayedCardUsePrograms(useId)) return;
        var required = IsOrdinaryTrick(card.Kind) ? BuildProgramOrdinaryTrickUseOptions(actor, card.Kind, EffectiveSuit(actor, card),
            enforceUsePermission: true, actualEffectiveColor: CapturePhysicalCardColor(actor.Seat, card), hasActualColor: true, physicalCardIds: [card.Id])
            .FirstOrDefault(o => o.ActionKind == action.Kind && o.TargetCardId == action.TargetCardId && o.TargetSeats.SequenceEqual(action.TargetSeats))?.RequiredCardKind : null;
        BeginJizhiOrNullificationWindow(useId, card, actor.Seat, LifecycleCardUse(useId)!.TargetSeats, action.Kind, action.TargetCardId, required, card.Kind);
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 43200)]
    private void BindDuKuiNativeUse(CardUseDeclaredEvent fact)
    {
        if (LifecycleCardUse(fact.ResolutionId) is not { Action: { Type: CardActionType.Use } action } use) return;
        var index = _resolutionStack.FindIndex(p => p.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame { DuKui: { Stage: DuKuiStage.NativeIssuing } r } f) return;
        if (!ValidDuKuiFrame(f) || !DuKuiShownPaid(f) || use.CardId != r.CardId || action.ActorSeat != f.OwnerSeat || action.ProviderSeat != f.OwnerSeat ||
            action.PhysicalCards is not [{ CardId: var material, From: var from }] || material != r.CardId || from != CardLocation.Processing ||
            !action.TargetSeats.SequenceEqual(r.UseTargets) || use.CardKind != r.UseKind || action.ConversionChain.Count != 0)
            throw new InvalidOperationException("Fanyin changed its selected revealed physical entity, true actor or accepted native targets.");
        var origin = new DuKuiNativeOrigin(f.Id, f.InstructionIndex, r.Source, r.GameplayHash, r.Iteration, r.ActualTurn, r.CardId, use.Id, action.ActionId);
        UpdateLifecycleCardUse(use.Id, u => u with { DuKuiOrigin = origin });
        ReplaceRuntimeFrame(f.Id, f with { DuKui = r with { Stage = DuKuiStage.NativeUse, UseId = use.Id, ActionId = action.ActionId } });
        AdvanceEventRulesAndQueueFact(new DuKuiNativeIssuedEvent(origin));
    }
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.MaterialPayment, 43200)]
    private bool PayDuKuiProcessingMaterial(IReadOnlyList<Card> cards, int provider, CardLocation? from, CardMoveReason reason) =>
        from == CardLocation.Processing && reason == CardMoveReasons.Use && cards is [var card] &&
        _resolutionStack.LastOrDefault() is CardUseFrame { DuKuiOrigin: { } origin } use && IsDuKuiNativeUse(use) &&
        origin.Source.OwnerSeat == provider && origin.CardId == card.Id && _cardZones.Contains(CardLocation.Processing, card.Id);

    private bool IsDuKuiNativeUse(CardUseFrame use) => use.DuKuiOrigin is { } o && use.Id == o.UseId && use.CardId == o.CardId &&
        use.Action is { Type: CardActionType.Use } a && a.ActionId == o.ActionId && a.ActorSeat == o.Source.OwnerSeat && a.ProviderSeat == a.ActorSeat &&
        a.EffectiveKind == use.CardKind && a.PhysicalCards is [{ CardId: var id, From: var from }] && id == o.CardId && from == CardLocation.Processing &&
        _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(p => p.Id == o.FrameId) is { DuKui: { } r } f &&
        ValidDuKuiFrame(f) && r.UseId == use.Id && r.ActionId == a.ActionId && r.Source == o.Source && r.GameplayHash == o.GameplayHash && r.Iteration == o.Iteration &&
        ProgramEventHistory<DuKuiNativeIssuedEvent>().Count(e => e.Origin == o) == 1;

    [NativeUseValidation(43200)]
    private void ValidateDuKuiNativeReturn(CardUseFrame use)
    {
        if (use.DuKuiOrigin is not null && (use.Step != ResolutionFrameStep.Completed || !IsDuKuiNativeUse(use)))
            throw new InvalidOperationException("Fanyin must return its own completed native physical use.");
    }
    [NativeUseReturn(43200)]
    private void ReturnDuKuiNativeUse(CardUseFrame use)
    {
        if (use.DuKuiOrigin is not { } o) return;
        var f = GetActiveProgramFrame(o.FrameId); var r = f.DuKui!;
        if (!IsDuKuiNativeUse(use) || r.NativeReturned || LifecycleCardUse(use.Id) is not null ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id && e.CardId == o.CardId) != 1)
            throw new InvalidOperationException("Fanyin cannot retire another use or return its accepted use twice.");
        ReplaceRuntimeFrame(f.Id, f with { DuKui = r with { NativeReturned = true } });
        AdvanceEventRulesAndQueueFact(new DuKuiNativeReturnedEvent(f.Id, use.Id, o.ActionId));
        if (use.CardAttack is not { Active: true }) SettleDuKuiNativeUse(GetActiveProgramFrame(f.Id));
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 43200)]
    private bool ReturnDuKuiNativeAttack()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { DuKui: { Stage: DuKuiStage.NativeUse, NativeReturned: true } } f) return false;
        SettleDuKuiNativeUse(f); return true;
    }
    private void SettleDuKuiNativeUse(ProgramSkillFrame f)
    {
        var r = f.DuKui!;
        if (!r.NativeReturned || LifecycleCardUse(r.UseId) is not null) throw new InvalidOperationException("Fanyin must await the complete original native use tail.");
        var batches = _pendingCardsMovedBatches.Where(b => b.ParentFrameId == r.UseId && b.AwaitingProgramFrameId is null &&
            b.OriginOwnerSeat == f.OwnerSeat && b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId).ToArray();
        foreach (var b in batches) AdvanceEventRulesAndQueueFact(new DuKuiNativeBatchEvent(f.Id, r.UseId, b.Id, b.Movements.Count));
        ReplaceRuntimeTop(f = f with { DuKui = r with { Stage = DuKuiStage.NativeTail, After = _movementSequence, NativeBatches = batches.Select(b => b.Id).ToArray() },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        ContinueDuKui(f.Id);
    }
}
