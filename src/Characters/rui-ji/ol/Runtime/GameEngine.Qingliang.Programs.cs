namespace CardGame.Core;

public sealed partial class GameEngine
{

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

    [ProgramAnswer("qingliang-option")]
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

    [ProgramAnswer("qingliang-suit")]
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
            MoveLogicalDiscardCards(active.OwnerSeat, active.OwnerSeat, discarded, CardLocation.Hand(active.OwnerSeat),
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
}
