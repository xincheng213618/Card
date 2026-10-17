namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string XieweiStoreReason = "skill-program.guan-hai.xiewei.store";
    private bool HasGuanHaiCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveXieweiTwoHandBaitBasicUse) || _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveYouqueMinimumBaitPindian);
    private bool IsXieweiSource(CardConversionSource source) => ViewAsRule(source)?.XieweiBaitBasicUse is not null;
    private bool IsXieweiSelection(ProgramMultiCardViewAsSelection selection) => IsXieweiSource(selection.Source);
    private CardActionContext PrepareXieweiAction(CardActionContext action) => !action.ConversionChain.Any(IsXieweiSource) ? action :
        new(action.ActionId, action.ParentActionId, action.Type, action.ActorSeat, action.ProviderSeat, action.RequesterSeat,
            action.ResponderSeat, action.OpponentSeat, action.EffectiveKind, action.TargetSeats, action.PhysicalCards,
            action.ConversionChain, action.DesignatedTargetSeats, Suit.None, 0, null, action.FactionOrigin);
    private bool XieweiNativeDirection(CharacterState owner, CardKind kind, bool forResponse)
    {
        if (!forResponse) return kind == CardKind.Slash;
        if (kind != CardKind.Dodge) return false;
        var attack = ActiveFactionDefense is { } defense && defense.CurrentCandidateSeat == owner.Seat ? defense.Attack : ActiveCardAttack;
        return attack is not null && IsSlashCard(attack.EffectiveCardKind ?? CardKind.Dodge) &&
            (attack.TargetSeat == owner.Seat || ActiveFactionDefense is { } d && d.CurrentCandidateSeat == owner.Seat && SameAttackOwner(d.Attack, attack));
    }
    private bool TryCompleteXieweiFactionDefense(FactionDefenseHandle pending, CharacterState provider, ProgramMultiCardViewAsSelection selection)
    {
        if (!IsXieweiSelection(selection)) return false;
        var attack = pending.Attack;
        if (selection.OutputKind != CardKind.Dodge || selection.Source.OwnerSeat != provider.Seat ||
            !SameContinuationOwner(ActiveFactionDefense, pending) || !SameAttackOwner(ActiveCardAttack, attack) ||
            pending.CurrentCandidateSeat != provider.Seat || provider.Seat == pending.OwnerSeat ||
            !_players[pending.OwnerSeat].IsAlive || !provider.IsAlive || attack.TargetSeat != pending.OwnerSeat ||
            !IsSlashCard(attack.EffectiveCardKind ?? CardKind.Dodge) ||
            pending.Source.FactionId != "magic-any" && GetEffectiveFactionId(provider) != pending.Source.FactionId ||
            _resolutionStack.LastOrDefault() is not ResponseWindowFrame response || response.ParentFrameId != attack.ResolutionId ||
            response.ResponderSeat != pending.OwnerSeat || response.RequiredCardKind != CardKind.Dodge ||
            selection.Cards.Count != 2 || selection.Cards.Select(c => c.Id).Distinct().Count() != 2 ||
            selection.Cards.Any(c => _cardZones.GetLocation(c.Id) != CardLocation.Hand(provider.Seat)) ||
            FindProgramMultiCardViewAsSelection(provider, selection.Cards.Select(c => c.Id).ToArray(), CardKind.Dodge, true, selection.Source) is null)
            throw new InvalidOperationException("Xiewei's faction Dodge requires its current original Slash, living requester/provider, exact two Hand cards and issued faction candidate.");
        if (_pendingDecision is { } prompt)
        {
            if (prompt.Kind != DecisionKind.RespondDodge || !prompt.IsPrivate || prompt.PlayerSeat != provider.Seat ||
                prompt.TargetSeat != pending.OwnerSeat || prompt.RequiredCardKind != CardKind.Dodge ||
                !prompt.Choices.Any(c => c.Cards.Order().SequenceEqual(selection.Cards.Select(card => card.Id).Order()) &&
                    TryReadConversionSource(c.Parameters, out var source) && source == selection.Source))
                throw new InvalidOperationException("Xiewei cannot answer a different or unpublished private faction request.");
        }
        else if (provider.IsHuman)
            throw new InvalidOperationException("A human Xiewei faction response must retain its actual private published pair.");
        // The ordinary AI candidate dispatcher has already cleared its prompt.
        // Its still-active faction cursor and original ResponseWindow above own
        // this exact selection until both real Hand costs have been deposited.
        PopResponseWindow(attack.ResolutionId);
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        var action = MoveProgramMultiCardResponse(provider, selection, attack.ResolutionId, attack.SourceSeat, pending.OwnerSeat);
        ActiveFactionDefense = null;
        ClearPendingDecision();
        _status = EngineStatus.Running;
        AddLog("CardResponded", $"{provider.Name} 发动【卸尾】，将两张手牌置为饵，替 {_players[pending.OwnerSeat].Name} 响应【{FactionPolicyName(pending.Source)}】。", provider.Seat, pending.OwnerSeat);
        AdvanceEventRulesAndQueueFact(new FactionDefenseResolvedEvent(attack.ResolutionId, pending.OwnerSeat, true, provider.Seat, selection.Cards[0].Id));
        if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardResponseAccepted, [attack.SourceSeat], ProgramCardContinuation.FactionDefenseDodge))
            ContinueAcceptedCardResponse(attack, action, ProgramCardContinuation.FactionDefenseDodge);
        return true;
    }
    private PublicPersistentPileSource EnsureXieweiPile(CardConversionSource source)
    {
        var key = (source.OwnerSeat, source.SkillId, source.SkillInstanceId);
        if (_publicPersistentPiles.TryGetValue(key, out var existing)) return existing;
        var pileId = PublicPileSources(source.OwnerSeat).Any(s => s.PublicPileId is null) ? PublicPileIdentity(source.SkillId, source.SkillInstanceId) : null;
        var pile = new PublicPersistentPileSource(source.OwnerSeat, source.SkillId, source.SkillInstanceId, int.MaxValue, pileId);
        _cardZones.EnsurePublicPersistentPile(pile.Location); _publicPersistentPiles.Add(key, pile); return pile;
    }
    private XieweiNativePayment PrepareXieweiPayment(CardUseFrame use, int provider, int actor, int? requester,
        CardConversionSource source, CardKind kind, bool response, IReadOnlyList<CardActionCost> costs)
    {
        if (!IsXieweiSource(source) || source.OwnerSeat != provider || !_players[provider].IsAlive || !HasRuntimeSkillInstance(_players[provider], source.SkillId, source.SkillInstanceId) ||
            costs.Count != 2 || costs.Select(c => c.CardId).Distinct().Count() != 2 || costs.Any(c => c.From != CardLocation.Hand(provider) || _cardZones.GetLocation(c.CardId) != c.From) ||
            !XieweiNativeDirection(_players[provider], kind, response)) throw new InvalidOperationException("Xiewei requires its original two genuine Hand cards and current true basic-use direction.");
        var p = new XieweiNativePayment(++_resolutionSequence, use.Id, source, _contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash,
            provider, actor, requester, _turnNumber, _turnProgression.OwnerSeat, response, kind, EnsureXieweiPile(source), _movementSequence)
        { Materials = costs.Select(c => new GuanHaiMaterial(c.CardId, c.CardKind, c.From)).ToArray() };
        AdvanceEventRulesAndQueueFact(new XieweiPaymentPreparedEvent(p.Id, p.UseId, p.Source, p.GameplayHash, provider, actor, requester, p.ActualTurn, p.TurnOwner, response, kind, p.Pile, p.Before));
        for (var i = 0; i < p.Materials.Count; i++) { var m = p.Materials[i]; AdvanceEventRulesAndQueueFact(new XieweiMaterialFrozenEvent(p.Id, i, m.CardId, m.Kind, m.From)); }
        return p;
    }
    private void PrepareXieweiNativeUse(CardUseFrame use)
    {
        if (!HasGuanHaiCapability || use.Action is not { Type: CardActionType.Use } action || action.ConversionChain.SingleOrDefault(IsXieweiSource) is not { } source) return;
        if (use.XieweiPayments?.Any(p => !p.ResponseUse) == true) throw new InvalidOperationException("Xiewei cannot prepare one native Use twice.");
        var paid = PrepareXieweiPayment(use, action.ProviderSeat, action.ActorSeat, action.RequesterSeat, source, action.EffectiveKind, false, action.PhysicalCards) with { ActionId = action.ActionId };
        UpdateLifecycleCardUse(use.Id, u => u with { XieweiPayments = (u.XieweiPayments ?? []).Append(paid).ToArray() });
        if (use.TargetSeats.Count > 1)
        {
            var maximum = XieweiTargetMaximum(_players[action.ActorSeat]);
            if (use.TargetSeats.Count > maximum || use.TargetSeats.Distinct().Count() != use.TargetSeats.Count) throw new InvalidOperationException("Xiewei exceeded its genuine native Slash target allowance.");
            UpdateLifecycleCardUse(use.Id, u => u with { XieweiTargetMaximum = maximum, XieweiOriginalTargets = use.TargetSeats });
            for (var i = 0; i < use.TargetSeats.Count; i++) AdvanceEventRulesAndQueueFact(new XieweiTargetFrozenEvent(use.Id, action.ActionId, i, use.TargetSeats[i], maximum));
        }
    }
    private void PrepareXieweiResponsePayment(CharacterState owner, ProgramMultiCardViewAsSelection selection, long nativeUseId, int? actorSeat = null)
    {
        if (!IsXieweiSelection(selection)) return;
        if (LifecycleCardUse(nativeUseId) is not { } use || use.XieweiPayments?.Any(p => p.ResponseUse && p.ActionId == 0) == true) throw new InvalidOperationException("A Xiewei Dodge requires exactly one current native attack payment.");
        var actor = actorSeat ?? owner.Seat;
        var costs = selection.Cards.Select(c => new CardActionCost(c.Id, c.Kind, FindOwnedCardLocation(owner, c), CapturePhysicalCardColor(owner.Seat, c))).ToArray();
        var paid = PrepareXieweiPayment(use, owner.Seat, actor, actor == owner.Seat ? null : actor, selection.Source, CardKind.Dodge, true, costs);
        UpdateLifecycleCardUse(use.Id, u => u with { XieweiPayments = (u.XieweiPayments ?? []).Append(paid).ToArray() });
    }
    private void UpdateXieweiPayment(long useId, XieweiNativePayment payment) => UpdateLifecycleCardUse(useId, u => u with { XieweiPayments = (u.XieweiPayments ?? []).Select(p => p.Id == payment.Id ? payment : p).ToArray() });
    private bool TryPayXieweiMaterials(IReadOnlyList<Card> cards, int providerSeat, CardLocation? sourceLocation, CardMoveReason reason)
    {
        if (!HasGuanHaiCapability || cards.Count != 2) return false;
        var ids = cards.Select(c => c.Id).Order().ToArray();
        var p = _resolutionStack.OfType<CardUseFrame>().SelectMany(u => u.XieweiPayments ?? []).SingleOrDefault(p => !p.Paid && p.OwnerSeat == providerSeat && p.Materials.Select(c => c.CardId).Order().SequenceEqual(ids));
        if (p is null) return false;
        if (reason != (p.ResponseUse ? CardMoveReasons.Respond : CardMoveReasons.Use) || sourceLocation is not null && sourceLocation != CardLocation.Hand(providerSeat) ||
            cards.Any(c => _cardZones.GetLocation(c.Id) != CardLocation.Hand(providerSeat))) throw new InvalidOperationException("Xiewei changed its issued Hand cost before the public deposit.");
        MoveProgramCardsFromMultipleSources(ids, p.Pile.Location, new(XieweiStoreReason),
            disclosure: CardMovementDisclosure.FaceUp); return true;
    }
    private void CaptureXieweiPaymentBatch(CardMovementBatchContext batch)
    {
        if (!HasGuanHaiCapability || batch.ParentFrameId is not { } id || LifecycleCardUse(id) is not { XieweiPayments: { } payments }) return;
        foreach (var original in payments.Where(p => !p.Paid).ToArray())
        {
            var records = batch.Movements.Where(m => original.Materials.Any(c => c.CardId == m.CardId) && m.Reason.Value == XieweiStoreReason).ToArray();
            if (records.Length == 0) continue;
            if (records.Length != 2 || records.Any(m => m.From != CardLocation.Hand(original.OwnerSeat) || m.To != original.Pile.Location || m.Sequence <= original.Before || !_cardMovements.Contains(m) || !original.Materials.Any(c => c.CardId == m.CardId && c.Kind == m.CardKind)) || records.Select(m => m.CardId).Distinct().Count() != 2)
                throw new InvalidOperationException("Xiewei requires one exact atomic two-Hand public-pile deposit, never a Processing invoice.");
            var p = original with { Paid = true, After = records.Max(m => m.Sequence), Movements = records.OrderBy(m => m.Sequence).ToArray() };
            UpdateXieweiPayment(id, p);
            for (var i = 0; i < p.Materials.Count; i++) { var m = records.Single(m => m.CardId == p.Materials[i].CardId); AdvanceEventRulesAndQueueFact(new XieweiBaitStoredEvent(p.Id, i, m.CardId, m.From, m.To, m.Sequence, batch.Id)); }
            if (!p.ResponseUse && p.ActionId > 0) AdvanceEventRulesAndQueueFact(new XieweiOutputBoundEvent(p.Id, p.UseId, p.ActionId, p.ActorSeat, p.OwnerSeat, p.RequesterSeat, p.Kind, false));
            if (!p.ResponseUse && LifecycleCardUse(id) is { PaymentMovementReceipt: null, Action: { } a }) UpdateLifecycleCardUse(id, u => u with { PaymentMovementReceipt = new(id, a.ActionId, records[0].Sequence - 1, records[^1].Sequence) { Batch = batch } });
        }
    }
    private void BindXieweiResponsePayment(long nativeUseId, CardActionContext action)
    {
        if (!HasGuanHaiCapability || !action.ConversionChain.Any(IsXieweiSource)) return;
        if (LifecycleCardUse(nativeUseId) is not { } use || use.XieweiPayments?.Where(p => p.ResponseUse && p.ActionId == 0 && p.OwnerSeat == action.ProviderSeat).ToArray() is not [var p] || !p.Paid ||
            action.Type != CardActionType.Response || action.EffectiveKind != CardKind.Dodge || action.ActorSeat != p.ActorSeat || action.RequesterSeat != p.RequesterSeat || action.ResponderSeat != p.OwnerSeat || !XieweiActionCosts(p, action))
            throw new InvalidOperationException("A Xiewei Dodge lost its real paid bait costs or native actor/provider/requester.");
        p = p with { ActionId = action.ActionId }; UpdateXieweiPayment(nativeUseId, p);
        AdvanceEventRulesAndQueueFact(new XieweiOutputBoundEvent(p.Id, p.UseId, p.ActionId, p.ActorSeat, p.OwnerSeat, p.RequesterSeat, p.Kind, true));
    }
    private static bool XieweiActionCosts(XieweiNativePayment p, CardActionContext a) => a.ConversionChain.Contains(p.Source) && a.PhysicalCards.Select(c => (c.CardId, c.CardKind, c.From)).OrderBy(c => c.CardId).SequenceEqual(p.Materials.Select(c => (c.CardId, c.Kind, c.From)).OrderBy(c => c.CardId));
    private bool ValidXieweiPayment(XieweiNativePayment p)
    {
        if (p.Materials.Count != 2 || p.Materials.Select(c => c.CardId).Distinct().Count() != 2 || p.Materials.Any(c => c.From != CardLocation.Hand(p.OwnerSeat)) || p.Source.OwnerSeat != p.OwnerSeat || p.Pile.OwnerSeat != p.OwnerSeat || p.Pile.SkillId != p.Source.SkillId || p.Pile.SkillInstanceId != p.Source.SkillInstanceId ||
            _contentRegistry.GetSkill(p.Source.SkillId).Program?.GameplayHash != p.GameplayHash || !IsXieweiSource(p.Source) ||
            ProgramEventHistory<XieweiPaymentPreparedEvent>().Count(e => e == new XieweiPaymentPreparedEvent(p.Id, p.UseId, p.Source, p.GameplayHash, p.OwnerSeat, p.ActorSeat, p.RequesterSeat, p.ActualTurn, p.TurnOwner, p.ResponseUse, p.Kind, p.Pile, p.Before)) != 1 ||
            !ProgramEventHistory<XieweiMaterialFrozenEvent>().Where(e => e.PaymentId == p.Id).OrderBy(e => e.Index).Select(e => new GuanHaiMaterial(e.CardId, e.Kind, e.From)).SequenceEqual(p.Materials) || p.Paid != (p.Movements.Count == 2)) return false;
        return p.Movements.All(m => m.Sequence > p.Before && m.Sequence <= p.After && m.From == CardLocation.Hand(p.OwnerSeat) && m.To == p.Pile.Location && m.Reason.Value == XieweiStoreReason && _cardMovements.Contains(m) && p.Materials.Any(c => c.CardId == m.CardId && c.Kind == m.CardKind) && ProgramEventHistory<XieweiBaitStoredEvent>().Count(e => e.PaymentId == p.Id && e.CardId == m.CardId && e.Sequence == m.Sequence && e.Pile == m.To) == 1) &&
            (!p.Paid || p.ActionId == 0 || ProgramEventHistory<XieweiOutputBoundEvent>().Count(e => e == new XieweiOutputBoundEvent(p.Id, p.UseId, p.ActionId, p.ActorSeat, p.OwnerSeat, p.RequesterSeat, p.Kind, p.ResponseUse)) == 1);
    }
    private bool IsXieweiRemovedMaterial(long useId, int cardId) => LifecycleCardUse(useId)?.XieweiPayments?.Any(p => !p.ResponseUse && p.Paid && p.ActionId > 0 && p.Materials.Any(c => c.CardId == cardId) && ValidXieweiPayment(p)) == true;
    private bool IsXieweiResponseRemovedMaterial(ProgramMultiCardViewAsSelection selection, int cardId) => IsXieweiSelection(selection) && _resolutionStack.OfType<CardUseFrame>().SelectMany(u => u.XieweiPayments ?? []).Any(p => p.ResponseUse && p.Paid && p.ActionId > 0 && p.Source == selection.Source && p.Materials.Select(c => c.CardId).Order().SequenceEqual(selection.Cards.Select(c => c.Id).Order()) && p.Materials.Any(c => c.CardId == cardId) && ValidXieweiPayment(p));
    private bool IsXieweiPaymentBatch(CardUseFrame use, CardMovementBatchContext batch) => use.XieweiPayments?.Any(p => !p.ResponseUse && p.Paid && p.UseId == use.Id && p.ActionId == use.Action?.ActionId && batch.ParentFrameId == use.Id && batch.Movements.SequenceEqual(p.Movements) && ValidXieweiPayment(p)) == true;
    private void AssertXieweiPayments(CardUseFrame use)
    { if (use.XieweiPayments is not null && (use.XieweiPayments is not System.Collections.IList { IsReadOnly: true } || use.XieweiPayments.Any(p => p.UseId != use.Id || p.Materials is not System.Collections.IList { IsReadOnly: true } || p.Movements is not System.Collections.IList { IsReadOnly: true } || !ValidXieweiPayment(p)))) throw new InvalidOperationException("Xiewei lost its immutable native cost/virtual-output separation."); }
    private bool IsXieweiMultiTargetUse(CardUseFrame use) => use.XieweiTargetMaximum is >= 2 && use.XieweiOriginalTargets is { Count: >= 2 } targets && targets is System.Collections.IList { IsReadOnly: true } && targets.Count <= use.XieweiTargetMaximum && targets.Distinct().Count() == targets.Count && use.XieweiPayments?.Any(p => !p.ResponseUse && p.Paid && p.ActionId == use.Action?.ActionId && ValidXieweiPayment(p)) == true && ProgramEventHistory<XieweiTargetFrozenEvent>().Where(e => e.UseId == use.Id).OrderBy(e => e.Index).Select(e => (e.Index, e.TargetSeat, e.Maximum, e.ActionId)).SequenceEqual(targets.Select((t, i) => (i, t, use.XieweiTargetMaximum.Value, use.Action!.ActionId)));
}
