namespace CardGame.Core;



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
        MoveCards(hand, CardLocation.Hand(active.OwnerSeat), CardLocation.Hand(boundarySeat), new($"skill-program.{active.SkillId}.yuanzi-give"), NativeGiveCompletion(active.Id,active.OwnerSeat,boundarySeat,hand.Select(c=>c.Id).ToArray(),_movementSequence,null));
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
}
