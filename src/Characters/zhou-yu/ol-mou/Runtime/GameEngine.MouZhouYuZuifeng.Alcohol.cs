namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool CanUseMouZuifengAlcohol(ProgramSkillFrame f)
    {
        if (f.MouZuifeng is not { Paid: true, ShownId: { } id } r || !IsPaidMouZuifeng(f) ||
            r.Stage is not (MouZuifengStage.AlcoholChoice or MouZuifengStage.NativeIssuing) ||
            _winner != Winner.None || !_players[r.TargetSeat].IsAlive || !_players[f.OwnerSeat].IsAlive ||
            _cardZones.GetLocation(id) != CardLocation.Hand(r.TargetSeat)) return false;
        var actor = _players[r.TargetSeat]; var target = _players[f.OwnerSeat]; var card = GetAdvancedCard(id); var suit = EffectiveSuit(actor, card);
        return !IsTurnHandCardRestricted(actor, card) && !IsTurnPhysicalUseForbidden(actor.Seat, [id]) &&
            !IsCardUseForbidden(actor.Seat, CardKind.Alcohol, CardActionType.Use) &&
            !IsDirectedCardTargetProhibited(actor.Seat, target.Seat, CardKind.Alcohol) &&
            !(HasTurnCardTargetRestriction(actor.Seat, SkillProgramCardTargetRestriction.SelfOnly) && actor.Seat != target.Seat) &&
            !IsCardTargetProhibited(target, CardKind.Alcohol, suit, SuitColor(suit)) && !HasBeneficiarySuitShield(actor.Seat, target.Seat, suit) &&
            IsMouGuquUseAllowed(actor.Seat, CardKind.Alcohol, [id], r.Source);
    }
    private bool IsExactMouZuifengAlcoholConversion(int actorSeat, int cardId, CardConversionSource? source)
    {
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame { MouZuifeng: { Paid: true, ShownId: { } shown } r } f &&
            r.Stage is MouZuifengStage.AlcoholChoice or MouZuifengStage.NativeIssuing && r.TargetSeat == actorSeat && shown == cardId &&
            (source is null || source == r.Source) && _cardZones.GetLocation(cardId) == CardLocation.Hand(actorSeat) && IsPaidMouZuifeng(f)) return true;
        return _resolutionStack.OfType<CardUseFrame>().Any(use => use.MouZuifengAlcohol is { } o && o.ActorSeat == actorSeat &&
            o.CardId == cardId && (source is null || source == o.Source) && IsIssuedMouZuifengAlcoholUse(use));
    }
    private void DispatchMouZuifengAlcohol(ProgramSkillFrame f)
    {
        if (!CanUseMouZuifengAlcohol(f)) throw new InvalidOperationException("Zuifeng cannot spend a moved, banned or foreign substitute for its publicly shown original Hand card.");
        var r = f.MouZuifeng!; var actor = _players[r.TargetSeat]; var card = GetAdvancedCard(r.ShownId!.Value);
        var suit = EffectiveSuit(actor, card); var rank = EffectiveOwnedCardRank(actor, card); var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { MouZuifeng = r with { Stage = MouZuifengStage.NativeIssuing, WineSuit = suit, WineRank = rank,
            WineBefore = before, Before = before, After = before } });
        var appearance = card with { Suit = suit, Rank = rank };
        var id = BeginCardUse(appearance, actor.Seat, [f.OwnerSeat], CardKind.Alcohol, physicalCardIds: [card.Id], conversionSource: r.Source);
        if (LifecycleCardUse(id)?.MouZuifengAlcohol is null) throw new InvalidOperationException("Zuifeng's real Alcohol must bind its exact original material and native return before payment.");
        MoveCard(card, CardLocation.Hand(actor.Seat), CardLocation.Processing, CardMoveReasons.Use, disclosure: CardMovementDisclosure.FaceUp);
        var parent = _resolutionStack.OfType<ProgramSkillFrame>().Single(p => p.Id == f.Id);
        if (parent.MouZuifeng!.WinePaidSequence <= before) throw new InvalidOperationException("Zuifeng's native Alcohol did not pay its original real Hand entity.");
        actor.UsedPlayPhaseAlcoholThisTurn = true;
        AdvanceEventRulesAndQueueFact(new ProgramViewAsConvertedEvent(id, r.Source.SkillId, r.Source.BindingId, actor.Seat,
            Array.AsReadOnly(new[] { card.Id }), CardKind.Alcohol, true, Array.AsReadOnly(new[] { f.OwnerSeat })));
        BeginSimpleCardUse(id, new(card.Id, SimpleCardUseEffect.Alcohol));
    }
    private void BindMouZuifengAlcoholUse(CardUseFrame supplied)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == supplied.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame { MouZuifeng: { Stage: MouZuifengStage.NativeIssuing } r } f) return;
        var use = LifecycleCardUse(supplied.Id)!;
        if (!IsPaidMouZuifeng(f) || use.MouZuifengAlcohol is not null || use.CardId != r.ShownId || use.CardKind != CardKind.Alcohol ||
            use.SourceSeat != r.TargetSeat || !use.TargetSeats.SequenceEqual([f.OwnerSeat]) || use.Action is not { Type: CardActionType.Use } a ||
            a.ActorSeat != r.TargetSeat || a.ProviderSeat != r.TargetSeat || a.RequesterSeat is not null || a.EffectiveKind != CardKind.Alcohol ||
            a.EffectiveSuit != r.WineSuit || a.EffectiveRank != r.WineRank || a.PhysicalCards is not [var cost] ||
            cost.CardId != r.ShownId || cost.CardKind != r.ShownKind || cost.From != CardLocation.Hand(r.TargetSeat) ||
            !a.ConversionChain.SequenceEqual([r.Source]) || !a.TargetSeats.SequenceEqual([f.OwnerSeat]))
            throw new InvalidOperationException("Zuifeng's foreign-target Alcohol changed its paid issuer, true actor/provider, original Hand material, effective appearance or beneficiary.");
        var origin = new MouZuifengAlcoholOrigin(f.Id, r.Source, r.GameplayHash, use.Id, a.ActionId, r.TargetSeat,
            f.OwnerSeat, cost.CardId, cost.CardKind, r.WineSuit, r.WineRank, r.WineBefore);
        UpdateLifecycleCardUse(use.Id, u => u with { MouZuifengAlcohol = origin });
        ReplaceRuntimeFrame(f.Id, f with { MouZuifeng = r with { Stage = MouZuifengStage.NativeUse, UseId = use.Id, ActionId = a.ActionId } });
        AdvanceEventRulesAndQueueFact(new MouZuifengAlcoholIssuedEvent(f.Id, use.Id, a.ActionId, origin.ActorSeat, origin.TargetSeat,
            cost.CardId, cost.CardKind, origin.EffectiveSuit, origin.EffectiveRank));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 29020)]
    private void CaptureMouZuifengAlcoholPayment(CardMovedEvent e)
    {
        if (e.Reason != CardMoveReasons.Use || _cardMovements.LastOrDefault() is not { } m || m.CardId != e.CardId || m.From != e.From || m.To != e.To ||
            m.Reason != e.Reason || _resolutionStack.LastOrDefault() is not CardUseFrame { MouZuifengAlcohol: { } o } use ||
            m.CardId != o.CardId || m.CardKind != o.PrintedKind || m.From != CardLocation.Hand(o.ActorSeat) || m.To != CardLocation.Processing || m.Sequence <= o.BeforePayment) return;
        var f = _resolutionStack.OfType<ProgramSkillFrame>().Single(p => p.Id == o.ProgramFrameId); var r = f.MouZuifeng!;
        if (r.Stage != MouZuifengStage.NativeUse || r.UseId != use.Id || r.ActionId != o.ActionId || r.WinePaidSequence != 0)
            throw new InvalidOperationException("Zuifeng cannot pay its original native Alcohol material more than once.");
        ReplaceRuntimeFrame(f.Id, f with { MouZuifeng = r with { WinePaidSequence = m.Sequence, After = m.Sequence } });
        AdvanceEventRulesAndQueueFact(new MouZuifengAlcoholPaidEvent(f.Id, use.Id, o.ActionId, m.CardId, m.CardKind,
            o.ActorSeat, m.From, m.To, m.Sequence));
    }
    private bool IsIssuedMouZuifengAlcoholUse(CardUseFrame use)
    {
        if (use.MouZuifengAlcohol is not { } o || !ValidMouZuifengHistory(o.ProgramFrameId, out var start, out var original) ||
            start.Source != o.Source || start.GameplayHash != o.GameplayHash || original.TargetSeat != o.ActorSeat || start.Source.OwnerSeat != o.TargetSeat ||
            ProgramEventHistory<MouZuifengShownEvent>().Count(e => e.FrameId == o.ProgramFrameId && e.TargetSeat == o.ActorSeat &&
                e.ViewerSeat == o.TargetSeat && e.CardId == o.CardId && e.PrintedKind == o.PrintedKind) != 1 ||
            ProgramEventHistory<MouZuifengAlcoholChosenEvent>().Count(e => e == new MouZuifengAlcoholChosenEvent(o.ProgramFrameId,o.ActorSeat,o.TargetSeat,o.CardId,true)) != 1 || use.Id != o.UseId || use.CardId != o.CardId || use.CardKind != CardKind.Alcohol ||
            use.SourceSeat != o.ActorSeat || use.Action is not { Type: CardActionType.Use } a || a.ActionId != o.ActionId ||
            a.ActorSeat != o.ActorSeat || a.ProviderSeat != o.ActorSeat || a.RequesterSeat is not null || a.EffectiveKind != CardKind.Alcohol ||
            a.EffectiveSuit != o.EffectiveSuit || a.EffectiveRank != o.EffectiveRank || !a.EffectiveDesignatedTargetSeats.SequenceEqual([o.TargetSeat]) ||
            !a.ConversionChain.SequenceEqual([o.Source]) || a.PhysicalCards is not [var cost] || cost.CardId != o.CardId || cost.CardKind != o.PrintedKind ||
            cost.From != CardLocation.Hand(o.ActorSeat) || _contentRegistry.GetSkill(o.Source.SkillId).Program?.GameplayHash != o.GameplayHash ||
            ProgramEventHistory<MouZuifengAlcoholIssuedEvent>().Where(e => e.UseId == use.Id).ToArray() is not [var issued] ||
            issued != new MouZuifengAlcoholIssuedEvent(o.ProgramFrameId, o.UseId, o.ActionId, o.ActorSeat, o.TargetSeat, o.CardId,
                o.PrintedKind, o.EffectiveSuit, o.EffectiveRank)) return false;
        var payments = ProgramEventHistory<MouZuifengAlcoholPaidEvent>().Where(e => e.UseId == use.Id).ToArray();
        if (payments.Length == 0) return _cardZones.GetLocation(o.CardId) == cost.From && _movementSequence == o.BeforePayment;
        return payments is [var paid] && paid.FrameId == o.ProgramFrameId && paid.ActionId == o.ActionId && paid.CardId == o.CardId &&
            paid.PrintedKind == o.PrintedKind && paid.ActorSeat == o.ActorSeat && paid.From == cost.From && paid.To == CardLocation.Processing &&
            paid.Sequence > o.BeforePayment && _cardMovements.Count(m => m.Sequence == paid.Sequence && m.CardId == o.CardId &&
                m.CardKind == o.PrintedKind && m.From == cost.From && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1;
    }
    // The ordinary simple-card pipeline still owns all finalized/committed,
    // additional-effect, payment-child and completion windows. Only this exact
    // issued origin changes its normally self-only Alcohol beneficiary.
    private CharacterState MouZuifengAlcoholBeneficiary(CardUseFrame use, CharacterState ordinary) =>
        use.MouZuifengAlcohol is { } o && IsIssuedMouZuifengAlcoholUse(use) && use.AdjustedSimpleContinuation is null ? _players[o.TargetSeat] : ordinary;
    private bool ContinueMouZuifengAlcoholUse(long id, ProgramSimpleCardContinuation continuation)
    {
        if (LifecycleCardUse(id) is not { MouZuifengAlcohol: { } o } use) return false;
        if (continuation.CardId != o.CardId || continuation.Effect != SimpleCardUseEffect.Alcohol || !IsIssuedMouZuifengAlcoholUse(use))
            throw new InvalidOperationException("Zuifeng's paid native Alcohol changed its original simple-effect continuation.");
        SetCardUseStep(id, ResolutionFrameStep.ResolvingEffect); var card = GetAdvancedCard(o.CardId);
        var actor = _players[use.SourceSeat];
        if (!actor.IsAlive && !IsExtraCardEffectRound(id)) { FinishCardUse(id, card, CardKind.Alcohol); return true; }
        CompleteAlcoholUse(actor, card, id); return true;
    }
    [NativeUseValidation(29020)]
    private void ValidateMouZuifengAlcoholUse(CardUseFrame use)
    { if (use.MouZuifengAlcohol is not null && (!IsIssuedMouZuifengAlcoholUse(use) || use.Step != ResolutionFrameStep.Completed ||
        use.PendingRecoveryAttempts is { Count: > 0 } || use.RecoveryPaidContinuation is not null)) throw new InvalidOperationException("Zuifeng cannot return before its whole native Alcohol and paid children complete."); }
    [NativeUseReturn(29020)]
    private void ReturnMouZuifengAlcoholUse(CardUseFrame use)
    {
        if (use.MouZuifengAlcohol is not { } o) return;
        var f = GetActiveProgramFrame(o.ProgramFrameId); var r = f.MouZuifeng!;
        if (r.Stage != MouZuifengStage.NativeUse || r.UseId != use.Id || r.ActionId != o.ActionId || r.UseReturned ||
            use.Step != ResolutionFrameStep.Completed || LifecycleCardUse(use.Id) is not null || !IsIssuedMouZuifengAlcoholUse(use) ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1)
            throw new InvalidOperationException("Zuifeng's native Alcohol cannot return to another program or before all native effects finish.");
        var batches = _pendingCardsMovedBatches.Where(b => b.ParentFrameId == use.Id && b.AwaitingProgramFrameId is null &&
            b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId && b.OriginOwnerSeat == f.OwnerSeat).ToArray();
        foreach (var batch in batches)
        {
            if (batch.Movements.Count == 0 || batch.Movements.Any(m => !_cardMovements.Contains(m) || m.CardId != o.CardId || m.CardKind != o.PrintedKind ||
                !(m.Sequence == r.WinePaidSequence && m.From == CardLocation.Hand(o.ActorSeat) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use ||
                  m.Sequence > r.WinePaidSequence && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.UseFinished)))
                throw new InvalidOperationException("Zuifeng's retired Alcohol produced a batch without its exact original material payment or native finish invoice.");
            AdvanceEventRulesAndQueueFact(new MouZuifengNativeBatchSignedEvent(f.Id, use.Id, batch.Id,
                batch.Movements[0].Sequence, batch.Movements[^1].Sequence, batch.Movements.Count));
        }
        ReplaceRuntimeTop(f = f with { MouZuifeng = r with { Stage = MouZuifengStage.NativeChildren, UseReturned = true,
            NativeBatches = batches.Select(b => b.Id).ToArray(), After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouZuifengAlcoholReturnedEvent(f.Id, use.Id, o.ActionId)); ContinueMouZuifeng(f.Id);
    }
    private bool IsMouZuifengNativeSettlementBatch(ProgramSkillFrame f, CardMovementBatchContext batch) =>
        f.MouZuifeng is { Stage: MouZuifengStage.NativeChildren, UseReturned: true } r && IsPaidMouZuifeng(f) &&
        batch.ParentFrameId == r.UseId && batch.AwaitingProgramFrameId is null && batch.OriginSkillId == f.SkillId &&
        batch.OriginSkillInstanceId == f.SkillInstanceId && batch.OriginOwnerSeat == f.OwnerSeat && r.NativeBatches.Contains(batch.Id) &&
        batch.Movements.Count > 0 && ProgramEventHistory<MouZuifengNativeBatchSignedEvent>().Count(e => e ==
            new MouZuifengNativeBatchSignedEvent(f.Id, r.UseId, batch.Id, batch.Movements[0].Sequence, batch.Movements[^1].Sequence, batch.Movements.Count)) == 1 &&
        batch.Movements.All(m => _cardMovements.Contains(m) && m.CardId == r.ShownId && m.CardKind == r.ShownKind &&
            (m.Sequence == r.WinePaidSequence && m.From == CardLocation.Hand(r.TargetSeat) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use ||
             m.Sequence > r.WinePaidSequence && m.Sequence <= r.After && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.UseFinished));
}
