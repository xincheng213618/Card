namespace CardGame.Core;

public sealed partial class GameEngine
{
    private LeiluanNativePayment PrepareLeiluanPayment(CardUseFrame use, CharacterState owner, CardConversionSource source,
        CardKind kind, bool response, IReadOnlyList<CardActionCost> costs)
    {
        if (!HasLeiluanCapability || ViewAsRule(source)?.LeiluanBasicUse is null || source.OwnerSeat != owner.Seat || !owner.IsAlive ||
            _contentRegistry.GetSkill(source.SkillId).Program is not { } program || costs.Count != LeiluanCurrentX || costs.Select(c => c.CardId).Distinct().Count() != costs.Count ||
            costs.Any(c => c.From.OwnerSeat != owner.Seat || c.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain) || _cardZones.GetLocation(c.CardId) != c.From) ||
            !LeiluanSelections(owner, kind, response).Any(s => s.Source == source && s.OutputKind == kind && s.Cards.Select(c => c.Id).Order().SequenceEqual(costs.Select(c => c.CardId).Order())))
            throw new InvalidOperationException("Leiluan cannot issue a cost without the exact current X, native direction and complete owned materials.");
        var hand = GetHand(owner).Select(c => c.Id).ToArray(); var materials = costs.Select(c => new LeiluanMaterial(c.CardId, c.CardKind, c.From)).ToArray();
        var p = new LeiluanNativePayment(++_resolutionSequence, use.Id, source, program.GameplayHash, owner.Seat, _turnNumber, _turnProgression.OwnerSeat, _roundNumber, kind, response, costs.Count, _movementSequence)
        { Materials = materials, HandBefore = hand, LastHand = hand.Length > 0 && hand.All(id => costs.Any(c => c.CardId == id && c.From == CardLocation.Hand(owner.Seat))) };
        AdvanceEventRulesAndQueueFact(new LeiluanPaymentPreparedEvent(p.Id, p.UseId, p.Source, p.GameplayHash, p.OwnerSeat, p.ActualTurn, p.TurnOwner, p.Round, p.Kind, p.ResponseUse, p.FrozenX, p.HandBefore.Count, p.Before));
        for (var i = 0; i < materials.Length; i++) AdvanceEventRulesAndQueueFact(new LeiluanMaterialFrozenEvent(p.Id, i, materials[i].CardId, materials[i].Kind, materials[i].From));
        for (var i = 0; i < hand.Length; i++) AdvanceEventRulesAndQueueFact(new LeiluanHandFrozenEvent(p.Id, i, hand[i]));
        return p;
    }
    private void BindLeiluanNativeUse(CardUseFrame supplied)
    {
        if (!HasLeiluanCapability || supplied.Action is not { Type: CardActionType.Use } action || action.ConversionChain.SingleOrDefault(IsLeiluanSource) is not { } source) return;
        if (supplied.LeiluanPayments?.Any(p => !p.ResponseUse && p.ActionId == action.ActionId) == true) throw new InvalidOperationException("Leiluan cannot bind one accepted Use twice.");
        var p = PrepareLeiluanPayment(supplied, _players[source.OwnerSeat], source, action.EffectiveKind, false, action.PhysicalCards) with { ActionId = action.ActionId };
        UpdateLifecycleCardUse(supplied.Id, u => u with { LeiluanPayments = (u.LeiluanPayments ?? []).Append(p).ToArray() });
    }
    private void PrepareLeiluanResponsePayment(CharacterState owner, ProgramMultiCardViewAsSelection selection, long nativeUseId)
    {
        if (!IsLeiluanSelection(selection)) return;
        if (LifecycleCardUse(nativeUseId) is not { } use || use.LeiluanPayments?.Any(p => p.ResponseUse && p.ActionId == 0) == true || selection.OutputKind != CardKind.Dodge || !IsProgramResponseCardUse(owner, CardKind.Dodge))
            throw new InvalidOperationException("Leiluan response payment requires its real incoming Slash and one unbound owned payment.");
        var costs = selection.Cards.Select(c => new CardActionCost(c.Id, c.Kind, FindOwnedCardLocation(owner, c), CapturePhysicalCardColor(owner.Seat, c))).ToArray();
        var p = PrepareLeiluanPayment(use, owner, selection.Source, CardKind.Dodge, true, costs);
        UpdateLifecycleCardUse(use.Id, u => u with { LeiluanPayments = (u.LeiluanPayments ?? []).Append(p).ToArray() });
    }
    private void BindLeiluanResponsePayment(long nativeUseId, CardActionContext action)
    {
        if (!HasLeiluanCapability || !action.ConversionChain.Any(IsLeiluanSource)) return;
        if (LifecycleCardUse(nativeUseId) is not { } use || action.Type != CardActionType.Response || action.EffectiveKind != CardKind.Dodge || action.ActorSeat != action.ProviderSeat || action.ResponderSeat != action.ActorSeat || action.RequesterSeat is not null ||
            use.LeiluanPayments?.Where(p => p.ResponseUse && p.ActionId == 0 && p.OwnerSeat == action.ActorSeat).ToArray() is not [var p] || !p.Paid ||
            !action.ConversionChain.Contains(p.Source) || !action.PhysicalCards.Select(c => (c.CardId, c.CardKind, c.From)).OrderBy(c => c.CardId).SequenceEqual(p.Materials.Select(c => (c.CardId, c.Kind, c.From)).OrderBy(c => c.CardId)))
            throw new InvalidOperationException("A true Leiluan Dodge lost its already paid exact native response materials.");
        p = p with { ActionId = action.ActionId }; UpdateLeiluanPayment(use.Id, p);
        AdvanceEventRulesAndQueueFact(new LeiluanPaymentBoundEvent(p.Id, p.UseId, p.ActionId, p.LastHand, p.After));
    }
    private void UpdateLeiluanPayment(long useId, LeiluanNativePayment payment) => UpdateLifecycleCardUse(useId, u => u with { LeiluanPayments = (u.LeiluanPayments ?? []).Select(p => p.Id == payment.Id ? payment : p).ToArray() });
    private void CaptureLeiluanPaymentBatch(CardMovementBatchContext batch)
    {
        if (!HasLeiluanCapability || batch.ParentFrameId is not { } useId || LifecycleCardUse(useId) is not { LeiluanPayments: { } payments }) return;
        foreach (var supplied in payments.Where(p => !p.Paid).ToArray())
        {
            var p = supplied; var paid = p.Movements.ToList();
            foreach (var (material, index) in p.Materials.Select((m, i) => (m, i)))
            {
                if (paid.Any(m => m.CardId == material.CardId)) continue;
                var reason = p.ResponseUse ? CardMoveReasons.Respond : CardMoveReasons.Use;
                var matching = batch.Movements.Where(m => m.CardId == material.CardId && m.From == material.From && m.CardKind == material.Kind && m.Reason == reason).ToArray();
                if (matching.Length == 0) continue;
                if (matching is not [var movement] || movement.Sequence <= p.Before || movement.To != CardLocation.Processing && !(GetAdvancedCard(material.CardId).IsGeneralWeapon && material.From.Zone == CardZoneKind.Equipment && movement.To == CardLocation.OutsideGame) || !_cardMovements.Contains(movement))
                    throw new InvalidOperationException("Leiluan payment changed a native cost's exact source, reason or destination.");
                paid.Add(movement); AdvanceEventRulesAndQueueFact(new LeiluanMaterialPaidEvent(p.Id, index, material.CardId, movement.Sequence, batch.Id));
            }
            if (paid.Count == p.Movements.Count) continue;
            p = p with { Movements = paid.OrderBy(m => m.Sequence).ToArray(), After = paid.Max(m => m.Sequence), Paid = paid.Count == p.Materials.Count };
            UpdateLeiluanPayment(useId, p);
            if (p.Paid && p.ActionId > 0) AdvanceEventRulesAndQueueFact(new LeiluanPaymentBoundEvent(p.Id, p.UseId, p.ActionId, p.LastHand, p.After));
        }
    }
    private bool LeiluanActionMatches(LeiluanNativePayment p, CardActionContext action)
    {
        if (p.ActionId != action.ActionId || action.ProviderSeat != p.OwnerSeat || !action.ConversionChain.Contains(p.Source) ||
            action.Type != (p.ResponseUse ? CardActionType.Response : CardActionType.Use) ||
            !action.PhysicalCards.Select(c => (c.CardId, c.CardKind, c.From)).OrderBy(c => c.CardId).SequenceEqual(p.Materials.Select(c => (c.CardId, c.Kind, c.From)).OrderBy(c => c.CardId))) return false;
        if (p.Kind == action.EffectiveKind) return true;
        if (LifecycleCardUse(p.UseId) is not { CurrentSlashFirePolicy: { OriginalAction: { } original } } use || p.ResponseUse || !IsSlashCard(p.Kind) || action.EffectiveKind != CardKind.FireSlash || original.ActionId != p.ActionId || original.EffectiveKind != p.Kind) return false;
        AssertCurrentSlashFirePolicy(use); return true;
    }
    private LeiluanNativePayment? IssuedLeiluanPayment(CardActionContext action) => _resolutionStack.OfType<CardUseFrame>().SelectMany(u => u.LeiluanPayments ?? [])
        .SingleOrDefault(p => p.Paid && p.LastHand && LeiluanActionMatches(p, action));
    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 5)]
    private void CollectIssuedLeiluanCandidates(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result)
    {
        if (!HasLeiluanCapability || window != SkillProgramTriggerWindow.CardUseCommitted || IssuedLeiluanPayment(action) is not { } p ||
            CompleteProgramEventHistory().OfType<LeiluanBenefitIssuedEvent>().Any(e => e.PaymentId == p.Id)) return;
        var program = _contentRegistry.GetSkill(p.Source.SkillId).Program!;
        var trigger = program.Triggers.Single(t => t.Effects is [{ Op: SkillProgramEffectOp.ResolveLeiluanLastHandUseBenefit }]);
        var facts = CaptureProgramTriggerFacts(_players[p.OwnerSeat], action);
        var context = CreateCardActionProgramContext(action, window, 0, p.OwnerSeat, -1, facts);
        result.Add(new(p.OwnerSeat, -1, p.Source.SkillId, trigger.Id, p.GameplayHash, p.Source.SkillInstanceId, trigger.Priority, context));
    }
    private bool IsIssuedLeiluanCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId != "ol:leiluan" || context.Window != SkillProgramTriggerWindow.CardUseCommitted || context.OwnerSeat != candidate.OwnerSeat ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } window ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || ToSharedCandidate(window.Candidates[window.CandidateIndex]) != candidate ||
            IssuedLeiluanPayment(window.Action) is not { } p || p.Source.OwnerSeat != candidate.OwnerSeat || p.Source.SkillInstanceId != candidate.SkillInstanceId || p.GameplayHash != candidate.GameplayHash ||
            _contentRegistry.GetSkill(candidate.SkillId).Program is not { } program || program.GameplayHash != candidate.GameplayHash ||
            program.Triggers.SingleOrDefault(t => t.Id == candidate.BindingId)?.Effects is not [{ Op: SkillProgramEffectOp.ResolveLeiluanLastHandUseBenefit }] ||
            context.CardUse?.CardActionId != p.ActionId || !LeiluanActionMatches(p, window.Action)) return false;
        return !CompleteProgramEventHistory().OfType<LeiluanBenefitIssuedEvent>().Any(e => e.PaymentId == p.Id);
    }
    private void AssertLeiluanPayments(CardUseFrame use)
    {
        if (use.LeiluanPayments is null) return;
        if (!HasLeiluanCapability || use.LeiluanPayments is not System.Collections.IList { IsReadOnly: true } || use.LeiluanPayments.Select(p => p.Id).Distinct().Count() != use.LeiluanPayments.Count) throw new InvalidOperationException("Leiluan lost its owning immutable native payment list.");
        var history = CompleteProgramEventHistory().ToArray();
        foreach (var p in use.LeiluanPayments)
        {
            if (p.UseId != use.Id || p.Id <= 0 || p.FrozenX < 1 || p.Materials.Count != p.FrozenX || p.Source.OwnerSeat != p.OwnerSeat || !IsValidPlayerSeat(p.OwnerSeat) ||
                _contentRegistry.GetSkill(p.Source.SkillId).Program is not { } program || program.GameplayHash != p.GameplayHash || ViewAsRule(p.Source)?.LeiluanBasicUse is null ||
                p.Materials is not System.Collections.IList { IsReadOnly: true } || p.HandBefore is not System.Collections.IList { IsReadOnly: true } || p.Movements is not System.Collections.IList { IsReadOnly: true } ||
                p.LastHand != (p.HandBefore.Count > 0 && p.HandBefore.All(id => p.Materials.Any(c => c.CardId == id && c.From == CardLocation.Hand(p.OwnerSeat)))) ||
                history.OfType<LeiluanPaymentPreparedEvent>().Count(e => e == new LeiluanPaymentPreparedEvent(p.Id, p.UseId, p.Source, p.GameplayHash, p.OwnerSeat, p.ActualTurn, p.TurnOwner, p.Round, p.Kind, p.ResponseUse, p.FrozenX, p.HandBefore.Count, p.Before)) != 1 ||
                !history.OfType<LeiluanMaterialFrozenEvent>().Where(e => e.PaymentId == p.Id).OrderBy(e => e.Index).Select(e => new LeiluanMaterial(e.CardId, e.Kind, e.From)).SequenceEqual(p.Materials) ||
                !history.OfType<LeiluanHandFrozenEvent>().Where(e => e.PaymentId == p.Id).OrderBy(e => e.Index).Select(e => e.CardId).SequenceEqual(p.HandBefore) ||
                p.Paid != (p.Movements.Count == p.Materials.Count) || p.Movements.Any(m => m.Sequence <= p.Before || !_cardMovements.Contains(m) || !p.Materials.Any(c => c.CardId == m.CardId && c.From == m.From && c.Kind == m.CardKind)))
                throw new InvalidOperationException("Leiluan changed its frozen complete cost, original Hand or exact paid movement proof.");
            if (p.Paid && p.ActionId > 0 && history.OfType<LeiluanPaymentBoundEvent>().Count(e => e == new LeiluanPaymentBoundEvent(p.Id, p.UseId, p.ActionId, p.LastHand, p.After)) != 1) throw new InvalidOperationException("Leiluan payment has no unique native action binding.");
        }
    }
}