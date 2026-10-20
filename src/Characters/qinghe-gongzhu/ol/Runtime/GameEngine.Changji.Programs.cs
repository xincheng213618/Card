namespace CardGame.Core;

public sealed partial class GameEngine
{
    // 长姬: at any character's ending phase, if the owner dealt damage this turn
    // she may have that character draw two cards, and if she took damage this
    // turn she may have that character discard two cards.
    private SkillProgramStepOutcome ChangjiProgramEndingDamageChoice(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding, TargetSeat: { } endingSeat } ||
            !_players[endingSeat].IsAlive)
            return SkillProgramStepOutcome.Continue;
        var damages = EventsSinceLastBoundary(item => item is TurnStartedEvent).OfType<DamageAppliedEvent>()
            .Where(item => item.Amount > 0).ToArray();
        var dealt = damages.Any(item => !item.SourceLess && item.SourceSeat == owner.Seat);
        var taken = damages.Any(item => item.TargetSeat == owner.Seat);
        if (!dealt && !taken) return SkillProgramStepOutcome.Continue;
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = new List<PromptChoice>();
        if (dealt)
            choices.Add(new PromptChoice(
                new ChoiceId($"changji-ending.frame-{frame.Id}.draw"),
                $"令 {_players[endingSeat].Name} 摸两张牌。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "changji-ending-choice",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["branch"] = "draw"
                }));
        if (taken)
            choices.Add(new PromptChoice(
                new ChoiceId($"changji-ending.frame-{frame.Id}.discard"),
                $"令 {_players[endingSeat].Name} 弃置两张牌。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "changji-ending-choice",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["branch"] = "discard"
                }));
        choices.Add(new PromptChoice(
            new ChoiceId($"changji-ending.frame-{frame.Id}.skip"), "不发动【长姬】。", [], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "changji-ending-choice",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["branch"] = "skip"
            }));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择是否发动。",
            [], [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = endingSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 结束阶段",
                dealt && taken ? "你本回合造成过伤害，也受到过伤害，请选择一项。" :
                dealt ? "你本回合造成过伤害。" : "你本回合受到过伤害。"),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    [ProgramAnswer("changji-ending-choice")]
    private void ResolveChangjiEndingChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Changji ending choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.ChangjiEndingDamageChoice } ||
            selected.Parameters.GetValueOrDefault("program-action") != "changji-ending-choice" ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Changji ending choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Changji ending chooser changed while suspended.");
        ClearPendingDecision();
        var endingSeat = active.WindowContext!.TargetSeat!.Value;
        var branch = selected.Parameters.GetValueOrDefault("branch");
        if (branch == "draw")
        {
            DrawCards(_players[endingSeat], 2, true, new($"skill-program.{active.SkillId}.changji-draw"));
            AddLog("SkillEffect",
                $"{_players[active.OwnerSeat].Name} 发动【长姬】：令 {_players[endingSeat].Name} 摸两张牌。",
                active.OwnerSeat, endingSeat);
            AdvanceEventRulesAndQueueFact(new ProgramChangjiEndingEvent(active.Id, active.SkillId,
                GetProgramBindingId(active), active.OwnerSeat, endingSeat, "draw", [], 2));
            if (!TryBeginCardsMovedProgramWindow())
                AdvanceRuntimeProgram(active.Id);
            return;
        }
        if (branch == "discard")
        {
            var hand = GetHand(_players[endingSeat]);
            if (hand.Count == 0)
            {
                AddLog("SkillEffect", $"{_players[endingSeat].Name} 没有手牌，长姬弃牌落空。", active.OwnerSeat, endingSeat);
                AdvanceRuntimeProgram(active.Id);
                return;
            }
            ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with
            {
                ChangjiDiscard = new ChangjiDiscardState(endingSeat, active.OwnerSeat,
                    Math.Min(2, hand.Count), [])
            });
            PresentChangjiDiscardPrompt(GetActiveProgramFrame(active.Id));
            return;
        }
        AdvanceRuntimeProgram(active.Id);
    }

    private void PresentChangjiDiscardPrompt(ProgramSkillFrame frame)
    {
        var state = frame.ChangjiDiscard ?? throw new InvalidOperationException("The Changji discard lost its state.");
        var active = GetActiveProgramFrame(frame.Id);
        var hand = GetHand(_players[state.EndingSeat]).OrderBy(card => card.Id).ToArray();
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = hand.Select(card => new PromptChoice(
            new ChoiceId($"changji-discard.frame-{frame.Id}.card-{card.Id}"),
            $"弃置【{card.DisplayName}】。",
            [card.Id], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "changji-discard",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["card-id"] = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, state.EndingSeat,
            $"【{presentation.Name}】请弃置 {state.Remaining} 张手牌。",
            hand.Select(card => card.Id).ToArray(), [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            TargetSeat = state.EndingSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 弃牌",
                $"{_players[state.OwnerSeat].Name} 本回合受到过伤害，你弃置 {state.Remaining} 张手牌。"),
            Choices = choices.AsReadOnly()
        };
        _status = _players[state.EndingSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    [ProgramAnswer("changji-discard")]
    private void ResolveChangjiDiscardChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Changji discard choice lost its program frame.");
        var state = frame.ChangjiDiscard ?? throw new InvalidOperationException("The Changji discard lost its state.");
        if (!int.TryParse(selected.Parameters.GetValueOrDefault("card-id"),
                System.Globalization.CultureInfo.InvariantCulture, out var cardId) ||
            _cardZones.GetLocation(cardId) != CardLocation.Hand(state.EndingSeat))
            throw new InvalidOperationException("The Changji discard card is no longer in hand.");
        ClearPendingDecision();
        var active = GetActiveProgramFrame(frame.Id);
        active = AppendFrameNativeLogicalDiscard(active, state.EndingSeat, [cardId],
            new($"skill-program.{active.SkillId}.changji-discard"));
        MoveCards([_cardZones.CardsAt(CardLocation.Hand(state.EndingSeat)).Single(card => card.Id == cardId)],
            CardLocation.Hand(state.EndingSeat), CardLocation.DiscardPile,
            new($"skill-program.{active.SkillId}.changji-discard"));
        var discarded = state.DiscardedCardIds.Append(cardId).ToArray();
        if (state.Remaining > 1)
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
            {
                ChangjiDiscard = state with
                {
                    Remaining = state.Remaining - 1,
                    DiscardedCardIds = Array.AsReadOnly(discarded)
                }
            });
            PresentChangjiDiscardPrompt(GetActiveProgramFrame(frame.Id));
            return;
        }
        active = CompleteFrameNativeLogicalDiscard(GetActiveProgramFrame(frame.Id));
        ReplaceRuntimeTop(active with { ChangjiDiscard = null });
        AddLog("SkillEffect",
            $"{_players[active.OwnerSeat].Name} 发动【长姬】：{_players[state.EndingSeat].Name} 弃置 {discarded.Length} 张手牌。",
            active.OwnerSeat, state.EndingSeat);
        AdvanceEventRulesAndQueueFact(new ProgramChangjiEndingEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, state.EndingSeat, "discard",
            Array.AsReadOnly(discarded), 0));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }
}
