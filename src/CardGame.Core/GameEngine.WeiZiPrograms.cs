namespace CardGame.Core;

// 援资 evidence: one scalar record per accepted invocation. The recipient and
// the turn number are public; the same-turn damage payoff reads this committed
// fact, so cold recovery rebuilds the benefit window from history alone.
public sealed record ProgramYuanziInvokedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int RecipientSeat, int TurnNumber) : IGameEvent;

// 援资 payoff evidence: the drawn card ids are public; one record per payoff.
public sealed record ProgramYuanziDamageDrawEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int RecipientSeat, int TurnNumber, IReadOnlyList<int> DrawnCardIds) : IGameEvent;

// 烈节 evidence: the source-side random discard is public once it moves; the
// red count behind the quota is rebuilt from the committed first-clause discards.
public sealed record ProgramLiejieSourceDiscardEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int SourceSeat, int DiscardCount, IReadOnlyList<int> DiscardedCardIds) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public YuanziDrawPendingState? YuanziDrawPending { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public LiejieDiscardPendingState? LiejieDiscardPending { get; init; }
}

// The suspension receipt of one 援资 payoff prompt: the armed recipient the
// draw prompt belongs to.
public sealed record YuanziDrawPendingState(int RecipientSeat);

// The suspension receipt of one 烈节 second-clause prompt: the damage source
// and the public quota clamp (red count versus available hand).
public sealed record LiejieDiscardPendingState(int SourceSeat, int MaximumCount);

