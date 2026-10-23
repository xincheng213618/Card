namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsAnniversaryBaiLingYunSlashSource(long? programId, int source, int target, Card card, CardKind kind, int provider,
        CardLocation from, IReadOnlyList<Card>? physical, bool counts) => from == CardLocation.Processing && counts && source == provider &&
        (card.Kind == kind || card.Kind == CardKind.Slash && kind == CardKind.FireSlash && HasZhuqueFan(_players[source])) && IsSlashCard(kind) &&
        (physical is null || physical is [var material] && material.Id == card.Id) && _resolutionStack.LastOrDefault() is ProgramSkillFrame
        { AnniversaryBly: { Stage: AnniversaryBlyStage.IssuingUse, Plan: { } p } } f && f.Id == programId && f.OwnerSeat == source && p.CardId == card.Id &&
        p.Targets.Count > 0 && p.Targets[0] == target && _cardZones.Contains(CardLocation.Processing, card.Id) && ValidAnniversaryBly(f) && AnniversaryBlyUsePaid(f);
    private void IssueAnniversaryBlyUse(ProgramSkillFrame f)
    {
        var r = f.AnniversaryBly!; var p = r.Plan!; var actor = _players[f.OwnerSeat]; var card = GetAdvancedCard(p.CardId);
        if (r.Stage != AnniversaryBlyStage.UsePayment || !AnniversaryBlyUsePaid(f) || !_cardZones.Contains(CardLocation.Processing, card.Id))
            throw new InvalidOperationException("Linghui must use its exact privately viewed entity after its original real payment.");
        var a = AnniversaryBlyUseActions(f).Single(a => AnniversaryBlyActionMatches(a, p));
        ReplaceRuntimeTop(f = f with { AnniversaryBly = r with { Stage = AnniversaryBlyStage.IssuingUse, Before = _movementSequence, After = _movementSequence } });
        if (a.ProgramActivationId is "next-card-target-adjustment" or "next-actual-use-target-adjustment") _selectedNextCardTargetSeats = a.TargetSeats;
        if (IsSlashCard(card.Kind))
        {
            ResolveSlashCore(actor, _players[a.TargetSeats[0]], card, p.Kind, actor.Seat, countsTowardSlashLimit: true,
                usesZhuqueFan: p.Kind == CardKind.FireSlash && card.Kind == CardKind.Slash && HasZhuqueFan(actor), programSkillCardUseFrameId: f.Id, physicalSourceLocation: CardLocation.Processing); return;
        }
        var id = BeginCardUse(card, actor.Seat, a.TargetSeats, card.Kind, physicalCardIds: [card.Id],
            designatedTargetSeats: card.Kind == CardKind.BorrowedSword ? a.TargetSeats.Where((_, i) => i % 2 == 0).ToArray() : null);
        if (LifecycleCardUse(id)?.AnniversaryBlyUse is null) throw new InvalidOperationException("Linghui lost its actual declared physical use.");
        if (EquipmentCatalog.IsEquipment(card.Kind)) { if (!TryBeginEquipmentTargetPrograms(actor, card, id)) CompleteEquipmentUse(actor, card, id); return; }
        if (card.Kind == CardKind.Peach) { BeginSimpleCardUse(id, new(card.Id, SimpleCardUseEffect.Recovery)); return; }
        if (card.Kind == CardKind.Alcohol) { BeginSimpleCardUse(id, new(card.Id, SimpleCardUseEffect.Alcohol)); return; }
        if (card.Kind == CardKind.Lightning && !HasSoleTargetProtectionCapability && TryBeginDelayedCardUsePrograms(id)) return;
        var required = IsOrdinaryTrick(card.Kind) ? BuildProgramOrdinaryTrickUseOptions(actor, card.Kind, EffectiveSuit(actor, card), enforceUsePermission: true,
            actualEffectiveColor: CapturePhysicalCardColor(actor.Seat, card), hasActualColor: true, physicalCardIds: [card.Id])
            .FirstOrDefault(o => o.ActionKind == a.Kind && o.TargetCardId == a.TargetCardId && o.TargetSeats.SequenceEqual(a.TargetSeats))?.RequiredCardKind : null;
        BeginJizhiOrNullificationWindow(id, card, actor.Seat, LifecycleCardUse(id)!.TargetSeats, a.Kind, a.TargetCardId, required, card.Kind);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 43600)] private void BindAnniversaryBlyNative(CardUseDeclaredEvent fact)
    {
        if (LifecycleCardUse(fact.ResolutionId) is not { Action: { Type: CardActionType.Use } a } use) return;
        var index = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame { AnniversaryBly: { Stage: AnniversaryBlyStage.IssuingUse, Plan: { } p } r } f) return;
        if (!ValidAnniversaryBly(f) || !AnniversaryBlyUsePaid(f) || use.CardId != p.CardId || use.CardKind != p.Kind || a.ActorSeat != f.OwnerSeat || a.ProviderSeat != f.OwnerSeat ||
            a.PhysicalCards is not [{ CardId: var material, From: var from }] || material != p.CardId || from != CardLocation.Processing ||
            !a.TargetSeats.SequenceEqual(GetImplicitSelfCardUseTargets(p.Kind, f.OwnerSeat, p.Targets)) || a.ConversionChain.Count != 0)
            throw new InvalidOperationException("Linghui changed its actual private entity, actor/provider, printed kind or native targets.");
        var origin = new AnniversaryBlyNative(f.Id, f.InstructionIndex, r.Source, r.Hash, r.Turn, p.CardId, use.Id, a.ActionId);
        UpdateLifecycleCardUse(use.Id, u => u with { AnniversaryBlyUse = origin });
        ReplaceRuntimeFrame(f.Id, f with { AnniversaryBly = r with { Stage = AnniversaryBlyStage.NativeUse, UseId = use.Id, ActionId = a.ActionId } });
        AdvanceEventRulesAndQueueFact(new AnniversaryBlyNativeIssuedEvent(origin));
    }
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.MaterialPayment, 43600)]
    private bool PayAnniversaryBlyProcessing(IReadOnlyList<Card> cards, int provider, CardLocation? from, CardMoveReason reason) =>
        from == CardLocation.Processing && reason == CardMoveReasons.Use && cards is [var card] && _resolutionStack.LastOrDefault() is CardUseFrame { AnniversaryBlyUse: { } o } u &&
        provider == o.Source.OwnerSeat && card.Id == o.CardId && _cardZones.Contains(CardLocation.Processing, card.Id) && IsAnniversaryBlyNative(u);
    private bool IsAnniversaryBlyNative(CardUseFrame use) => use.AnniversaryBlyUse is { } o && use.Id == o.UseId && use.CardId == o.CardId &&
        use.Action is { Type: CardActionType.Use } a && a.ActionId == o.ActionId && a.ProviderSeat == o.Source.OwnerSeat && a.PhysicalCards is [{ CardId: var id, From: var from }] &&
        id == o.CardId && from == CardLocation.Processing && a.EffectiveKind == use.CardKind &&
        _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == o.FrameId) is { AnniversaryBly: { } r } parent && ValidAnniversaryBly(parent) &&
        r.Source == o.Source && r.Hash == o.Hash && r.UseId == use.Id && r.ActionId == a.ActionId && r.Plan?.CardId == id && AnniversaryBlyUsePaid(parent) &&
        ProgramEventHistory<AnniversaryBlyNativeIssuedEvent>().Count(e => e.Origin == o) == 1;
    [NativeUseValidation(43600)] private void ValidateAnniversaryBlyNativeReturn(CardUseFrame use)
    { if (use.AnniversaryBlyUse is not null && (use.Step != ResolutionFrameStep.Completed || !IsAnniversaryBlyNative(use))) throw new InvalidOperationException("Linghui must retire its original complete physical use."); }
    [NativeUseReturn(43600)] private void ReturnAnniversaryBlyNative(CardUseFrame use)
    {
        if (use.AnniversaryBlyUse is not { } o) return;
        var f = GetActiveProgramFrame(o.FrameId); var r = f.AnniversaryBly!;
        if (!IsAnniversaryBlyNative(use) || r.Returned || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id && e.CardId == o.CardId) != 1)
            throw new InvalidOperationException("Linghui cannot return a different or unfinished use or pay its reward twice.");
        ReplaceRuntimeFrame(f.Id, f with { AnniversaryBly = r with { Returned = true } }); AdvanceEventRulesAndQueueFact(new AnniversaryBlyNativeReturnedEvent(f.Id, use.Id, o.ActionId));
        if (use.CardAttack is not { Active: true }) SettleAnniversaryBlyNative(GetActiveProgramFrame(f.Id));
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 43600)] private bool ReturnAnniversaryBlyAttack()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryBly: { Stage: AnniversaryBlyStage.NativeUse, Returned: true } } f) return false;
        SettleAnniversaryBlyNative(f); return true;
    }
    private void SettleAnniversaryBlyNative(ProgramSkillFrame f)
    {
        var r = f.AnniversaryBly!; if (!r.Returned || LifecycleCardUse(r.UseId) is not null) throw new InvalidOperationException("Linghui must await the actual native use tail.");
        var batches = _pendingCardsMovedBatches.Where(b => b.ParentFrameId == r.UseId && b.AwaitingProgramFrameId is null && b.OriginOwnerSeat == f.OwnerSeat && b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId).ToArray();
        foreach (var b in batches) AdvanceEventRulesAndQueueFact(new AnniversaryBlyNativeBatchEvent(f.Id, r.UseId, b.Id, b.Movements.Count));
        ReplaceRuntimeTop(f = f with { AnniversaryBly = r with { Stage = AnniversaryBlyStage.NativeTail, After = _movementSequence, NativeBatches = batches.Select(b => b.Id).ToArray() }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        ContinueAnniversaryBly(f.Id);
    }
}
