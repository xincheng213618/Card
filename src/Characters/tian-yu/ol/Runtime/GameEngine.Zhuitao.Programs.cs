namespace CardGame.Core;

public sealed partial class GameEngine
{

    // 追讨 mark: the accepted optional preparation trigger offers every other
    // living character whose distance the owner does not currently reduce this
    // way; the choice commits the directed distance grant.
    private SkillProgramStepOutcome ZhuitaoProgramMarkTarget(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var candidates = _players
            .Where(player => player.IsAlive && player.Seat != active.OwnerSeat &&
                ActiveDirectedDistanceGrants(active.OwnerSeat, player.Seat).Count == 0)
            .Select(player => player.Seat).Order().ToArray();
        if (candidates.Length == 0)
            return SkillProgramStepOutcome.Continue;
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = candidates.Select(seat => new PromptChoice(
                new ChoiceId($"zhuitao-mark.frame-{active.Id}.seat-{seat}"),
                $"令你与 {_players[seat].Name} 的距离-1。",
                [], [seat],
                new Dictionary<string, string>
                {
                    ["program-action"] = "zhuitao-mark",
                    ["frame-id"] = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["target-seat"] = seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }))
            .ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择一名未以此法减少距离的其他角色，你与你的目标距离-1。",
            [], candidates, active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择目标", presentation.Description),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    [ProgramAnswer("zhuitao-mark")]
    private void ResolveZhuitaoMarkChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Zhuitao choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.ZhuitaoMarkTarget } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Zhuitao choice does not match its suspended instruction.");
        if (!int.TryParse(selected.Parameters.GetValueOrDefault("target-seat"),
                System.Globalization.CultureInfo.InvariantCulture, out var targetSeat) ||
            !IsValidPlayerSeat(targetSeat) || targetSeat == frame.OwnerSeat ||
            !_players[targetSeat].IsAlive ||
            ActiveDirectedDistanceGrants(frame.OwnerSeat, targetSeat).Count > 0)
            throw new InvalidOperationException("The Zhuitao target is no longer legal.");
        var active = GetActiveProgramFrame(frame.Id);
        ClearPendingDecision();
        AdvanceEventRulesAndQueueFact(new ProgramDirectedDistanceGrantedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.SkillInstanceId, active.OwnerSeat, targetSeat, -1));
        AddLog("SkillTriggered",
            $"{_players[active.OwnerSeat].Name} 发动【{_contentRegistry.GetSkill(active.SkillId).Name}】，" +
            $"令自己与 {_players[targetSeat].Name} 的距离-1。",
            active.OwnerSeat, targetSeat);
        AdvanceRuntimeProgram(active.Id);
    }

    // 追讨 revoke: the locked damage sweep drops every active reduction the
    // owner holds against the damaged character; damages to anyone else and
    // owner-less damage leave the ledger untouched.
    private SkillProgramStepOutcome ZhuitaoProgramRevoke(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            active.WindowContext?.TargetSeat is not { } damagedSeat ||
            damagedSeat == active.OwnerSeat)
            return SkillProgramStepOutcome.Continue;
        var grants = ActiveDirectedDistanceGrants(active.OwnerSeat, damagedSeat);
        if (grants.Count == 0)
            return SkillProgramStepOutcome.Continue;
        AdvanceEventRulesAndQueueFact(new ProgramDirectedDistanceRevokedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.SkillInstanceId, active.OwnerSeat, damagedSeat));
        AddLog("SkillEffect",
            $"{owner.Name} 对 {_players[damagedSeat].Name} 造成伤害，失去以此法对其减少的距离。",
            active.OwnerSeat, damagedSeat);
        return SkillProgramStepOutcome.Continue;
    }

    // 追讨 mark: price every candidate through the shared target scoring; the
    // hint charges one point of prospective harm so a hostile target wins.
    [ProgramAiChoice(SkillProgramEffectOp.ZhuitaoMarkTarget)]
    private PromptChoice SelectAiZhuitaoMarkChoice(PendingDecision decision)
    {
        var view = CreateSnapshot(decision.PlayerSeat);
        var brain = _aiBrains[decision.PlayerSeat];
        return decision.Choices
            .Select(choice =>
            {
                var targetSeat = int.Parse(choice.Parameters.GetValueOrDefault("target-seat", "-1"),
                    System.Globalization.CultureInfo.InvariantCulture);
                var hint = new SkillProgramAiHint(0, 0, 0, 0, 0, 1, false, false);
                return (Choice: choice, Score: brain.ScoreProgramTarget(view, targetSeat, hint));
            })
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Choice.Id.Value, StringComparer.Ordinal)
            .First().Choice;
    }
}
