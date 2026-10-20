namespace CardGame.Core;

public sealed partial class GameEngine
{

    private bool TracksRoundTrickUses =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ZecaiRoundSettlement);

    // 择才 settlement: at the owner's turn start after a round boundary, the
    // finished round is settled once; the limited activation offers one other
    // character the 集智 grant until this round ends.
    private SkillProgramStepOutcome ZecaiProgramRoundSettlement(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        var settledRound = _roundNumber - 1;
        if (!owner.IsAlive || settledRound < 1 ||
            CompleteProgramEventHistory().OfType<ProgramZecaiRoundSettledEvent>()
                .Any(fact => fact.OwnerSeat == active.OwnerSeat && fact.SettledRound == settledRound))
            return SkillProgramStepOutcome.Continue;
        PurgeExpiredRoundGrants();
        if (CompleteProgramEventHistory().OfType<ProgramZecaiSkillGrantedEvent>()
                .Any(fact => fact.OwnerSeat == active.OwnerSeat))
            return SkillProgramStepOutcome.Continue;
        var others = _players.Where(player => player.IsAlive && player.Seat != active.OwnerSeat)
            .OrderBy(player => player.Seat).ToArray();
        if (others.Length == 0) return SkillProgramStepOutcome.Continue;
        AdvanceEventRulesAndQueueFact(new ProgramZecaiRoundSettledEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, settledRound));
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var leader = UniqueRoundTrickLeaderSeat(settledRound);
        var choices = others.Select(player => new PromptChoice(
                new ChoiceId($"zecai.frame-{frame.Id}.seat-{player.Seat}"),
                $"令 {player.Name} 获得“集智”直到本轮游戏结束。",
                [], [player.Seat],
                new Dictionary<string, string>
                {
                    ["program-action"] = "zecai-target",
                    ["target-seat"] = player.Seat.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["unique-trick-leader"] =
                        (player.Seat == leader).ToString(System.Globalization.CultureInfo.InvariantCulture)
                }))
            .ToList<PromptChoice>();
        choices.Add(new PromptChoice(
            new ChoiceId($"zecai.frame-{frame.Id}.decline"),
            "不发动【择才】。",
            [], [],
            new Dictionary<string, string> { ["program-action"] = "zecai-decline" }));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】你可以令一名其他角色获得技能“集智”直到本轮游戏结束；若其是上一轮使用锦囊牌数唯一最多的角色，其执行一个额外的回合。",
            [], choices.Where(choice => choice.Targets.Count > 0).SelectMany(choice => choice.Targets).ToArray(),
            active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择获得“集智”的角色", presentation.Description),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private int UniqueRoundTrickLeaderSeat(int round)
    {
        var counts = CompleteProgramEventHistory().OfType<ProgramRoundTrickUsedEvent>()
            .Where(fact => fact.RoundNumber == round)
            .GroupBy(fact => fact.ActorSeat)
            .ToDictionary(group => group.Key, group => group.Count());
        if (counts.Count == 0) return -1;
        var maximum = counts.Values.Max();
        var leaders = counts.Where(pair => pair.Value == maximum).Select(pair => pair.Key).ToArray();
        return leaders.Length == 1 ? leaders[0] : -1;
    }

    [ProgramAnswer("zecai-target", "zecai-decline")]
    private void ResolveZecaiChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Zecai choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.ZecaiRoundSettlement })
            throw new InvalidOperationException("The Zecai choice does not match the suspended instruction.");
        ClearPendingDecision();
        var active = GetActiveProgramFrame(frame.Id);
        if (selected.Parameters.GetValueOrDefault("program-action") == "zecai-decline")
        {
            AdvanceRuntimeProgram(active.Id);
            return;
        }
        if (!int.TryParse(selected.Parameters.GetValueOrDefault("target-seat"),
                System.Globalization.CultureInfo.InvariantCulture, out var targetSeat) ||
            !_players[targetSeat].IsAlive || targetSeat == active.OwnerSeat)
            throw new InvalidOperationException("The Zecai beneficiary is no longer legal.");
        var round = _roundNumber;
        AcquireRuntimeSkills(_players[targetSeat], $"round:{round}:{active.SkillId}:{GetProgramBindingId(active)}",
            ["classic:jizhi"]);
        var extraTurn = UniqueRoundTrickLeaderSeat(round - 1) == targetSeat;
        if (extraTurn) PendProgramExtraTurn(active, targetSeat);
        AddLog("SkillTriggered",
            extraTurn
                ? $"{_players[targetSeat].Name} 获得“集智”直到本轮游戏结束，并执行一个额外的回合。"
                : $"{_players[targetSeat].Name} 获得“集智”直到本轮游戏结束。",
            active.OwnerSeat, targetSeat);
        AdvanceEventRulesAndQueueFact(new ProgramZecaiSkillGrantedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, targetSeat, round, "classic:jizhi", extraTurn));
        AdvanceRuntimeProgram(active.Id);
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 290)]
    private void ObserveRoundTrickUse(IGameEvent payload)
    {
        if (!TracksRoundTrickUses) return;
        CardActionContext? action = payload switch
        {
            CardUseDeclaredEvent declared => _resolutionStack.OfType<CardUseFrame>()
                .SingleOrDefault(frame => frame.Id == declared.ResolutionId)?.Action,
            CardActionAcceptedEvent accepted when accepted.Action.Type == CardActionType.Response &&
                accepted.Action.EffectiveKind == CardKind.Nullification &&
                accepted.Action.ActorSeat == accepted.Action.ProviderSeat &&
                accepted.Action.ResponderSeat == accepted.Action.ActorSeat &&
                accepted.Action.RequesterSeat is null &&
                _resolutionStack.OfType<NullificationWindowFrame>().Any(window =>
                    _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Id == window.ParentFrameId)
                        ?.Action?.ActionId == accepted.Action.ParentActionId) => accepted.Action,
            _ => null
        };
        if (action is null || GetProgramCardCategory(action.EffectiveKind) != SkillProgramCardCategory.Trick ||
            CompleteProgramEventHistory().OfType<ProgramRoundTrickUsedEvent>()
                .Any(fact => fact.ActionId == action.ActionId)) return;
        AdvanceEventRulesAndQueueFact(new ProgramRoundTrickUsedEvent(
            _roundNumber, action.ActorSeat, action.ActionId, action.EffectiveKind));
    }
}
