namespace CardGame.Core;

// 巧力 evidence: every committed option, draw, distribution and ending gain is a
// scalar fact; cold recovery rebuilds the turn counts from these facts alone.
public sealed record ProgramQiaoliArmorCommittedEvent(long FrameId, string SkillId, string ActivationId,
    int OwnerSeat, int TurnNumber) : IGameEvent;
public sealed record ProgramQiaoliWeaponDrawEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int Range, int DrawnCount, int TurnNumber) : IGameEvent;
public sealed record ProgramQiaoliDistributionEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int CardId, int? RecipientSeat) : IGameEvent;
public sealed record ProgramQiaoliEndingGainEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int CardId, int TurnNumber) : IGameEvent;
public sealed record ProgramQingliangRevealEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int ActorSeat, int CardCount) : IGameEvent;
public sealed record ProgramQingliangResolutionEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int ActorSeat, string Choice, int DiscardCount, bool Nullified) : IGameEvent;

// One prompt per drawn card; the distribution rides the owning trigger frame.
public sealed record RuiJiDistributionState(int OwnerSeat, IReadOnlyList<int> CardIds, int Cursor);
// 清靓 suspends between the option prompt and the suit prompt on the same frame.
public sealed record RuiJiQingliangState(long ParentUseFrameId, int ActorSeat, string Stage);

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public RuiJiDistributionState? RuiJiDistribution { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public RuiJiQingliangState? RuiJiQingliang { get; init; }
}

public sealed partial class GameEngine
{
    internal const string QiaoliWeaponBinding = "qiaoli-weapon";
    internal const string QiaoliArmorBinding = "qiaoli-armor";
    private const string QingliangRevealBinding = "qingliang-reveal";

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

    private static int QiaoliWeaponRange(IReadOnlyList<Card> physicalCards)
    {
        foreach (var card in physicalCards)
        {
            if (!EquipmentCatalog.IsEquipment(card.Kind)) continue;
            var definition = EquipmentCatalog.Get(card.Kind);
            if (definition.Slot == EquipmentSlot.Weapon) return definition.WeaponAttackRange ?? 1;
        }
        return 1;
    }

    // 巧力 first-option payoff: only the skill-committed Duel damaging its
    // designated target without chain propagation counts.
    private SkillProgramStepOutcome QiaoliProgramWeaponDamageDraw(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.DamageAppliedBeforeDying } window ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not DamageTriggerWindowFrame damageWindow ||
            damageWindow.Id != window.ParentFrameId || window.ParentFrameId != window.DamageFrameId ||
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
            MoveCard(card, CardLocation.Hand(state.OwnerSeat), CardLocation.Hand(recipient),
                new($"skill-program.{active.SkillId}.{QiaoliWeaponBinding}-give"));
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

