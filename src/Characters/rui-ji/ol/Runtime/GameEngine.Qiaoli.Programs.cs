namespace CardGame.Core;

public sealed partial class GameEngine
{

    // 巧力 launch shared by both options: the selected weapon/equipment card is
    // committed as a real Duel use with the skill conversion source, and the
    // unrespondable variant attaches the shared issued policy once the counter
    // window is open, so 无懈可击 can still respond but the target cannot.
    private SkillProgramStepOutcome QiaoliProgramDuelLaunch(ProgramSkillFrame frame, bool unrespondable)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            active.SelectedCardIds is not [var cardId] || active.SelectedTargetSeats is not [var target] ||
            !IsValidPlayerSeat(target) || target == active.OwnerSeat || !_players[target].IsAlive ||
            _cardZones.GetLocation(cardId) is not { OwnerSeat: { } holder } location ||
            holder != active.OwnerSeat || location.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))
            return SkillProgramStepOutcome.Continue;
        var binding = unrespondable ? QiaoliArmorBinding : QiaoliWeaponBinding;
        var source = new CardConversionSource(active.SkillId, binding, active.OwnerSeat, active.SkillInstanceId);
        var card = _cardZones.CardsAt(location).Single(item => item.Id == cardId);
        var resolutionId = BeginCardUse(card, active.OwnerSeat, [target], CardKind.Duel, conversionSource: source);
        MoveCard(card, location, CardLocation.Processing, new($"skill-program.{active.SkillId}.{binding}"));
        if (unrespondable)
        {
            AdvanceEventRulesAndQueueFact(new ProgramQiaoliArmorCommittedEvent(active.Id, active.SkillId,
                active.ActivationId, active.OwnerSeat, _turnNumber));
        }
        BeginJizhiOrNullificationWindow(resolutionId, card, active.OwnerSeat, [target],
            LegalActionKind.Duel, playedCardKind: CardKind.Duel);
        if (unrespondable)
            AttachQiaoliNoResponse(resolutionId, source);
        AddLog("SkillTriggered",
            $"{owner.Name} 发动【{_contentRegistry!.GetSkill(active.SkillId).Name}】，将【{card.DisplayName}】当【决斗】对 {_players[target].Name} 使用{(unrespondable ? "（不能被响应）" : string.Empty)}。",
            active.OwnerSeat, target);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private void AttachQiaoliNoResponse(long resolutionId, CardConversionSource source)
    {
        var use = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(item => item.Id == resolutionId) ??
            throw new InvalidOperationException("The Qiaoli duel lost its committed use frame.");
        var action = use.Action ??
            throw new InvalidOperationException("The Qiaoli duel lost its committed action.");
        var issued = new IssuedCardNoResponse(action.ActionId, resolutionId, source, _turnNumber, _cardUseDebitPhaseInstanceId);
        ReplaceRuntimeFrame(use.Id, use with { IssuedNoResponse = issued });
        AdvanceEventRulesAndQueueFact(new IssuedCardNoResponseEvent(issued));
    }

    private static int QiaoliWeaponRange(IReadOnlyList<Card> physicalCards) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_rui_ji_ol_Qiaoli_Programs.QiaoliWeaponRange(physicalCards);

    // 巧力 first-option payoff: only the skill-committed Duel damaging its
    // designated target without chain propagation counts.
    private SkillProgramStepOutcome QiaoliProgramWeaponDamageDraw(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied,
                ParentFrameId: { } parentFrameId, DamageFrameId: { } damageFrameId } ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not DamageTriggerWindowFrame damageWindow ||
            damageWindow.Id != parentFrameId || damageWindow.ParentFrameId != damageFrameId ||
            _winner != Winner.None || !owner.IsAlive)
            return SkillProgramStepOutcome.Continue;
        var attempt = GetDamageTriggerAttack(damageWindow);
        if (!attempt.DamageWasApplied || attempt.DamageAmount <= 0 || attempt.IsChainPropagation ||
            attempt.SourceSeat != active.OwnerSeat ||
            attempt.ConversionSource is not { } source ||
            source.SkillId != active.SkillId || source.BindingId != QiaoliWeaponBinding ||
            source.OwnerSeat != active.OwnerSeat)
            return SkillProgramStepOutcome.Continue;
        var range = QiaoliWeaponRange(attempt.PhysicalCards);
        var drawn = DrawCards(owner, range, true, new($"skill-program.{active.SkillId}.{QiaoliWeaponBinding}-draw"));
        AdvanceEventRulesAndQueueFact(new ProgramQiaoliWeaponDrawEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, range, drawn.Count, _turnNumber));
        AddLog("SkillEffect", $"{owner.Name} 巧力：决斗对目标造成伤害，摸 {drawn.Count} 张牌（攻击范围 {range}）。", owner.Seat);
        var recipients = _players.Where(player => player.IsAlive && player.Seat != active.OwnerSeat)
            .Select(player => player.Seat).ToArray();
        if (drawn.Count == 0 || recipients.Length == 0)
        {
            if (!TryBeginCardsMovedProgramWindow())
                AdvanceRuntimeProgram(active.Id);
            return SkillProgramStepOutcome.AwaitChild;
        }
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with
        {
            RuiJiDistribution = new RuiJiDistributionState(active.OwnerSeat,
                Array.AsReadOnly(drawn.ToArray()), 0)
        });
        PresentQiaoliDistributionPrompt(GetActiveProgramFrame(active.Id));
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PresentQiaoliDistributionPrompt(ProgramSkillFrame frame)
    {
        var state = frame.RuiJiDistribution ?? throw new InvalidOperationException("The Qiaoli distribution lost its state.");
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[state.OwnerSeat];
        var cardId = state.CardIds[state.Cursor];
        if (_cardZones.GetLocation(cardId) != CardLocation.Hand(state.OwnerSeat))
            throw new InvalidOperationException("The Qiaoli distributed card left the owner's hand.");
        var card = _cardZones.CardsAt(CardLocation.Hand(state.OwnerSeat)).Single(item => item.Id == cardId);
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var recipients = _players.Where(player => player.IsAlive && player.Seat != state.OwnerSeat)
            .Select(player => player.Seat).ToArray();
        var choices = new List<PromptChoice>
        {
            new(new ChoiceId($"ruiji-distribution.frame-{frame.Id}.card-{cardId}.keep"), $"保留【{card.DisplayName}】。",
                [cardId], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "ruiji-distribution",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["card-id"] = cardId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["recipient"] = string.Empty
                })
        };
        choices.AddRange(recipients.Select(seat => new PromptChoice(
            new ChoiceId($"ruiji-distribution.frame-{frame.Id}.card-{cardId}.seat-{seat}"),
            $"将【{card.DisplayName}】交给 {_players[seat].Name}。",
            [cardId], [seat],
            new Dictionary<string, string>
            {
                ["program-action"] = "ruiji-distribution",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["card-id"] = cardId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["recipient"] = seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, state.OwnerSeat,
            $"【{presentation.Name}】请选择摸到的牌的去向（还可分配 {state.CardIds.Count - state.Cursor} 张）。",
            [cardId], recipients, active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = state.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 分配", "可以将摸到的任意张牌交给任意名其他角色。"),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    [ProgramAnswer("ruiji-distribution")]
    private void ResolveQiaoliDistributionChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Qiaoli distribution choice lost its program frame.");
        var state = frame.RuiJiDistribution ?? throw new InvalidOperationException("The Qiaoli distribution lost its state.");
        if (!int.TryParse(selected.Parameters.GetValueOrDefault("card-id"),
                System.Globalization.CultureInfo.InvariantCulture, out var cardId) ||
            !state.CardIds.Contains(cardId))
            throw new InvalidOperationException("The Qiaoli distribution choice does not match its card.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != state.OwnerSeat)
            throw new InvalidOperationException("The Qiaoli distribution chooser changed while suspended.");
        ClearPendingDecision();
        var owner = _players[state.OwnerSeat];
        var recipientParameter = selected.Parameters.GetValueOrDefault("recipient");
        var hasRecipient = int.TryParse(recipientParameter,
            System.Globalization.CultureInfo.InvariantCulture, out var recipient);
        if (_winner != Winner.None || !owner.IsAlive ||
            _cardZones.GetLocation(cardId) != CardLocation.Hand(state.OwnerSeat) ||
            hasRecipient && (!IsValidPlayerSeat(recipient) || recipient == state.OwnerSeat || !_players[recipient].IsAlive))
        {
            CancelProgramBindingAndCleanup(active, "巧力的分配目标或牌已失效，剩余分配取消。");
            return;
        }
        if (hasRecipient)
        {
            var card = _cardZones.CardsAt(CardLocation.Hand(state.OwnerSeat)).Single(item => item.Id == cardId);
            MoveCard(card, CardLocation.Hand(state.OwnerSeat), CardLocation.Hand(recipient), new($"skill-program.{active.SkillId}.{QiaoliWeaponBinding}-give"), NativeGiveCompletion(active.Id,state.OwnerSeat,recipient,[card.Id],_movementSequence,null));
            AddLog("SkillEffect", $"{owner.Name} 巧力：将【{card.DisplayName}】交给 {_players[recipient].Name}。",
                state.OwnerSeat, recipient);
        }
        AdvanceEventRulesAndQueueFact(new ProgramQiaoliDistributionEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), state.OwnerSeat, cardId, hasRecipient ? recipient : null));
        var next = state.Cursor + 1;
        if (next < state.CardIds.Count)
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
            {
                RuiJiDistribution = state with { Cursor = next }
            });
            PresentQiaoliDistributionPrompt(GetActiveProgramFrame(frame.Id));
            return;
        }
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { RuiJiDistribution = null });
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }

    // True while at least one option-2 activation of this turn is still owed its
    // end-phase gain; used both as the trigger eligibility gate and the count.
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition, 50, SkillProgramEffectOp.QiaoliEndingEquipmentGain)]
    private bool HasQiaoliEndingGainPending(int ownerSeat) =>
        QiaoliEndingGainCount(ownerSeat) > 0;

    private int QiaoliEndingGainCount(int ownerSeat) =>
        CompleteProgramEventHistory().OfType<ProgramQiaoliArmorCommittedEvent>()
            .Count(item => item.OwnerSeat == ownerSeat && item.TurnNumber == _turnNumber);

    private SkillProgramStepOutcome QiaoliProgramEndingEquipmentGain(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } ||
            _winner != Winner.None || !owner.IsAlive)
            return SkillProgramStepOutcome.Continue;
        var gains = QiaoliEndingGainCount(owner.Seat);
        if (gains <= 0) return SkillProgramStepOutcome.Continue;
        var gainedAny = false;
        for (var index = 0; index < gains; index++)
        {
            var candidates = _cardZones.CardsAt(CardLocation.DrawPile)
                .Where(card => EquipmentCatalog.IsEquipment(card.Kind)).OrderBy(card => card.Id).ToArray();
            if (candidates.Length == 0)
            {
                AddLog("SkillEffect", $"{owner.Name} 巧力：牌堆中没有装备牌，随机获得落空。", owner.Seat);
                break;
            }
            var card = candidates[_random.Next(candidates.Length)];
            MoveCard(card, CardLocation.DrawPile, CardLocation.Hand(owner.Seat),
                new($"skill-program.{active.SkillId}.{QiaoliArmorBinding}-gain"));
            AdvanceEventRulesAndQueueFact(new ProgramQiaoliEndingGainEvent(active.Id, active.SkillId,
                GetProgramBindingId(active), owner.Seat, card.Id, _turnNumber));
            AddLog("SkillEffect", $"{owner.Name} 巧力：随机获得【{card.DisplayName}】。", owner.Seat);
            gainedAny = true;
        }
        if (!gainedAny || !TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    // The bespoke duel launch is an activation instruction; when its committed
    // use finishes, the program frame advances past the launch instruction.
    [NativeUseReturn(170)]
    private void ContinueProgramAfterRuiJiDuelUse()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.TriggerId is not null)
            return;
        var program = _contentRegistry.Skills.GetValueOrDefault(frame.SkillId)?.Program;
        var activation = program is null ? null : ProgramInstructionResolver.Default.FindActivation(program, frame.ActivationId);
        if (activation is null || frame.InstructionIndex == 0 ||
            activation.Effects[frame.InstructionIndex - 1].Op is not
                (SkillProgramEffectOp.QiaoliWeaponDuel or SkillProgramEffectOp.QiaoliArmorDuel))
            return;
        AdvanceRuntimeProgram(frame.Id);
    }
}
