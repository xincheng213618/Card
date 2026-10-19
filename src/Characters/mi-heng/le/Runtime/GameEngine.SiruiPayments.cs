namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void IssueSiruiUse(CardUseFrame use)
    {
        if (use.Action is not { Type: CardActionType.Use } action) return;
        var sources = action.ConversionChain.Where(IsSiruiSource).ToArray();
        if (sources.Length == 0) return;
        if (sources is not [var source] || action.ActorSeat != source.OwnerSeat ||
            action.ProviderSeat != source.OwnerSeat || action.PhysicalCards is not [var cost] ||
            use.PhysicalCardIds is not [var physical] || physical != cost.CardId ||
            _resolutionStack.LastOrDefault()?.Id != use.Id || use.Sirui is not null ||
            !CanUseSiruiPhase(_players[source.OwnerSeat], source) ||
            (action.EffectiveKind != ViewAsRule(source)!.OutputKind &&
                !(ViewAsRule(source)!.OutputKind == CardKind.Slash && action.EffectiveKind == CardKind.FireSlash &&
                    HasZhuqueFan(_players[source.OwnerSeat]))))
            throw new InvalidOperationException("Sirui cannot issue a different native actor, material, source or Play allowance.");
        var card = _cardZones.CardsAt(cost.From).SingleOrDefault(c => c.Id == cost.CardId);
        if (card is null || cost.From.OwnerSeat != source.OwnerSeat ||
            cost.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            _cardZones.GetLocation(card.Id) != cost.From ||
            SiruiNameLength(card.Kind) != SiruiNameLength(ViewAsRule(source)!.OutputKind))
            throw new InvalidOperationException("Sirui must pay its one original equal-name HE entity.");
        var receipt = SiruiNewReceipt(use.Id, source, action.EffectiveKind, card, cost.From, SiruiNativeUseKind.CardUse)
            with { ActionId = action.ActionId };
        ReplaceRuntimeFrame(use.Id, use with { Sirui = receipt });
        IssueSiruiReceiptFact(receipt);
        AdvanceEventRulesAndQueueFact(new SiruiNativeActionBoundEvent(use.Id, action.ActionId,
            action.ActorSeat, action.ProviderSeat, action.Type, action.EffectiveKind));
    }

    private SiruiUseReceipt SiruiNewReceipt(long owner, CardConversionSource source, CardKind output,
        Card card, CardLocation from, SiruiNativeUseKind kind) => new(owner, source,
            _contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash,
            SiruiCurrentPhase(source.OwnerSeat) ?? throw new InvalidOperationException("Sirui lost its actual Play phase."),
            kind, output, card.Id, card.Kind, from, _movementSequence);

    private void IssueSiruiReceiptFact(SiruiUseReceipt r) => AdvanceEventRulesAndQueueFact(
        new SiruiPhaseUseIssuedEvent(r.OwnerFrameId, r.Source, r.GameplayHash, r.Phase,
            r.NativeKind, r.OutputKind, r.CardId, r.PrintedKind, r.From, r.SequenceBefore));

    private void PrepareSiruiResponsePayment(CharacterState provider, Card card, CardKind kind,
        CardConversionSource? source)
    {
        PrepareMouZhaJiangResponse(provider, [card.Id], kind);
        if (!IsSiruiSource(source)) return;
        if (!CanUseSiruiPhase(provider, source!) || !IsSiruiTrueResponseUse(provider, kind) ||
            ViewAsRule(source!)!.OutputKind != kind)
            throw new InvalidOperationException("Sirui cannot pay a pure Played response or a different used name.");
        ResolutionFrame? parent = kind == CardKind.Nullification
            ? _resolutionStack.LastOrDefault() as NullificationWindowFrame
            : ActiveCardAttack is { } attack ? LifecycleCardUse(attack.ResolutionId) : null;
        var from = _cardZones.GetLocation(card.Id);
        if (parent is null || parent.Sirui is not null || from.OwnerSeat != provider.Seat ||
            from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            SiruiNameLength(card.Kind) != SiruiNameLength(kind) || HasProgramCardIdentity(provider, card) ||
            IsTurnHandCardRestricted(provider, card))
            throw new InvalidOperationException("Sirui response payment lost its original native owner or HE entity.");
        var r = SiruiNewReceipt(parent.Id, source!, kind, card, from,
            kind == CardKind.Nullification ? SiruiNativeUseKind.Nullification : SiruiNativeUseKind.Dodge);
        ReplaceRuntimeFrame(parent.Id, parent with { Sirui = r });
        IssueSiruiReceiptFact(r);
    }

    private void BindSiruiResponseAction(ResolutionFrame parent, CardActionContext action)
    {
        var sources = action.ConversionChain.Where(IsSiruiSource).ToArray();
        if (sources.Length == 0) return;
        var current = _resolutionStack.SingleOrDefault(f => f.Id == parent.Id);
        if (current?.Sirui is not { NativeKind: not SiruiNativeUseKind.CardUse } r ||
            sources is not [var source] || source != r.Source || action.Type != CardActionType.Response ||
            action.ActorSeat != source.OwnerSeat || action.ProviderSeat != source.OwnerSeat ||
            action.RequesterSeat is not null || action.EffectiveKind != r.OutputKind ||
            action.PhysicalCards is not [var cost] || cost.CardId != r.CardId ||
            cost.From != r.From || cost.CardKind != r.PrintedKind || r.ActionId != 0 ||
            !ValidSiruiInvoice(current, r, requirePayment: true))
            throw new InvalidOperationException("Sirui response action changed its already-paid native true-use invoice.");
        ReplaceRuntimeFrame(current.Id, current with { Sirui = r with { ActionId = action.ActionId } });
        AdvanceEventRulesAndQueueFact(new SiruiNativeActionBoundEvent(current.Id, action.ActionId,
            action.ActorSeat, action.ProviderSeat, action.Type, action.EffectiveKind));
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 28212)]
    private void CaptureSiruiMaterialPayment(IGameEvent payload)
    {
        if (payload is not CardMovedEvent moved) return;
        foreach (var frame in _resolutionStack.Where(f => f.Sirui is { PaymentSequence: 0 }).ToArray())
        {
            var r = frame.Sirui!;
            if (moved.CardId != r.CardId || moved.From != r.From) continue;
            var reason = r.NativeKind switch
            {
                SiruiNativeUseKind.Dodge => CardMoveReasons.Respond,
                SiruiNativeUseKind.Nullification => CardMoveReasons.Nullification,
                _ => CardMoveReasons.Use
            };
            // A real general weapon has the native Outside disposition instead
            // of Processing. No other entity or movement gains that exception.
            var outside = r.From.Zone == CardZoneKind.Equipment && r.PrintedKind == CardKind.GeneralWeapon &&
                moved.To == CardLocation.OutsideGame;
            if (moved.Reason != reason || moved.To != CardLocation.Processing && !outside ||
                _cardMovements.LastOrDefault() is not { } movement || movement.Sequence <= r.SequenceBefore ||
                movement.CardId != r.CardId || movement.CardKind != r.PrintedKind || movement.From != r.From || movement.To != moved.To ||
                movement.Reason != reason || !ValidSiruiInvoice(frame, r, requirePayment: false))
                throw new InvalidOperationException("Sirui cannot substitute a collateral movement for its native material payment.");
            ReplaceRuntimeFrame(frame.Id, frame with { Sirui = r with { PaymentSequence = movement.Sequence } });
            AdvanceEventRulesAndQueueFact(new SiruiMaterialPaidEvent(frame.Id, r.CardId, r.From,
                moved.To, moved.Reason, movement.Sequence));
        }
    }
}
