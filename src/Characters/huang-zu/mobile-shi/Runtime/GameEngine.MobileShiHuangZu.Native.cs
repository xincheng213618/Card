namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 47900)]
    [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 47900)]
    [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash, 47900)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 47900)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash, 47900)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPaidWeaponSlash, 47900)]
    [ProgramDistanceRule(ProgramDistanceStage.SpecificSlashExemption, 47900)]
    private bool ShiHzSlashDistance(ProgramSlashDistanceQuery q) => IsSlashCard(q.Kind) &&
        CardPolicies(q.Actor, SkillProgramCardPolicyKind.IgnoreUseDistance, q.Kind).Any(p => p.Source.SkillId == ShiHzChizhang);
    private bool IsShiHzOriginalUseChoice(CharacterState owner, Card card, CardKind kind)
    {
        if (!owner.IsAlive || card.Kind != kind || !IsSlashCard(kind) || _winner != Winner.None ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiHuangZu: { Stage: ShiHzStage.IssuingUse } r,
                WindowContext: { Window: SkillProgramTriggerWindow.ActualPhaseEnded, ActualPhaseEnd: { } due } } f ||
            f.OwnerSeat != owner.Seat || f.SkillId != ShiHzDuanyang || f.GameplayHash != r.GameplayHash || r.ActualTurn != _turnNumber ||
            ShiHzGrant(r.GrantId) is not { } g) return false;
        return r.CardId == card.Id && g.CardId == card.Id && r.Source == g.Source && r.GameplayHash == g.GameplayHash &&
            due.EntitlementId == g.GrantId && due.Phase == g.Phase && g.Phase.TurnNumber == _turnNumber &&
            g.Source.OwnerSeat == owner.Seat && _cardZones.GetLocation(card.Id) == g.Pile.Location &&
            _publicPersistentPiles.GetValueOrDefault((g.Pile.OwnerSeat, g.Pile.SkillId, g.Pile.SkillInstanceId)) == g.Pile &&
            ProgramEventHistory<ShiHzGrantConsumedEvent>().Count(e => e.GrantId == g.GrantId && e.FrameId == f.Id) == 1;
    }
    private bool IsShiHzOriginalUseParent(CardUseFrame use)
    {
        var n = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (use.Action is not { Type: CardActionType.Use, PhysicalCards: [var cost], ConversionChain.Count: 0 } action ||
            action.ProviderSeat != action.ActorSeat || n < 1 ||
            _resolutionStack[n - 1] is not ProgramSkillFrame { ShiHuangZu: { Stage: ShiHzStage.IssuingUse } r } f ||
            ShiHzGrant(r.GrantId) is not { } g) return false;
        return action.ActorSeat == f.OwnerSeat && use.SourceSeat == f.OwnerSeat && use.CardId == r.CardId &&
            cost.CardId == r.CardId && cost.From == g.Pile.Location && cost.CardKind == GetAdvancedCard(r.CardId).Kind &&
            use.CardKind == cost.CardKind && IsSlashCard(use.CardKind) && use.TargetSeats.SequenceEqual([r.Target]) &&
            use.PhysicalCardIds is [var id] && id == r.CardId &&
            r.Source == g.Source && r.GameplayHash == g.GameplayHash && r.ActualTurn == _turnNumber &&
            ProgramEventHistory<ShiHzGrantConsumedEvent>().Count(e => e.GrantId == g.GrantId && e.FrameId == f.Id) == 1;
    }
    private void IssueShiHzNativeUse(ProgramSkillFrame f)
    {
        var r = f.ShiHuangZu!;
        if (r.Stage != ShiHzStage.IssuingUse || !ShiHzTargets(f).Contains(r.Target) || ShiHzGrant(r.GrantId) is not { } g)
            throw new InvalidOperationException("Duanyang cannot replace its stored original Slash or chosen native target.");
        var owner = _players[f.OwnerSeat]; var target = _players[r.Target]; var card = GetAdvancedCard(r.CardId);
        SelectUseConversion(new(LegalActionKind.Slash, card.Id, target.Seat, "使用断鞅储存的原牌", card.Kind));
        var id = BeginCardUse(card, owner.Seat, [target.Seat], card.Kind, physicalCardIds: [card.Id]);
        var use = LifecycleCardUse(id)!;
        if (!IsShiHzOriginalUseParent(use)) throw new InvalidOperationException("Duanyang lost its original public-pile material on native declaration.");
        var origin = new ShiHzNativeUse(r.GrantId, f.Id, r.Source, r.GameplayHash, r.ActualTurn, card.Id, card.Kind, g.Pile.Location, target.Seat, id, use.Action!.ActionId);
        var parent = _resolutionStack.OfType<ProgramSkillFrame>().Single(p => p.Id == f.Id);
        ReplaceRuntimeFrame(f.Id, f = parent with { ShiHuangZu = r with { Stage = ShiHzStage.NativeUse, UseId = id, ActionId = origin.ActionId } });
        UpdateLifecycleCardUse(id, u => u with { ShiHuangZuUse = origin, UnlimitedUse = true });
        AdvanceEventRulesAndQueueFact(new ShiHzNativeIssuedEvent(g.GrantId, f.Id, id, origin.ActionId, card.Id, target.Seat));
        MoveCard(card, g.Pile.Location, CardLocation.Processing, CardMoveReasons.Use);
        var attack = new CardAttackHandle(this, id, owner.Seat, target.Seat, card, damageAmount: 1 + owner.AlcoholDamageBonus,
            playedCardKind: card.Kind, physicalCards: [card], ignoresArmor: HasCardArmorBypass(owner, target, card.Kind), programSkillCardUseFrameId: f.Id);
        ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(owner.Seat, card.Kind); CaptureProgramAlcoholConsumption(id, owner); owner.HasAlcoholEffect = false;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(card.Id, card.Kind, owner.Seat, target.Seat, IgnoresArmor: attack.IgnoresArmor)); NotifyAiOfSlash(owner, target);
        if (!TryMarkProgramUseCommitted(id) || !TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!, SkillProgramTriggerWindow.CardUseCommitted, [target.Seat], ProgramCardContinuation.CommittedSlash))
            BeginSlashTargetResolution(attack);
    }
    private bool ShiHzUseRidesOn(CardUseFrame use, ProgramSkillFrame f) => use.ShiHuangZuUse is { } n &&
        f.ShiHuangZu is { } r && n.ParentId == f.Id && n.Source == r.Source && n.GameplayHash == f.GameplayHash &&
        n.GrantId == r.GrantId && n.ActualTurn == _turnNumber && n.UseId == use.Id && n.UseId == r.UseId &&
        n.ActionId == use.Action?.ActionId && n.ActionId == r.ActionId && n.CardId == r.CardId && n.Target == r.Target &&
        use.CardKind == n.Kind && use.SourceSeat == f.OwnerSeat && use.CardId == n.CardId && use.PhysicalCardIds is [var id] && id == n.CardId &&
        use.Action is { Type: CardActionType.Use, PhysicalCards: [var cost], ConversionChain.Count: 0 } a &&
        ShownEntityUseActorMatches(use, f.OwnerSeat, f.OwnerSeat) && cost.CardId == n.CardId && cost.From == n.From && cost.CardKind == n.Kind &&
        ProgramEventHistory<ShiHzNativeIssuedEvent>().Count(e => e.FrameId == f.Id && e.UseId == use.Id && e.ActionId == a.ActionId && e.CardId == id) == 1;
    private bool IsShiHzOriginalActionCost(CardActionContext action, CardActionCost cost)
    {
        if (!HasShiHz || action is not { Type: CardActionType.Use, PhysicalCards: [var only], ConversionChain.Count: 0 } ||
            only != cost || action.ActorSeat != action.ProviderSeat || action.RequesterSeat is not null ||
            ProgramEventHistory<ShiHzNativeIssuedEvent>().SingleOrDefault(e => e.ActionId == action.ActionId && e.CardId == cost.CardId) is not { } issued ||
            ShiHzGrant(issued.GrantId) is not { } g || g.Source.OwnerSeat != action.ActorSeat || cost.From != g.Pile.Location ||
            action.EffectiveKind != cost.CardKind || !IsSlashCard(cost.CardKind)) return false;
        return ProgramEventHistory<ShiHzGrantConsumedEvent>().Count(e => e.GrantId == g.GrantId && e.FrameId == issued.FrameId) == 1 &&
            _cardMovements.Count(m => m.CardId == cost.CardId && m.From == cost.From && m.To == CardLocation.Processing &&
                m.Reason == CardMoveReasons.Use && m.Sequence > g.DiscardSequence && m.TurnNumber == g.Phase.TurnNumber) == 1;
    }
    [NativeUseValidation(47900)]
    private void ValidateShiHzNativeUse(CardUseFrame use)
    {
        if (use.ShiHuangZuUse is { } n && (use.Step != ResolutionFrameStep.Completed ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == n.ParentId) is not { } f || !ShiHzUseRidesOn(use, f)))
            throw new InvalidOperationException("Duanyang cannot retire a different or incomplete original Slash Use.");
    }
    [NativeUseReturn(47900)]
    private bool ReturnShiHzNativeUse(CardUseFrame use)
    {
        if (use.ShiHuangZuUse is not { } n) return false;
        var f = GetActiveProgramFrame(n.ParentId);
        if (LifecycleCardUse(use.Id) is not null || !ShiHzUseRidesOn(use, f) ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1 ||
            ProgramEventHistory<ShiHzNativeReturnedEvent>().Any(e => e.UseId == use.Id))
            throw new InvalidOperationException("Duanyang requires one exact whole native use return.");
        AdvanceEventRulesAndQueueFact(new ShiHzNativeReturnedEvent(n.GrantId, use.Id));
        ReplaceRuntimeFrame(f.Id, f with { ShiHuangZu = f.ShiHuangZu! with { Stage = ShiHzStage.ReturnedUse } });
        if (use.CardAttack is not { Active: true }) ContinueShiHz(f.Id);
        return true;
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 47900)]
    private bool ResumeShiHzAttack()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiHuangZu: { Stage: ShiHzStage.ReturnedUse } } f) return false;
        ContinueShiHz(f.Id); return true;
    }
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.MatchBatch, 47900)]
    private bool ShiHzNativeMaterialBatch(CardUseFrame use, CardMovementBatchContext batch) => use.ShiHuangZuUse is { } n &&
        _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == n.ParentId) is { } f && ShiHzUseRidesOn(use, f) &&
        batch.ParentFrameId == use.Id && batch.Movements.Any(m => m.CardId == n.CardId && m.From == n.From && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use);
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashCountExemption, 47900)]
    private bool ShiHzExemptDebit(long useId, int actor) => LifecycleCardUse(useId) is { ShiHuangZuUse: { } n } use && n.Source.OwnerSeat == actor &&
        _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == n.ParentId) is { } f && ShiHzUseRidesOn(use, f);

    // The nearest whole Use owns this response. A nested unrelated Use cannot
    // inherit the outer ban; counterspell and Duel response frames retain it.
    private bool ShiHzResponseColorAllowed(int responder, bool? effectiveIsRed)
    {
        if (effectiveIsRed is not { } red || _resolutionStack.OfType<CardUseFrame>().LastOrDefault() is not
                { ShiHuangZuColors: { } ban, Action: { } action } use || responder == action.ActorSeat) return true;
        if (ban.UseId != use.Id || ban.ActionId != action.ActionId || ban.OwnerSeat != action.ActorSeat ||
            ProgramEventHistory<ShiHzColorsBoundEvent>().Count(e => e == ban) != 1)
            throw new InvalidOperationException("Chizhang response colors lost the exact original Use and paid instruction.");
        return red ? !ban.Red : !ban.Black;
    }
    private bool? ShiHzResponseColor(CharacterState owner, IReadOnlyList<int> cards, CardConversionSource? source)
    {
        if (cards.Count == 0) return null;
        var material = cards.Select(GetAdvancedCard).ToArray();
        if (material.Length == 1)
        {
            var appeared = ApplyProgramUseAppearance(owner, material[0], source);
            return SuitColor(EffectiveSuit(owner, appeared));
        }
        return PhysicalGroupColor(owner, material);
    }
    [ProgramCardLegality(ProgramCardLegalityStage.NativeResponseBan, 47900)]
    private bool ShiHzNativeResponseBan(ProgramNativeResponseQuery q) => !q.DyingUse &&
        !ShiHzResponseColorAllowed(q.Provider.Seat, ShiHzResponseColor(q.Provider, q.CardIds, q.Source));
    [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan, 47900)]
    private bool ShiHzViewAsResponseBan(ProgramViewAsLegalityQuery q) => q.ForResponse && !q.DyingUse &&
        !ShiHzResponseColorAllowed(q.Owner.Seat, ShiHzResponseColor(q.Owner, q.CardIds, q.Source));
    [ProgramCardLegality(ProgramCardLegalityStage.SubmittedUseValidation, 47900)]
    private void ShiHzSubmittedResponse(ProgramSubmittedUseQuery q)
    {
        if (_pendingDecision?.Kind is not (DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)) return;
        if (!ShiHzResponseColorAllowed(q.ActorSeat, ShiHzResponseColor(_players[q.ActorSeat], q.CardIds, q.ConversionChain.LastOrDefault())))
            throw new InvalidOperationException("This response color is prohibited by the exact incoming Chizhang Use.");
    }
}