public sealed partial class GameEngine
{
    // 援资 arm: the accepted invocation moves the whole hand to the turn owner
    // and queues the committed evidence the payoff trigger reads.
    private SkillProgramStepOutcome YuanziProgramResolve(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,
                SourceSeat: { } boundarySeat } ||
            boundarySeat == active.OwnerSeat || boundarySeat != _currentSeat ||
            !owner.IsAlive || !_players[boundarySeat].IsAlive)
            return SkillProgramStepOutcome.Continue;
        var hand = GetHand(owner).OrderBy(card => card.Id).ToArray();
        if (hand.Length == 0)
            return SkillProgramStepOutcome.Continue;
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        MoveCards(hand, CardLocation.Hand(active.OwnerSeat), CardLocation.Hand(boundarySeat),
            new($"skill-program.{active.SkillId}.yuanzi-give"));
        AddLog("SkillTriggered",
            $"{owner.Name} 发动【{presentation.Name}】，将所有手牌（{hand.Length}张）交给 {_players[boundarySeat].Name}。",
            active.OwnerSeat, boundarySeat);
        AdvanceEventRulesAndQueueFact(new ProgramYuanziInvokedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, boundarySeat, _turnNumber));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    // 援资 payoff: only the armed recipient dealing damage inside the armed turn
    // with at least the owner's hand count opens the optional draw prompt.
    private SkillProgramStepOutcome YuanziProgramDamageDraw(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied,
                SourceSeat: { } sourceSeat, Amount: > 0 } ||
            sourceSeat == active.OwnerSeat || sourceSeat != _currentSeat ||
            !IsValidPlayerSeat(sourceSeat) || !_players[sourceSeat].IsAlive)
            return SkillProgramStepOutcome.Continue;
        var invocation = CompleteProgramEventHistory().OfType<ProgramYuanziInvokedEvent>().LastOrDefault(fact =>
            fact.OwnerSeat == active.OwnerSeat && fact.RecipientSeat == sourceSeat && fact.TurnNumber == _turnNumber);
        if (invocation is null || GetHand(_players[sourceSeat]).Count < GetHand(owner).Count)
            return SkillProgramStepOutcome.Continue;
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with
        {
            YuanziDrawPending = new YuanziDrawPendingState(sourceSeat)
        });
        var frameId = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】你可以摸两张牌。",
            [], [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = sourceSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 摸牌",
                $"{_players[sourceSeat].Name} 本回合造成伤害后手牌数不小于你，你可以摸两张牌。"),
            Choices = new List<PromptChoice>
            {
                new(new ChoiceId($"yuanzi-draw.frame-{active.Id}"),
                    "摸两张牌。",
                    [], [],
                    new Dictionary<string, string>
                    {
                        ["program-action"] = "yuanzi-draw",
                        ["frame-id"] = frameId
                    }),
                new(new ChoiceId($"yuanzi-decline.frame-{active.Id}"),
                    $"不发动【{presentation.Name}】。",
                    [], [],
                    new Dictionary<string, string>
                    {
                        ["program-action"] = "yuanzi-decline",
                        ["frame-id"] = frameId
                    })
            }.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    // 烈节 second clause: the red count of the already committed first-clause
    // discards is the public quota; the prompt lets the owner discard up to that
    // many random hand cards from the living damage source.
    private SkillProgramStepOutcome LiejieProgramSourceDiscard(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied,
                SourceSeat: { } sourceSeat } ||
            sourceSeat == active.OwnerSeat || !IsValidPlayerSeat(sourceSeat) ||
            !_players[sourceSeat].IsAlive)
            return SkillProgramStepOutcome.Continue;
        if (effect.SourceBind is null ||
            active.CardSetBindings.SingleOrDefault(set => set.Name == effect.SourceBind) is not { } binding)
            throw new InvalidOperationException("The Liejie source discard lost its bound card set.");
        var redCount = binding.CardIds.Select(ResolveProgramCommittedCard)
            .Count(card => card.Suit is Suit.Heart or Suit.Diamond);
        var handCount = GetHand(_players[sourceSeat]).Count;
        if (redCount <= 0 || handCount == 0)
            return SkillProgramStepOutcome.Continue;
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var maximum = Math.Min(redCount, handCount);
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with
        {
            LiejieDiscardPending = new LiejieDiscardPendingState(sourceSeat, maximum)
        });
        var choices = new List<PromptChoice>();
        var frameId = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (var count = 1; count <= maximum; count++)
        {
            choices.Add(new PromptChoice(
                new ChoiceId($"liejie-count.frame-{active.Id}.{count}"),
                $"弃置 {_players[sourceSeat].Name} {count} 张手牌（随机）。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "liejie-count",
                    ["frame-id"] = frameId,
                    ["count"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
        }
        choices.Add(new PromptChoice(
            new ChoiceId($"liejie-decline.frame-{active.Id}"),
            $"不发动【{presentation.Name}】。",
            [], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "liejie-decline",
                ["frame-id"] = frameId
            }));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】你可以弃置 {_players[sourceSeat].Name} 至多{maximum}张牌（X为你以此法弃置的红色牌数）。",
            [], [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = sourceSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 弃置伤害来源",
                $"你以此法弃置了 {redCount} 张红色牌，你可以弃置 {_players[sourceSeat].Name} 至多{maximum}张手牌。"),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private Card ResolveProgramCommittedCard(int cardId)
    {
        var location = _cardZones.GetLocation(cardId);
        return _cardZones.CardsAt(location).Single(card => card.Id == cardId);
    }

    // Prompt router: every Wei Zi prompt rides one op, so the shared program
    // dispatch hands each accepted choice back through this single entry.
    private void ResolveWeiZiProgramChoice(PromptChoice selected)
    {
        switch (selected.Parameters.GetValueOrDefault("program-action"))
        {
            case "yuanzi-draw" or "yuanzi-decline": ResolveYuanziDrawChoice(selected); return;
            case "liejie-count" or "liejie-decline": ResolveLiejieCountChoice(selected); return;
            default: throw new InvalidOperationException("The Wei Zi choice lost its action parameter.");
        }
    }

    private void ResolveYuanziDrawChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Yuanzi draw choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.YuanziDamageDraw } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Yuanzi draw choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Yuanzi draw chooser changed while suspended.");
        ClearPendingDecision();
        var pending = active.YuanziDrawPending ??
            throw new InvalidOperationException("The Yuanzi draw choice lost its pending state.");
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with { YuanziDrawPending = null });
        if (selected.Parameters.GetValueOrDefault("program-action") != "yuanzi-draw")
        {
            AdvanceRuntimeProgram(active.Id);
            return;
        }
        var owner = _players[active.OwnerSeat];
        var drawn = DrawCards(owner, 2, true, new($"skill-program.{active.SkillId}.yuanzi-draw"));
        AddLog("SkillEffect",
            $"{owner.Name} 的【{_contentRegistry.GetSkill(active.SkillId).Name}】生效：摸两张牌。",
            active.OwnerSeat, pending.RecipientSeat);
        AdvanceEventRulesAndQueueFact(new ProgramYuanziDamageDrawEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, pending.RecipientSeat, _turnNumber,
            Array.AsReadOnly(drawn.ToArray())));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }

    private void ResolveLiejieCountChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Liejie count choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.LiejieSourceDiscard } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Liejie count choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Liejie discard chooser changed while suspended.");
        ClearPendingDecision();
        var pending = active.LiejieDiscardPending ??
            throw new InvalidOperationException("The Liejie count choice lost its pending state.");
        if (!int.TryParse(selected.Parameters.GetValueOrDefault("count", "0"),
                System.Globalization.CultureInfo.InvariantCulture, out var count) ||
            count < 0 || count > pending.MaximumCount)
            throw new InvalidOperationException("The Liejie discard count is no longer available.");
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with { LiejieDiscardPending = null });
        if (count == 0)
        {
            AdvanceRuntimeProgram(active.Id);
            return;
        }
        var source = _players[pending.SourceSeat];
        var pool = GetHand(source).ToList();
        var picked = new List<Card>();
        for (var index = 0; index < count; index++)
        {
            var card = pool[_random.Next(pool.Count)];
            pool.Remove(card);
            picked.Add(card);
        }
        MoveCards(picked, CardLocation.Hand(pending.SourceSeat), CardLocation.DiscardPile,
            new($"skill-program.{active.SkillId}.liejie-discard"));
        AddLog("SkillEffect",
            $"{_players[active.OwnerSeat].Name} 的【{_contentRegistry.GetSkill(active.SkillId).Name}】生效：{_players[pending.SourceSeat].Name} 随机弃置 {count} 张手牌。",
            active.OwnerSeat, pending.SourceSeat);
        AdvanceEventRulesAndQueueFact(new ProgramLiejieSourceDiscardEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, pending.SourceSeat, count,
            Array.AsReadOnly(picked.Select(card => card.Id).ToArray())));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }

    // 援资 payoff prompt: the optional draw is always worth taking.
    private PromptChoice SelectAiYuanziDamageDrawChoice(PendingDecision decision) =>
        decision.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "yuanzi-draw");

    // 烈节 source discard: stripping the damage source is pure control value,
    // so the AI discards as many cards as the red quota allows.
    private PromptChoice SelectAiLiejieSourceDiscardChoice(PendingDecision decision) =>
        decision.Choices
            .Select(choice => (Choice: choice,
                Count: int.TryParse(choice.Parameters.GetValueOrDefault("count", "-1"),
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : -1))
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Choice.Id.Value, StringComparer.Ordinal)
            .First().Choice;

    private sealed partial class ProgramSkillHost : IWeiZiProgramHost
    {
        public SkillProgramStepOutcome YuanziResolve(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.YuanziProgramResolve(frame, effect);
        public SkillProgramStepOutcome YuanziDamageDraw(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.YuanziProgramDamageDraw(frame, effect);
        public SkillProgramStepOutcome LiejieSourceDiscard(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.LiejieProgramSourceDiscard(frame, effect);
    }
}
