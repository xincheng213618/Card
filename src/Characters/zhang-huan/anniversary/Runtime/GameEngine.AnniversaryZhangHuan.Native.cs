namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool SameZhangHuanAction(LegalAction a, LegalAction b) => a.Kind == b.Kind && a.CardId == b.CardId && a.PlayedCardKind == b.PlayedCardKind && a.TargetSeat == b.TargetSeat &&
        a.TargetCardId == b.TargetCardId && a.ProgramActivationId == b.ProgramActivationId && a.ConversionSource == b.ConversionSource &&
        (a.AdditionalConversionSources ?? []).SequenceEqual(b.AdditionalConversionSources ?? []) && a.TargetSeats.SequenceEqual(b.TargetSeats);
    private static bool SameZhangHuanNative(AnniversaryZhangHuanNative a, AnniversaryZhangHuanNative b) =>
        (a.FrameId, a.Instruction, a.Source, a.Hash, a.Turn, a.Actor, a.Discard, a.UseId, a.ActionId) ==
        (b.FrameId, b.Instruction, b.Source, b.Hash, b.Turn, b.Actor, b.Discard, b.UseId, b.ActionId) && SameZhangHuanAction(a.Action, b.Action);
    private IReadOnlyList<int> ZhangHuanExpectedTargets(AnniversaryZhangHuanNative n)
    {
        var a = n.Action; var actor = _players[n.Actor]; var card = ApplyProgramUseAppearance(actor, GetAdvancedCard(n.Discard.CardId), null);
        if (GuanduExpectedNativeTargets(actor, card, a) is { } guandu) return guandu;
        if (DragonBoatExpectedNativeTargets(actor, card, a) is { } dragon) return dragon;
        if (a.Kind == LegalActionKind.Peach) return [actor.Seat];
        if (a.Kind is not (LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage or LegalActionKind.PeachGarden or LegalActionKind.FiveGrains))
            return GetImplicitSelfCardUseTargets(a.PlayedCardKind ?? card.Kind, actor.Seat, a.TargetSeats);
        var kind = a.PlayedCardKind ?? card.Kind; var offensive = a.Kind is LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage;
        var seats = Enumerable.Range(offensive ? 1 : 0, _playerCount - (offensive ? 1 : 0)).Select(i => _players[TableStepSeat(actor.Seat, i)])
            .Where(p => p.IsAlive && (!offensive || !HasCardPolicy(p, SkillProgramCardPolicyKind.ExcludeGlobalTarget, kind)) && !IsCardTargetProhibited(p, kind, card.Suit, SuitColor(EffectiveSuit(actor, card))) &&
                !HasBeneficiarySuitShield(actor.Seat, p.Seat, EffectiveSuit(actor, card))).Select(p => p.Seat).ToArray();
        if (!offensive) seats = ApplyTurnCardGroupTargetRestrictions(actor, seats).ToArray();
        if (a.Kind == LegalActionKind.FiveGrains) seats = seats.Take(_cardZones.Count(CardLocation.DrawPile) + _cardZones.Count(CardLocation.DiscardPile)).ToArray();
        return Array.AsReadOnly(seats);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 46300)] private void BindZhangHuanUse(CardUseDeclaredEvent fact)
    {
        if (LifecycleCardUse(fact.ResolutionId) is not { Action: { Type: CardActionType.Use } a } use) return;
        var index = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame { AnniversaryZhangHuan: { Stage: AnniversaryZhangHuanStage.IssuingUse, Native: { UseId: 0, ActionId: 0 } n } r } f) return;
        if (!ValidZhangHuan(f) || use.CardId != n.Discard.CardId || use.SourceSeat != f.OwnerSeat || use.CardKind != (n.Action.PlayedCardKind ?? n.Discard.Kind) || a.ActorSeat != f.OwnerSeat || a.ProviderSeat != f.OwnerSeat ||
            a.RequesterSeat is not null || a.ConversionChain.Count != 0 || a.PhysicalCards is not [{ CardId: var material, From: var from }] || material != n.Discard.CardId || from != CardLocation.DiscardPile ||
            !_cardZones.Contains(from, material) || !ZhangHuanDiscards().Contains(n.Discard) || _cardMovements.LastOrDefault(m => m.CardId == material)?.Sequence != n.Discard.Sequence ||
            !use.TargetSeats.SequenceEqual(ZhangHuanExpectedTargets(n)))
            throw new InvalidOperationException("Shuguo changed its actual discarded entity, discard executor, native actor or accepted targets.");
        var bound = n with { UseId = use.Id, ActionId = a.ActionId };
        ReplaceRuntimeFrame(f.Id, f with { AnniversaryZhangHuan = r with { Stage = AnniversaryZhangHuanStage.NativeUse, Native = bound } });
        UpdateLifecycleCardUse(use.Id, u => u with { AnniversaryZhangHuanUse = bound });
        AdvanceEventRulesAndQueueFact(new AnniversaryZhangHuanUseIssuedEvent(f.Id, use.Id, a.ActionId, material, n.Discard.Sequence, n.Discard.ExecutorSeat));
    }
    private bool IsZhangHuanNative(CardUseFrame use) => use.AnniversaryZhangHuanUse is { } n && use.Id == n.UseId && use.CardId == n.Discard.CardId && use.Action is { Type: CardActionType.Use } a &&
        a.ActionId == n.ActionId && a.ProviderSeat == n.Actor && ShownEntityUseActorMatches(use, n.Actor, n.Actor) &&
        a.EffectiveKind == (n.Action.PlayedCardKind ?? n.Discard.Kind) && a.PhysicalCards is [{ CardId: var id, From: var from }] && id == n.Discard.CardId && from == CardLocation.DiscardPile &&
        use.PhysicalCardIds is [var physical] && physical == id &&
        _resolutionStack.SingleOrDefault(f => f.Id == n.FrameId) is ProgramSkillFrame { AnniversaryZhangHuan.Native: { } pending } parent && ValidZhangHuan(parent) && SameZhangHuanNative(n, pending) &&
        ProgramEventHistory<AnniversaryZhangHuanUseIssuedEvent>().Count(e => e == new AnniversaryZhangHuanUseIssuedEvent(n.FrameId, n.UseId, n.ActionId, id, n.Discard.Sequence, n.Discard.ExecutorSeat)) == 1;
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit, 46300)] private bool ZhangHuanEndingSlashDebit(long id, int actor) =>
        LifecycleCardUse(id) is { AnniversaryZhangHuanUse.Actor: var owner } use && owner == actor && IsZhangHuanNative(use);
    [NativeUseValidation(46300)] private void ValidateZhangHuanNativeReturn(CardUseFrame use)
    {
        if (use.AnniversaryZhangHuanUse is not null && (use.Step != ResolutionFrameStep.Completed || !IsZhangHuanNative(use)))
            throw new InvalidOperationException("Shuguo cannot retire a different or incomplete actual discarded-card use.");
    }
    [NativeUseReturn(46300)] private void ReturnZhangHuanUse(CardUseFrame use)
    {
        if (use.AnniversaryZhangHuanUse is not { } n) return;
        var f = GetActiveProgramFrame(n.FrameId); var r = f.AnniversaryZhangHuan!;
        if (!IsZhangHuanNative(use) || r.Returned || r.Used.Contains(n.Discard.CardId) || LifecycleCardUse(use.Id) is not null ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id && e.CardId == n.Discard.CardId) != 1 ||
            _cardMovements.Count(m => m.CardId == n.Discard.CardId && m.Sequence > r.Before && m.From == CardLocation.DiscardPile && m.To == CardLocation.Processing &&
                m.Reason == (EquipmentCatalog.IsEquipment(n.Discard.Kind) ? CardMoveReasons.EquipmentUse : CardMoveReasons.Use)) != 1)
            throw new InvalidOperationException("Shuguo must pay and complete this exact original native entity once.");
        ReplaceRuntimeFrame(f.Id, f with { AnniversaryZhangHuan = r with { Returned = true, Used = r.Used.Append(n.Discard.CardId).ToArray(), Stop = n.Discard.ExecutorSeat != f.OwnerSeat } });
        AdvanceEventRulesAndQueueFact(new AnniversaryZhangHuanUseReturnedEvent(f.Id, use.Id, n.ActionId, n.Discard.CardId));
        if (use.CardAttack is not { Active: true }) SettleZhangHuanUse(GetActiveProgramFrame(f.Id));
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 46300)] private bool ReturnZhangHuanAttack()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryZhangHuan: { Stage: AnniversaryZhangHuanStage.NativeUse, Returned: true } } f) return false;
        SettleZhangHuanUse(f); return true;
    }
    private void SettleZhangHuanUse(ProgramSkillFrame f)
    {
        var r = f.AnniversaryZhangHuan!; var n = r.Native!;
        if (!r.Returned || LifecycleCardUse(n.UseId) is not null) throw new InvalidOperationException("Shuguo must wait for all ordered targets and the complete native card-use tail.");
        var batches = _pendingCardsMovedBatches.Where(b => b.ParentFrameId == n.UseId && b.AwaitingProgramFrameId is null && b.OriginOwnerSeat == f.OwnerSeat && b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId).ToArray();
        foreach (var batch in batches) AdvanceEventRulesAndQueueFact(new AnniversaryZhangHuanNativeBatchEvent(f.Id, n.UseId, batch.Id));
        ReplaceRuntimeTop(f = f with { AnniversaryZhangHuan = r with { Stage = AnniversaryZhangHuanStage.NativeTail, After = _movementSequence, NativeBatches = batches.Select(b => b.Id).ToArray() }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        ContinueZhangHuan(f.Id);
    }
    private bool IsAnniversaryZhangHuanSettlementBatch(ProgramSkillFrame f, CardMovementBatchContext b) => f.AnniversaryZhangHuan is { Stage: AnniversaryZhangHuanStage.NativeTail, Native: { } n, Returned: true } r &&
        ValidZhangHuan(f) && r.NativeBatches.Contains(b.Id) && b.ParentFrameId == n.UseId && b.AwaitingProgramFrameId is null && b.OriginOwnerSeat == f.OwnerSeat &&
        b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId && b.Movements.Count > 0 && b.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After) &&
        ProgramEventHistory<AnniversaryZhangHuanNativeBatchEvent>().Count(e => e == new AnniversaryZhangHuanNativeBatchEvent(f.Id, n.UseId, b.Id)) == 1;
}
