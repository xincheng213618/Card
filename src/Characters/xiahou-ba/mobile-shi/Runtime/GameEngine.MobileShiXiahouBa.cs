using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShiXbLibbing = ShiXiahouBaComposition.LibbingSkill, ShiXbPoxi = ShiXiahouBaComposition.PoxiSkill;

    private static CardConversionSource ShiXbSource(ProgramSkillFrame f) =>
        new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool HasShiXbCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveShiXbLibbing);

    // ---- 厉兵 ledger: the event history is the only state store ----------------

    private (int Last, int Count1, int Count2, int Count3) ShiXbLibbingState(int ownerSeat)
    {
        var last = 0; var count1 = 0; var count2 = 0; var count3 = 0;
        foreach (var payload in CompleteProgramEventHistory())
        {
            switch (payload)
            {
                case ShiXbLibbingAdvancedEvent { Source.OwnerSeat: var seat } advanced when seat == ownerSeat:
                    last = advanced.Option;
                    switch (advanced.Option)
                    {
                        case 1: count1++; break;
                        case 2: count2++; break;
                        default: count3++; break;
                    }
                    break;
                case ShiXbLibbingResetEvent { Source.OwnerSeat: var owner } when owner == ownerSeat:
                    last = 0; count1 = 0; count2 = 0; count3 = 0;
                    break;
            }
        }
        return (last, count1, count2, count3);
    }

    private int ShiXbLibbingRangeBonus(int seat) => !HasShiXbCapability ? 0 : ShiXbLibbingState(seat).Count1;

    private bool ShiXbLibbingIsDamageUse(ProgramCardUseContext use) =>
        ShiXiahouBaComposition.IsDamageCardKind(use.EffectiveKind);

    private bool ShiXbPoxiCannotRespond(int sourceSeat, int responderSeat, CardKind incomingKind,
        IReadOnlyList<int> targetSeats)
    {
        // The innermost incoming Use only: a nested unrelated Use cannot inherit the outer ban.
        if (_resolutionStack.LastOrDefault(x => x is CardUseFrame { ShiXiahouBaPoxi: not null }) is not CardUseFrame
            {
                Action: { } action, ShiXiahouBaPoxi: { } poxi
            })
            return false;
        return IsSlashCard(incomingKind) && action.Type == CardActionType.Use &&
            action.ActorSeat == sourceSeat && sourceSeat == poxi.Source.OwnerSeat &&
            action.ActionId == poxi.ActionId && targetSeats.Contains(responderSeat) &&
            GetHand(_players[responderSeat]).Count <= GetHand(_players[sourceSeat]).Count;
    }

    [ProgramCapability]
    private sealed class ShiXiahouBaCapability(GameEngine engine) : IShiXiahouBaProgramHost
    {
        public SkillProgramStepOutcome ResolveLibbing(ProgramSkillFrame frame) => engine.BeginShiXb(frame, SkillProgramEffectOp.ResolveShiXbLibbing);
        public SkillProgramStepOutcome ResolvePoxi(ProgramSkillFrame frame) => engine.BeginShiXb(frame, SkillProgramEffectOp.ResolveShiXbPoxi);
    }

    private SkillProgramStepOutcome BeginShiXb(ProgramSkillFrame input, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.ShiXiahouBa is not null)
            throw new InvalidOperationException("Shi Xiahou Ba restarted a running operation.");
        var r = op == SkillProgramEffectOp.ResolveShiXbLibbing && f.SkillId == ShiXbLibbing &&
            f.TriggerId == ShiXiahouBaComposition.LibbingTrigger
            ? StartShiXbLibbing(f)
            : op == SkillProgramEffectOp.ResolveShiXbPoxi && f.SkillId == ShiXbPoxi &&
                f.TriggerId == ShiXiahouBaComposition.PoxiTrigger ? StartShiXbPoxi(f)
            : throw new InvalidOperationException("Shi Xiahou Ba instruction has an incompatible owning skill.");
        ReplaceRuntimeTop(f = f with { ShiXiahouBa = r });
        if (r.Operation == "libbing")
        {
            ContinueShiXb(f.Id);
            return SkillProgramStepOutcome.AwaitChild;
        }
        PublishShiXb(GetActiveProgramFrame(f.Id));
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private ShiXiahouBaReceipt StartShiXbLibbing(ProgramSkillFrame f)
    {
        var use = f.WindowContext?.CardUse ??
            throw new InvalidOperationException("Libbing lost its completed-use window.");
        return new("libbing", _turnNumber)
        { Source = ShiXbSource(f), GameplayHash = f.GameplayHash, ActionId = use.CardActionId };
    }

    private ShiXiahouBaReceipt StartShiXbPoxi(ProgramSkillFrame f)
    {
        var (batch, index) = f.WindowContext is
            {
                Window: SkillProgramTriggerWindow.DiscardPileReceived,
                MovementBatch: { } movementBatch, MovementIndex: { } movementIndex
            } && movementIndex >= 0 && movementIndex < movementBatch.Movements.Count
            ? (movementBatch, movementIndex)
            : throw new InvalidOperationException("Poxi lost its exact discarded-slash movement.");
        var move = batch.Movements[index];
        if (move.To != CardLocation.DiscardPile || !_cardMovements.Contains(move) ||
            _cardZones.GetLocation(move.CardId) != CardLocation.DiscardPile ||
            !IsSlashCard(GetAdvancedCard(move.CardId).Kind))
            throw new InvalidOperationException("Poxi lost its discarded original Slash.");
        return new("poxi", _turnNumber)
        {
            Source = ShiXbSource(f), GameplayHash = f.GameplayHash, BatchId = batch.Id,
            CardId = move.CardId, DiscardMovementSequence = move.Sequence
        };
    }

    // ---- continuations -------------------------------------------------------

    private void ContinueShiXb(long id)
    {
        if (_pendingDecision is not null ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiXiahouBa: { } r } f || f.Id != id) return;
        if (r.Stage is ShiXiahouBaComposition.PoxiStageIssuingUse or ShiXiahouBaComposition.PoxiStageNativeUse) return;
        switch (r.Operation, r.Stage)
        {
            case ("libbing", 0):
                ContinueShiXbLibbing(GetActiveProgramFrame(id));
                return;
            case ("poxi", ShiXiahouBaComposition.PoxiStageChoice):
                PublishShiXb(GetActiveProgramFrame(id));
                return;
            case ("poxi", ShiXiahouBaComposition.PoxiStageReturnedUse):
                FinishShiXbOperation(GetActiveProgramFrame(id));
                return;
            default:
                throw new InvalidOperationException("Shi Xiahou Ba has an unknown continuation stage.");
        }
    }

    private void ContinueShiXbLibbing(ProgramSkillFrame f)
    {
        var r = f.ShiXiahouBa!;
        var use = f.WindowContext?.CardUse ??
            throw new InvalidOperationException("Libbing lost its completed-use window.");
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive)
        { FinishShiXbOperation(GetActiveProgramFrame(f.Id)); return; }
        var (last, count1, count2, count3) = ShiXbLibbingState(f.OwnerSeat);
        if (ShiXbLibbingIsDamageUse(use))
        {
            AdvanceEventRulesAndQueueFact(new ShiXbLibbingResetEvent(f.Id, r.Source!, f.GameplayHash, _turnNumber,
                use.CardActionId, use.EffectiveKind.ToString()));
            FinishShiXbOperation(GetActiveProgramFrame(f.Id));
            return;
        }
        var option = last switch { 1 => 2, 2 => 3, 3 => 1, _ => 1 };
        var (next1, next2, next3) = option switch
        {
            1 => (count1 + 1, count2, count3),
            2 => (count1, count2 + 1, count3),
            _ => (count1, count2, count3 + 1),
        };
        AdvanceEventRulesAndQueueFact(new ShiXbLibbingAdvancedEvent(f.Id, r.Source!, f.GameplayHash, _turnNumber,
            option, next1, next2, next3));
        if (option == 2)
        {
            // X counts the current execution too: the first option-2 draw is two cards.
            DrawProgramCards(f.Id, f.OwnerSeat, next2 + 1, null, null, SkillProgramCardSetVisibility.Private,
                new(ShiXiahouBaComposition.LibbingDrawReason));
        }
        FinishShiXbOperation(GetActiveProgramFrame(f.Id));
    }

    // ---- 迫袭 choice publication ---------------------------------------------

    private Card ShiXbPoxiCard(ProgramSkillFrame f)
    {
        var r = f.ShiXiahouBa!;
        return _cardZones.GetLocation(r.CardId) == CardLocation.DiscardPile ? GetAdvancedCard(r.CardId) :
            throw new InvalidOperationException("Poxi lost its discarded original Slash.");
    }

    private IReadOnlyList<int> ShiXbPoxiTargets(ProgramSkillFrame f)
    {
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive) return [];
        var owner = _players[f.OwnerSeat];
        var card = ShiXbPoxiCard(f);
        if (IsCardUseForbidden(owner.Seat, card.Kind, CardActionType.Use) ||
            !TargetRules.AllowsNativeUseMaterial(new(owner.Seat, card.Kind, [card.Id]))) return [];
        return _players.Where(player => player.IsAlive && player.Seat != owner.Seat &&
                CanUseSlashTarget(owner, player, card, ignoreDistance: true, physicalCardIds: [card.Id]))
            .Select(player => player.Seat).ToArray();
    }

    private IReadOnlyList<PromptChoice> ShiXbChoices(ProgramSkillFrame f)
    {
        var r = f.ShiXiahouBa!;
        if (r.Operation != "poxi" || r.Stage != ShiXiahouBaComposition.PoxiStageChoice) return [];
        var card = ShiXbPoxiCard(f);
        return Array.AsReadOnly(ShiXbPoxiTargets(f).Select(seat => new PromptChoice(
            new($"shi-xiahou-ba.{f.Id}.{r.Stage}.use.{seat}"),
            $"使用弃置的【{card.DisplayName}】对 {_players[seat].Name}（无距离限制）",
            [card.Id], [seat],
            new Dictionary<string, string>
            {
                ["program-action"] = ShiXiahouBaComposition.ProgramAction,
                ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                ["branch"] = "use", ["target"] = seat.ToString(CultureInfo.InvariantCulture),
            })).ToArray());
    }

    private void PublishShiXb(ProgramSkillFrame f)
    {
        var r = f.ShiXiahouBa!;
        var choices = ShiXbChoices(f);
        if (choices.Count == 0)
        { FinishShiXbOperation(GetActiveProgramFrame(f.Id)); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "迫袭：选择目标，改为使用弃置的【杀】。",
            choices.SelectMany(choice => choice.Cards).Distinct().ToArray(),
            choices.SelectMany(choice => choice.Targets).Distinct().ToArray(), f.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description)
        };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    [ProgramAnswer(ShiXiahouBaComposition.ProgramAction)]
    private void AnswerShiXb(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiXiahouBa: { Operation: "poxi" } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !ShiXbChoices(f).Any(choice => AssistedChoicesEqual([choice], [selected])))
            throw new InvalidOperationException("Shi Xiahou Ba choice lost its current frame, chooser or legal branch.");
        ClearPendingDecision();
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect,
            ShiXiahouBa = r with { Stage = ShiXiahouBaComposition.PoxiStageIssuingUse,
                TargetSeat = int.Parse(selected.Parameters["target"], CultureInfo.InvariantCulture) } });
        IssueShiXbNativeUse(GetActiveProgramFrame(f.Id));
    }

    private void IssueShiXbNativeUse(ProgramSkillFrame f)
    {
        var r = f.ShiXiahouBa!;
        var card = ShiXbPoxiCard(f);
        if (!ShiXbPoxiTargets(f).Contains(r.TargetSeat))
            throw new InvalidOperationException("Poxi cannot issue its use at a lost target.");
        var owner = _players[f.OwnerSeat];
        var target = _players[r.TargetSeat];
        SelectUseConversion(new(LegalActionKind.Slash, card.Id, target.Seat, "使用迫袭弃置的原牌", card.Kind));
        var useId = BeginCardUse(card, owner.Seat, [target.Seat], card.Kind, physicalCardIds: [card.Id]);
        var use = LifecycleCardUse(useId) ??
            throw new InvalidOperationException("Poxi lost its declared native use.");
        if (use.Action is not { Type: CardActionType.Use, PhysicalCards: [var cost], ConversionChain.Count: 0 } action ||
            action.ActorSeat != f.OwnerSeat || cost.CardId != card.Id || cost.From != CardLocation.DiscardPile ||
            cost.CardKind != card.Kind || use.CardKind != card.Kind || !use.TargetSeats.SequenceEqual([r.TargetSeat]) ||
            use.PhysicalCardIds is not [var physicalId] || physicalId != card.Id)
            throw new InvalidOperationException("Poxi lost its original discarded-pile material on native declaration.");
        var origin = new ShiXiahouBaPoxiUse(f.Id, r.Source!, f.GameplayHash, _turnNumber, card.Id, card.Kind,
            CardLocation.DiscardPile, r.TargetSeat, useId, action.ActionId);
        var parent = _resolutionStack.OfType<ProgramSkillFrame>().Single(p => p.Id == f.Id);
        ReplaceRuntimeFrame(f.Id, f = parent with { ShiXiahouBa = r with { Stage = ShiXiahouBaComposition.PoxiStageNativeUse,
            UseId = useId, ActionId = action.ActionId } });
        UpdateLifecycleCardUse(useId, u => u with { ShiXiahouBaPoxi = origin, UnlimitedUse = true });
        AdvanceEventRulesAndQueueFact(new ShiXbPoxiNativeIssuedEvent(f.Id, r.Source!, f.GameplayHash, _turnNumber,
            useId, action.ActionId, card.Id, r.TargetSeat));
        MoveCard(card, CardLocation.DiscardPile, CardLocation.Processing, new(ShiXiahouBaComposition.PoxiUseMoveReason));
        var attack = new CardAttackHandle(this, useId, owner.Seat, r.TargetSeat, card, damageAmount: 1 + owner.AlcoholDamageBonus,
            playedCardKind: card.Kind, physicalCards: [card],
            ignoresArmor: HasCardArmorBypass(owner, target, card.Kind), programSkillCardUseFrameId: f.Id);
        ActiveCardAttack = attack;
        CaptureProgramAdjustedSlashBaseDamage(attack);
        CaptureProgramAlcoholConsumption(useId, owner);
        owner.HasAlcoholEffect = false;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(card.Id, card.Kind, owner.Seat, r.TargetSeat, IgnoresArmor: attack.IgnoresArmor));
        NotifyAiOfSlash(owner, target);
        if (!TryMarkProgramUseCommitted(useId) || !TryBeginProgramCardWindow(attack, LifecycleCardUse(useId)!.Action!,
                SkillProgramTriggerWindow.CardUseCommitted, [r.TargetSeat], ProgramCardContinuation.CommittedSlash))
            BeginSlashTargetResolution(attack);
    }

    private void FinishShiXbOperation(ProgramSkillFrame f)
    {
        var r = f.ShiXiahouBa!;
        if (r.Operation == "poxi")
            ReplaceRuntimeTop(GetActiveProgramFrame(f.Id) with
            { ShiXiahouBa = r with { Stage = ShiXiahouBaComposition.PoxiStageFinished } });
        FinishProgramSkill(GetActiveProgramFrame(f.Id), true);
    }

    // ---- native use return ----------------------------------------------------

    [NativeUseValidation(68000)]
    private void ValidateShiXbNativeUse(CardUseFrame use)
    {
        if (use.ShiXiahouBaPoxi is { } n && (use.Step != ResolutionFrameStep.Completed ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == n.FrameId) is null ||
            LifecycleCardUse(use.Id)?.ShiXiahouBaPoxi?.UseId != n.UseId))
            throw new InvalidOperationException("Poxi cannot retire a different or incomplete original Slash Use.");
    }

    [NativeUseReturn(68000)]
    private bool ReturnShiXbNativeUse(CardUseFrame use)
    {
        if (use.ShiXiahouBaPoxi is not { } n) return false;
        var f = GetActiveProgramFrame(n.FrameId);
        if (LifecycleCardUse(use.Id) is not null || f.ShiXiahouBa is not { UseId: var useId } || useId != use.Id ||
            ProgramEventHistory<ShiXbPoxiNativeReturnedEvent>().Any(e => e.UseId == use.Id))
            throw new InvalidOperationException("Poxi requires one exact whole native use return.");
        AdvanceEventRulesAndQueueFact(new ShiXbPoxiNativeReturnedEvent(n.FrameId, use.Id));
        ReplaceRuntimeFrame(f.Id, f with { ShiXiahouBa = f.ShiXiahouBa! with { Stage = ShiXiahouBaComposition.PoxiStageReturnedUse } });
        if (use.CardAttack is not { Active: true }) ContinueShiXb(f.Id);
        return true;
    }

    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 68000)]
    private bool ResumeShiXbAttack()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame
            { ShiXiahouBa: { Operation: "poxi", Stage: ShiXiahouBaComposition.PoxiStageReturnedUse } } f) return false;
        ContinueShiXb(f.Id);
        return true;
    }

    // ---- runtime hooks --------------------------------------------------------

    [ProgramContinuation(ProgramContinuationKind.Resume, 68000)]
    private bool ResumeShiXb(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiXiahouBa: not null } f || f.Id != id) return false;
        ContinueShiXb(id);
        return true;
    }

    [ProgramContinuation(ProgramContinuationKind.Movement, 68000)]
    private bool ReturnShiXbMovement(ProgramSkillFrame f) => f.ShiXiahouBa is not null && ResumeShiXb(f.Id);

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 68000)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 68000)]
    private bool ContinuePaidShiXb(ProgramSkillFrame f) => f.ShiXiahouBa is { Operation: "poxi", Stage: not ShiXiahouBaComposition.PoxiStageFinished };

    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 68000)]
    private int ShiXbLibbingDamageBonus(IDamageAttempt attack) =>
        attack is CardAttackHandle card && card.CardUserSeat is { } user && user >= 0 &&
        attack.EffectiveCardKind is { } kind && ShiXiahouBaComposition.IsDamageCardKind(kind)
            ? ShiXbLibbingState(user).Count3 : 0;

    [ProgramCardUsePayment(ProgramCardUsePaymentStage.MatchBatch, 68000)]
    private bool ShiXbNativeMaterialBatch(CardUseFrame use, CardMovementBatchContext batch) => use.ShiXiahouBaPoxi is { } n &&
        _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == n.FrameId) is { ShiXiahouBa: { UseId: var useId } } &&
        useId == use.Id && batch.ParentFrameId == use.Id &&
        batch.Movements.Any(m => m.CardId == n.CardId && m.From == CardLocation.DiscardPile &&
            m.To == CardLocation.Processing && m.Reason.Value == ShiXiahouBaComposition.PoxiUseMoveReason);

    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashCountExemption, 68000)]
    private bool ShiXbExemptDebit(long useId, int actor) => LifecycleCardUse(useId) is { ShiXiahouBaPoxi: { } n } &&
        n.Source.OwnerSeat == actor &&
        _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == n.FrameId && f.ShiXiahouBa is { UseId: var owned } && owned == useId);

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 68000)]
    private void AssertShiXb(ProgramSkillFrame f)
    {
        if (f.ShiXiahouBa is not { } r) return;
        if (r.ActualTurn <= 0 || r.Source?.OwnerSeat != f.OwnerSeat ||
            r.Operation is not ("libbing" or "poxi"))
            throw new InvalidOperationException("Shi Xiahou Ba lost its original typed operation receipt.");
        if (r.Operation == "libbing" && (f.SkillId != ShiXbLibbing || f.TriggerId != ShiXiahouBaComposition.LibbingTrigger ||
                f.WindowContext?.CardUse is null || r.ActionId != f.WindowContext.CardUse.CardActionId))
            throw new InvalidOperationException("Libbing lost its completed-use window receipt.");
        if (r.Operation == "poxi")
        {
            if (f.SkillId != ShiXbPoxi || f.TriggerId != ShiXiahouBaComposition.PoxiTrigger ||
                f.WindowContext is not { Window: SkillProgramTriggerWindow.DiscardPileReceived, MovementBatch: { } batch, MovementIndex: { } index } ||
                batch.Id != r.BatchId || index < 0 || index >= batch.Movements.Count ||
                batch.Movements[index].Sequence != r.DiscardMovementSequence || batch.Movements[index].CardId != r.CardId)
                throw new InvalidOperationException("Poxi lost its exact original discarded-slash movement.");
            if (r.Stage >= ShiXiahouBaComposition.PoxiStageNativeUse &&
                !ProgramEventHistory<ShiXbPoxiNativeIssuedEvent>().Any(e => e.FrameId == f.Id &&
                    e.Source == ShiXbSource(f) && e.UseId == r.UseId && e.ActionId == r.ActionId && e.CardId == r.CardId))
                throw new InvalidOperationException("Poxi lost its issued native-use receipt.");
            if (r.Stage >= ShiXiahouBaComposition.PoxiStageReturnedUse &&
                !ProgramEventHistory<ShiXbPoxiNativeReturnedEvent>().Any(e => e.FrameId == f.Id && e.UseId == r.UseId))
                throw new InvalidOperationException("Poxi lost its whole native-use return receipt.");
        }
    }

    [ProgramAiChoice(SkillProgramEffectOp.ResolveShiXbPoxi)]
    private PromptChoice ChooseShiXbAi(PendingDecision decision, ProgramSkillFrame f)
    {
        var owner = _players[f.OwnerSeat];
        return decision.Choices.OrderBy(choice => AreProgramDistributionAllies(owner, _players[choice.Targets.Single()]))
            .ThenBy(choice => GetHand(_players[choice.Targets.Single()]).Count).First();
    }
}