    // 清靓: the exact single designated target is re-verified engine-side, the
    // whole hand is published, and the owner picks the two-way resolution.
    private SkillProgramStepOutcome QingliangProgramChooseOption(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized,
                CardUse: { } use } ||
            _winner != Winner.None || !owner.IsAlive)
            return SkillProgramStepOutcome.Continue;
        var parent = LifecycleCardUse(use.ParentCardUseFrameId);
        if (parent?.Action?.ActionId != use.CardActionId ||
            parent.Action is not { } action ||
            parent.TargetSeats is not [var designated] || designated != active.OwnerSeat ||
            action.ActorSeat == active.OwnerSeat || !_players[action.ActorSeat].IsAlive)
            return SkillProgramStepOutcome.Continue;
        var hand = GetHand(owner).OrderBy(card => card.Id).ToArray();
        SetProgramCardSet(active.Id, QingliangRevealBinding, hand.Select(card => card.Id).ToArray(),
            SkillProgramCardSetVisibility.Public);
        AdvanceEventRulesAndQueueFact(new ProgramQingliangRevealEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, action.ActorSeat, hand.Length));
        AddLog("SkillEffect", $"{owner.Name} 清靓：展示所有手牌（{hand.Length} 张）。", owner.Seat);
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with
        {
            RuiJiQingliang = new RuiJiQingliangState(parent.Id, action.ActorSeat, "option")
        });
        PresentQingliangOptionPrompt(GetActiveProgramFrame(active.Id));
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PresentQingliangOptionPrompt(ProgramSkillFrame frame)
    {
        var state = frame.RuiJiQingliang ?? throw new InvalidOperationException("The Qingliang defense lost its state.");
        var active = GetActiveProgramFrame(frame.Id);
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var actor = _players[state.ActorSeat];
        var choices = new[]
        {
            new PromptChoice(
                new ChoiceId($"qingliang-option.frame-{frame.Id}.draw"),
                $"你与 {actor.Name} 各摸一张牌。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "qingliang-option",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["choice"] = "draw"
                }),
            new PromptChoice(
                new ChoiceId($"qingliang-option.frame-{frame.Id}.discard"),
                "弃置一种花色的所有手牌并取消此目标。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "qingliang-option",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["choice"] = "discard"
                })
        };
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择清靓的结算方式。",
            [], [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择一项", "你与其各摸一张牌，或弃置一种花色的所有手牌并取消此目标。"),
            Choices = choices.AsReadOnly()
        };
        _status = _players[active.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveQingliangOptionChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Qingliang option choice lost its program frame.");
        var state = frame.RuiJiQingliang ?? throw new InvalidOperationException("The Qingliang defense lost its state.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Qingliang chooser changed while suspended.");
        ClearPendingDecision();
        var choice = selected.Parameters.GetValueOrDefault("choice");
        if (choice == "draw")
        {
            var owner = _players[active.OwnerSeat];
            DrawCards(owner, 1, true, new($"skill-program.{active.SkillId}.qingliang-draw"));
            DrawCards(_players[state.ActorSeat], 1, true, new($"skill-program.{active.SkillId}.qingliang-draw"));
            AddLog("SkillEffect", $"{owner.Name} 清靓：与 {_players[state.ActorSeat].Name} 各摸一张牌。",
                active.OwnerSeat, state.ActorSeat);
            AdvanceEventRulesAndQueueFact(new ProgramQingliangResolutionEvent(active.Id, active.SkillId,
                GetProgramBindingId(active), active.OwnerSeat, state.ActorSeat, "draw", 0, false));
            ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with { RuiJiQingliang = null });
            if (!TryBeginCardsMovedProgramWindow())
                AdvanceRuntimeProgram(active.Id);
            return;
        }
        if (choice == "discard")
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with
            {
                RuiJiQingliang = state with { Stage = "suit" }
            });
            PresentQingliangSuitPrompt(GetActiveProgramFrame(active.Id));
            return;
        }
        throw new InvalidOperationException($"The Qingliang choice '{choice}' is not supported.");
    }

    private void PresentQingliangSuitPrompt(ProgramSkillFrame frame)
    {
        var state = frame.RuiJiQingliang ?? throw new InvalidOperationException("The Qingliang defense lost its state.");
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var suits = GetHand(owner).Select(card => card.Suit).Distinct().Order().ToArray();
        var suitNames = new Dictionary<Suit, string>
        {
            [Suit.Spade] = "黑桃", [Suit.Heart] = "红桃", [Suit.Club] = "梅花", [Suit.Diamond] = "方块"
        };
        var choices = suits.Select(suit => new PromptChoice(
            new ChoiceId($"qingliang-suit.frame-{frame.Id}.{suit}"),
            $"弃置所有{suitNames.GetValueOrDefault(suit, suit.ToString())}手牌并取消此目标。",
            [], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "qingliang-suit",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["suit"] = suit.ToString()
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择要弃置的花色。",
            GetHand(owner).Select(card => card.Id).ToArray(), [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择花色", "弃置一种花色的所有手牌，取消此目标。"),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveQingliangSuitChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Qingliang suit choice lost its program frame.");
        var state = frame.RuiJiQingliang ?? throw new InvalidOperationException("The Qingliang defense lost its state.");
        if (!Enum.TryParse(selected.Parameters.GetValueOrDefault("suit"), out Suit suit))
            throw new InvalidOperationException("The Qingliang suit choice is invalid.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Qingliang chooser changed while suspended.");
        ClearPendingDecision();
        var owner = _players[active.OwnerSeat];
        var parent = LifecycleCardUse(state.ParentUseFrameId);
        if (_winner != Winner.None || !owner.IsAlive || parent is null)
        {
            CancelProgramBindingAndCleanup(active, "清靓的目标用牌已失效，剩余结算取消。");
            return;
        }
        var discarded = GetHand(owner).Where(card => card.Suit == suit).ToArray();
        if (discarded.Length > 0)
            MoveCards(discarded, CardLocation.Hand(active.OwnerSeat), CardLocation.DiscardPile,
                new($"skill-program.{active.SkillId}.qingliang-discard"));
        MarkCardEffectIneffective(parent.Id, active.OwnerSeat);
        AdvanceEventRulesAndQueueFact(new CardEffectSkippedEvent(parent.Id, state.ActorSeat, active.OwnerSeat,
            parent.CardKind, CardEffectSkipReason.SkillNullified));
        AddLog("SkillEffect",
            $"{owner.Name} 清靓：弃置 {discarded.Length} 张手牌，取消【{parent.CardKind}】对此目标的结算。",
            active.OwnerSeat, state.ActorSeat);
        AdvanceEventRulesAndQueueFact(new ProgramQingliangResolutionEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, state.ActorSeat, "discard", discarded.Length, true));
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with { RuiJiQingliang = null });
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }

    // The bespoke duel launch is an activation instruction; when its committed
    // use finishes, the program frame advances past the launch instruction.
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

    private PromptChoice SelectAiQiaoliDistributionChoice(PendingDecision decision) =>
        decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();

    private PromptChoice SelectAiQingliangChoice(PendingDecision decision)
    {
        if (decision.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "qingliang-suit"))
            return decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();
        return decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("choice") == "draw") ??
            decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();
    }

    private sealed partial class ProgramSkillHost : IRuiJiProgramHost
    {
        public SkillProgramStepOutcome QiaoliWeaponDuel(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.QiaoliProgramDuelLaunch(frame, unrespondable: false);
        public SkillProgramStepOutcome QiaoliArmorDuel(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.QiaoliProgramDuelLaunch(frame, unrespondable: true);
        public SkillProgramStepOutcome QiaoliWeaponDamageDraw(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.QiaoliProgramWeaponDamageDraw(frame, effect);
        public SkillProgramStepOutcome QiaoliEndingEquipmentGain(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.QiaoliProgramEndingEquipmentGain(frame, effect);
        public SkillProgramStepOutcome QingliangChooseOption(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.QingliangProgramChooseOption(frame, effect);
    }
}
