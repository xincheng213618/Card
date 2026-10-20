namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<ProgramOrdinaryTrickUseOption> MouXuHuangDuelOptions(ProgramSkillFrame f) => Array.AsReadOnly(
        BuildProgramOrdinaryTrickUseOptions(_players[f.OwnerSeat], CardKind.Duel, Suit.None, enforceUsePermission: true,
            beneficiaryShieldSuit: Suit.None, actualEffectiveColor: null, hasActualColor: true, physicalCardIds: [],
            includeNextActualUseAdjustment: true, conversionSource: f.MouXuHuangDuanliang!.Source)
        .Where(o => o.ActionKind == LegalActionKind.Duel && o.TargetSeats.Count > 0 && o.TargetSeats[0] == f.MouXuHuangDuanliang.TargetSeat).ToArray());
    private IReadOnlyList<PromptChoice> MouXuHuangDuelChoices(ProgramSkillFrame f) => Array.AsReadOnly(MouXuHuangDuelOptions(f).Select(o =>
        new PromptChoice(new($"mou-xu-huang.{f.Id}.duel.{o.Id.Value}"), o.Description, [], o.TargetSeats,
            new Dictionary<string, string> { ["program-action"] = "mou-xu-huang-duel-targets", ["frame-id"] = f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["option-id"] = o.Id.Value })).ToArray());
    private void BeginMouXuHuangDuelTargets(ProgramSkillFrame f)
    {
        var options = MouXuHuangDuelOptions(f);
        if (options.Count == 0) { FinishMouXuHuangDuanliang(f); return; }
        if (options.Count == 1) { AcceptMouXuHuangDuelTargets(f, options[0]); return; }
        ReplaceRuntimeTop(f = f with { MouXuHuangDuanliang = f.MouXuHuangDuanliang! with { Stage = MouXuHuangDuanliangStage.DuelTargets } });
        PublishMouXuHuang(f, f.OwnerSeat, MouXuHuangDuelChoices(f), "擂鼓进军：选择本次原生决斗的目标调整。");
    }
    [ProgramAnswer("mou-xu-huang-duel-targets")]
    private void AnswerMouXuHuangDuelTargets(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id);
        if (!MouXuHuangDuanliangRoot(f) || f.MouXuHuangDuanliang!.Stage != MouXuHuangDuanliangStage.DuelTargets ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([selected], MouXuHuangDuelChoices(f).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Duanliang's optional native target adjustment lost its exact current producer.");
        var option = MouXuHuangDuelOptions(f).Single(o => o.Id.Value == selected.Parameters["option-id"]);
        ClearPendingDecision(); AcceptMouXuHuangDuelTargets(f, option);
    }
    private void AcceptMouXuHuangDuelTargets(ProgramSkillFrame f, ProgramOrdinaryTrickUseOption option)
    {
        ReplaceRuntimeTop(f = f with { MouXuHuangDuanliang = f.MouXuHuangDuanliang! with { NativeTargets = option.TargetSeats } });
        foreach (var pair in option.TargetSeats.Select((seat, index) => (seat, index)))
            AdvanceEventRulesAndQueueFact(new MouXuHuangDuelTargetSelectedEvent(f.Id, pair.index, pair.seat, option.NextActualUseAdjusted));
        if (option.NextActualUseAdjusted) _selectedNextCardTargetSeats = option.TargetSeats;
        IssueMouXuHuangUse(f, new(0, CardKind.Duel, Suit.None, 0), CardKind.Duel);
    }
    private bool CanMouXuHuangDuel(int actor, int target, CardConversionSource source) =>
        _players[actor].IsAlive && _players[target].IsAlive && actor != target && _winner == Winner.None &&
        !HasTurnCardTargetRestriction(actor, SkillProgramCardTargetRestriction.SelfOnly) &&
        !IsCardUseForbidden(actor, CardKind.Duel, CardActionType.Use) && TargetRules.AllowsNativeUseMaterial(new(actor, CardKind.Duel, [], source)) &&
        !IsCuiYanNextUseForbidden(actor, [], null) &&
        BuildProgramOrdinaryTrickUseOptions(_players[actor], CardKind.Duel, Suit.None, enforceUsePermission: true,
            beneficiaryShieldSuit: Suit.None, actualEffectiveColor: null, hasActualColor: true, physicalCardIds: [], conversionSource: source)
            .Any(p => p.ActionKind == LegalActionKind.Duel && p.TargetSeats.SequenceEqual([target]));
    private bool CanMouXuHuangSupply(ProgramSkillFrame f, Card card)
    {
        var r = f.MouXuHuangDuanliang!; var actor = _players[f.OwnerSeat]; var target = _players[r.TargetSeat];
        var suit = EffectiveSuit(actor, card); var color = CapturePhysicalCardColor(actor.Seat, card);
        return actor.IsAlive && target.IsAlive && actor.Seat != target.Seat && _winner == Winner.None &&
            _cardZones.GetLocation(card.Id) == CardLocation.DrawPile && !target.JudgmentAreaAbolished &&
            !HasJudgmentEffectiveCard(target, CardKind.SupplyShortage) && !HasCardPolicy(target, SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget) &&
            !IsCardUseForbidden(actor.Seat, CardKind.SupplyShortage, CardActionType.Use) &&
            !IsTurnPhysicalUseForbidden(actor.Seat, [card.Id]) && !IsTurnSuitUseForbidden(actor.Seat, suit) &&
            TargetRules.AllowsNativeUseMaterial(new(actor.Seat, CardKind.SupplyShortage, [card.Id], r.Source)) && !IsCuiYanNextUseForbidden(actor.Seat, [card.Id], suit) &&
            !HasTurnCardTargetRestriction(actor.Seat, SkillProgramCardTargetRestriction.SelfOnly) &&
            !IsDirectedCardTargetProhibited(actor.Seat, target.Seat, CardKind.SupplyShortage) &&
            !IsCardTargetProhibited(target, CardKind.SupplyShortage, suit, color) &&
            !IsShenwuRobeTargetProhibited(actor.Seat, target.Seat, CardKind.SupplyShortage) && !HasBeneficiarySuitShield(actor.Seat, target.Seat, suit);
    }
    private void IssueMouXuHuangTop(ProgramSkillFrame f)
    {
        var card = _cardZones.CardsAt(CardLocation.DrawPile).LastOrDefault();
        if (card is null || !CanMouXuHuangSupply(f, card)) { FinishMouXuHuangDuanliang(f); return; }
        IssueMouXuHuangUse(f, card, CardKind.SupplyShortage);
    }
    private void IssueMouXuHuangUse(ProgramSkillFrame f, Card card, CardKind kind)
    {
        var r = f.MouXuHuangDuanliang!;
        if (!MouXuHuangDuanliangRoot(f) || !r.Revealed || !r.Success || r.Native is not null ||
            kind == CardKind.Duel && !CanMouXuHuangDuel(f.OwnerSeat, r.TargetSeat, r.Source) ||
            kind == CardKind.SupplyShortage && !CanMouXuHuangSupply(f, card) || kind is not (CardKind.Duel or CardKind.SupplyShortage))
            throw new InvalidOperationException("Duanliang must issue its chosen successful legal native consequence exactly once.");
        var origin = new MouXuHuangNativeOrigin(f.Id, r.Source, f.GameplayHash, _resolutionSequence + 1, _cardActionSequence + 1,
            f.OwnerSeat, r.TargetSeat, kind, card.Id, card.Kind, card.Id == 0 ? null : CardLocation.DrawPile,
            card.Id == 0 ? Suit.None : EffectiveSuit(_players[f.OwnerSeat], card), card.Rank,
            card.Id == 0 ? null : CapturePhysicalCardColor(f.OwnerSeat, card));
        ReplaceRuntimeTop(f = f with { MouXuHuangDuanliang = r with { Stage = MouXuHuangDuanliangStage.NativeIssuing, Native = origin, Before = _movementSequence, After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new MouXuHuangNativeIssuedEvent(origin));
        var id = BeginCardUse(card, f.OwnerSeat, kind == CardKind.Duel ? r.NativeTargets : [r.TargetSeat], kind,
            physicalCardIds: card.Id == 0 ? [] : [card.Id], conversionSource: r.Source);
        if (id != origin.UseId || !IsMouXuHuangNativeUse(id)) throw new InvalidOperationException("Duanliang changed its signed native Use identity.");
        if (card.Id > 0)
        {
            MoveCard(card, CardLocation.DrawPile, CardLocation.Processing, CardMoveReasons.Use, m =>
            {
                if (_resolutionStack.LastOrDefault() is not CardUseFrame { MouXuHuangNative: { } signed } live || live.Id != id || signed != origin ||
                    _resolutionStack.Count < 2 || _resolutionStack[^2] is not ProgramSkillFrame { MouXuHuangDuanliang: { Stage: MouXuHuangDuanliangStage.NativeUse, PaymentSequence: null, Native: { } parentOrigin } } parent ||
                    parent.Id != f.Id || parentOrigin != origin || !MouXuHuangDuanliangRoot(parent) || !IsMouXuHuangNativeUse(id) ||
                    m.CardId != origin.CardId || m.CardKind != origin.PrintedKind || m.From != origin.From || m.To != CardLocation.Processing || m.Reason != CardMoveReasons.Use)
                    throw new InvalidOperationException("Duanliang top must pay its one original real DrawPile entity.");
                ReplaceRuntimeFrame(parent.Id, parent with { MouXuHuangDuanliang = parent.MouXuHuangDuanliang with { PaymentSequence = m.Sequence, After = m.Sequence } });
                AdvanceEventRulesAndQueueFact(new MouXuHuangTopPaidEvent(f.Id, id, m.CardId, m.Sequence));
            });
        }
        BeginJizhiOrNullificationWindow(id, card, f.OwnerSeat, LifecycleCardUse(id)!.TargetSeats,
            kind == CardKind.Duel ? LegalActionKind.Duel : LegalActionKind.SupplyShortage, playedCardKind: kind);
    }
    private CardActionContext PrepareMouXuHuangAction(CardActionContext action)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouXuHuangDuanliang: { Stage: MouXuHuangDuanliangStage.NativeIssuing, Native: { } o } } f) return action;
        if (!MouXuHuangDuanliangRoot(f) || action.Type != CardActionType.Use || action.ActionId != o.ActionId || action.ActorSeat != o.ActorSeat ||
            action.ProviderSeat != o.ActorSeat || action.RequesterSeat is not null || action.EffectiveKind != o.Kind ||
            !action.TargetSeats.SequenceEqual(o.Kind == CardKind.Duel ? f.MouXuHuangDuanliang.NativeTargets : [o.TargetSeat]) ||
            !action.ConversionChain.SequenceEqual([o.Source]) || (o.CardId == 0 ? action.PhysicalCards.Count != 0 :
                action.PhysicalCards is not [var cost] || cost.CardId != o.CardId || cost.CardKind != o.PrintedKind || cost.From != o.From))
            throw new InvalidOperationException("Xu Huang's native declaration must retain its exact chosen source, original top or empty Duel materials.");
        return CaptureFactionAction(new CardActionContext(action.ActionId, action.ParentActionId, action.Type, action.ActorSeat, action.ProviderSeat,
            action.RequesterSeat, action.ResponderSeat, action.OpponentSeat, action.EffectiveKind, action.TargetSeats, action.PhysicalCards,
            action.ConversionChain, action.DesignatedTargetSeats, effectiveSuit: o.Suit, effectiveRank: o.Rank,
            effectiveIsRed: o.EffectiveIsRed));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 29500)]
    private void BindMouXuHuangDeclared(IGameEvent fact)
    {
        if (fact is not CardUseDeclaredEvent e || LifecycleCardUse(e.ResolutionId) is not { } use) return;
        var index = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame { MouXuHuangDuanliang: { Stage: MouXuHuangDuanliangStage.NativeIssuing, Native: { } o } r } f) return;
        if (!MouXuHuangDuanliangRoot(f) || use.Id != o.UseId || use.Action?.ActionId != o.ActionId || use.CardId != o.CardId || use.CardKind != o.Kind)
            throw new InvalidOperationException("Xu Huang cannot bind another producer's native declaration.");
        UpdateLifecycleCardUse(use.Id, u => u with { MouXuHuangNative = o });
        ReplaceRuntimeFrame(f.Id, f with { MouXuHuangDuanliang = r with { Stage = MouXuHuangDuanliangStage.NativeUse } });
    }
    private bool IsMouXuHuangNativeUse(long id)
    {
        if (LifecycleCardUse(id) is not { MouXuHuangNative: { } o, Action: { Type: CardActionType.Use } a } use || o.UseId != id || o.ActionId != a.ActionId ||
            use.CardId != o.CardId || use.CardKind != o.Kind || a.EffectiveKind != o.Kind || a.ProviderSeat != o.ActorSeat || a.RequesterSeat is not null ||
            a.EffectiveSuit != o.Suit || a.EffectiveRank != o.Rank || a.EffectiveIsRed != o.EffectiveIsRed || !a.ConversionChain.SequenceEqual([o.Source]) ||
            !MouGuquActorLineageMatches(id, a.ActionId, o.ActorSeat, a.ActorSeat, a.ProviderSeat, a.Type) || use.SourceSeat != a.ActorSeat ||
            _resolutionStack.SingleOrDefault(x => x.Id == o.ProgramFrameId) is not ProgramSkillFrame { MouXuHuangDuanliang: { Native: { } expected, Stage: MouXuHuangDuanliangStage.NativeUse } } f ||
            expected != o || !MouXuHuangDuanliangRoot(f) || ProgramEventHistory<MouXuHuangNativeIssuedEvent>().Count(e => e.Origin == o) != 1) return false;
        return o.CardId == 0 ? o.Kind == CardKind.Duel && use.PhysicalCardIds is { Count: 0 } && a.PhysicalCards.Count == 0 && a.EffectiveIsRed is null :
            o.Kind == CardKind.SupplyShortage && use.PhysicalCardIds is [var material] && material == o.CardId &&
            a.PhysicalCards is [var cost] && cost.CardId == o.CardId && cost.CardKind == o.PrintedKind && cost.From == CardLocation.DrawPile;
    }
    private bool IsMouXuHuangZeroUse(long id) => LifecycleCardUse(id) is { CardId: 0 } && IsMouXuHuangNativeUse(id);
    private bool MatchesMouXuHuangNativeAction(long id, CardActionContext action, int cardId) =>
        cardId == 0 && IsMouXuHuangZeroUse(id) && LifecycleCardUse(id)?.Action is { } current && action.ActionId == current.ActionId &&
        action.Type == current.Type && action.ActorSeat == current.ActorSeat && action.ProviderSeat == current.ProviderSeat && action.EffectiveKind == current.EffectiveKind &&
        action.PhysicalCards.Count == 0 && action.ConversionChain.SequenceEqual(current.ConversionChain);
    private Card MouXuHuangRepresentation(long id) => IsMouXuHuangZeroUse(id) ? new(0, CardKind.Duel, Suit.None, 0) : throw new InvalidOperationException("Xu Huang lost its signed zero-material Duel.");
    private Card ReadMouXuHuangUseAppearance(long id, CardAppearanceReference appearance)
    {
        if (!IsMouXuHuangZeroUse(id)) return ReadMouLiuChengUseAppearance(id, appearance);
        if (appearance.Id != 0 || appearance.Kind != CardKind.Duel || appearance.Suit != Suit.None || appearance.Rank != 0)
            throw new InvalidOperationException("Xu Huang's empty native Duel changed its neutral appearance.");
        return MouXuHuangRepresentation(id);
    }
    private long? MouXuHuangDuelProgramParent(long id) => IsMouXuHuangZeroUse(id) ? LifecycleCardUse(id)!.MouXuHuangNative!.ProgramFrameId : null;
    private bool IsMouXuHuangDuelAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing) => IsMouXuHuangZeroUse(attack.ResolutionId) &&
        attack.Card is null && attack.PhysicalCards.Count == 0 && processing.Count == 0 && attack.EffectiveCardKind == CardKind.Duel &&
        attack.ProgramSkillCardUseFrameId == LifecycleCardUse(attack.ResolutionId)!.MouXuHuangNative!.ProgramFrameId;
    [NativeUseValidation(29500)]
    private void ValidateMouXuHuangNative(CardUseFrame use)
    {
        if (use.MouXuHuangNative is not null && (!IsMouXuHuangNativeUse(use.Id) || use.Step != ResolutionFrameStep.Completed ||
            use.PendingRecoveryAttempts is { Count: > 0 } || use.RecoveryPaidContinuation is not null)) throw new InvalidOperationException("Xu Huang cannot return an incomplete or unproven native Use.");
    }
    [NativeUseReturn(29500)]
    private void ReturnMouXuHuangNative(CardUseFrame use)
    {
        if (use.MouXuHuangNative is not { } o) return;
        var f = GetActiveProgramFrame(o.ProgramFrameId); var r = f.MouXuHuangDuanliang!;
        if (!MouXuHuangDuanliangRoot(f) || r.Native != o || r.NativeReturned || LifecycleCardUse(use.Id) is not null || use.Step != ResolutionFrameStep.Completed)
            throw new InvalidOperationException("Xu Huang's completed native Use must return once to its own original strategy.");
        var batches = _pendingCardsMovedBatches.Where(b => b.ParentFrameId == use.Id && b.AwaitingProgramFrameId is null &&
            b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId && b.OriginOwnerSeat == f.OwnerSeat).ToArray();
        foreach (var batch in batches)
        {
            AdvanceEventRulesAndQueueFact(new MouXuHuangNativeBatchSignedEvent(f.Id, use.Id, batch.Id, batch.ParentBatchId, batch.Movements.Count));
            foreach (var m in batch.Movements) SignMouXuHuangMovement(f, m);
        }
        var attackPending = use.CardAttack is { Active: true };
        ReplaceRuntimeTop(f = f with { MouXuHuangDuanliang = r with { Stage = MouXuHuangDuanliangStage.NativeChildren, NativeReturned = true,
            AttackReturned = !attackPending, NativeBatchIds = batches.Select(b => b.Id).ToArray() }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouXuHuangNativeReturnedEvent(f.Id, use.Id, o.ActionId));
        if (!attackPending) ContinueMouXuHuangDuanliang(f.Id);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 29500)]
    private bool ReturnMouXuHuangNativeAttack(AttackCompletionReceipt completion)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouXuHuangDuanliang: { NativeReturned: true, AttackReturned: false, Native: { } o } r } f ||
            completion.ResolutionId != o.UseId) return false;
        if (completion.ProgramFrameId != f.Id || !MouXuHuangDuanliangRoot(f)) throw new InvalidOperationException("Xu Huang's Duel returned to another owning strategy.");
        ReplaceRuntimeTop(f = f with { MouXuHuangDuanliang = r with { AttackReturned = true } });
        AdvanceEventRulesAndQueueFact(new MouXuHuangNativeAttackReturnedEvent(f.Id, o.UseId)); ContinueMouXuHuangDuanliang(f.Id); return true;
    }
    private bool IsMouXuHuangNativeSettlementBatch(ProgramSkillFrame f, CardMovementBatchContext b) =>
        f.MouXuHuangDuanliang is { Stage: MouXuHuangDuanliangStage.NativeChildren, NativeReturned: true, Native: { } o } r && MouXuHuangDuanliangRoot(f) &&
        r.NativeBatchIds.Contains(b.Id) && b.ParentFrameId == o.UseId && b.AwaitingProgramFrameId is null && b.Movements.Count > 0 &&
        b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId && b.OriginOwnerSeat == f.OwnerSeat &&
        ProgramEventHistory<MouXuHuangNativeBatchSignedEvent>().Count(e => e == new MouXuHuangNativeBatchSignedEvent(f.Id, o.UseId, b.Id, b.ParentBatchId, b.Movements.Count)) == 1 &&
        b.Movements.All(m => MouXuHuangMovementSigned(f, m));
}
