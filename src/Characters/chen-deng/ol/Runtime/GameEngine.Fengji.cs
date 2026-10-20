namespace CardGame.Core;

public sealed partial class GameEngine
{
    // The 丰积 round ledger feeds public rule queries; it exists only when a
    // registered program actually carries the bespoke round choice operation.
    internal bool TracksFengjiRoundLedger =>
        _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.FengjiRoundChoice) == true;

    private IReadOnlyList<ProgramFengjiOptionChosenEvent> CurrentRoundFengjiOptions(int ownerSeat)
    {
        if (_roundNumber == 0) return [];
        return [.. ProgramEventHistory<ProgramFengjiOptionChosenEvent>()
            .Where(e => e.RoundNumber == _roundNumber && e.OwnerSeat == ownerSeat)];
    }

    // The op prompts exactly one unresolved option in text order; stale
    // commands for other options are rejected against this derivation.
    private FengjiOptionKind? PendingFengjiOption(int ownerSeat)
    {
        var chosen = CurrentRoundFengjiOptions(ownerSeat);
        if (chosen.Any(e => e.Option == FengjiOptionKind.Draw))
            return chosen.Any(e => e.Option == FengjiOptionKind.Slash) ? null : FengjiOptionKind.Slash;
        return FengjiOptionKind.Draw;
    }

    // The choice opportunity belongs to exactly the actual turn whose begin
    // advanced the round ledger; extra turns never re-open it.
    internal bool IsFengjiRoundChoicePending(int ownerSeat)
    {
        if (_winner != Winner.None || _roundNumber == 0 || !IsValidPlayerSeat(ownerSeat) || !_players[ownerSeat].IsAlive ||
            _turnProgression.Kind == ActualTurnKind.Extra)
            return false;
        var latestRound = CompleteProgramEventHistory().OfType<RoundStartedEvent>().LastOrDefault();
        if (latestRound is not { } round || round.RoundNumber != _roundNumber || round.ActorSeat != _currentSeat)
            return false;
        return !CompleteProgramEventHistory().OfType<ProgramFengjiOptionChosenEvent>()
            .Any(e => e.RoundNumber == _roundNumber && e.OwnerSeat == ownerSeat);
    }

    // 丰积 launch: one prompt per option in text order; each acceptance asks for
    // a recipient in a follow-up prompt before the option fact commits.
    private SkillProgramStepOutcome FengjiProgramRoundChoice(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive || _winner != Winner.None || _roundNumber == 0 ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        if (PendingFengjiOption(active.OwnerSeat) is not { } option)
        {
            AdvanceRuntimeProgram(active.Id);
            return SkillProgramStepOutcome.Continue;
        }
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var label = option == FengjiOptionKind.Draw ? "摸牌阶段摸牌数" : "出牌阶段使用【杀】的次数上限";
        PromptChoice Answer(bool accept) => new(
            new ChoiceId($"fengji-option.frame-{frame.Id}.{(option == FengjiOptionKind.Draw ? "draw" : "slash")}.{(accept ? "yes" : "no")}"),
            accept ? $"是：你的{label}本轮 -1，并令一名其他角色该项 +2。" : $"否：你的{label}本轮 +1。",
            [], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "fengji-option",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["option"] = option == FengjiOptionKind.Draw ? "draw" : "slash",
                ["accept"] = accept ? "true" : "false"
            });
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择是否令你的{label}本轮 -1。",
            [], [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择",
                "选择“是”后需指定一名其他角色令其对应项数值 +2；选择“否”则你的对应项数值 +1。"),
            Choices = new[] { Answer(true), Answer(false) }.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    [ProgramAnswer("fengji-option")]
    private void ResolveFengjiOptionChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Fengji choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.FengjiRoundChoice } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("program-action") != "fengji-option")
            throw new InvalidOperationException("The Fengji choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Fengji chooser changed while suspended.");
        var accept = selected.Parameters.GetValueOrDefault("accept");
        if (accept is not ("true" or "false"))
            throw new InvalidOperationException("The Fengji choice answer is malformed.");
        var option = selected.Parameters.GetValueOrDefault("option") == "draw"
            ? FengjiOptionKind.Draw : FengjiOptionKind.Slash;
        if (option != PendingFengjiOption(active.OwnerSeat))
            throw new InvalidOperationException("The Fengji option does not match the pending prompt.");
        ClearPendingDecision();
        if (accept == "false")
        {
            CommitFengjiOption(active, option, accepted: false, recipientSeat: active.OwnerSeat);
            ResumeFengjiRoundChoice(active);
            return;
        }
        PresentFengjiRecipientPrompt(GetActiveProgramFrame(frame.Id), option);
    }

    private void PresentFengjiRecipientPrompt(ProgramSkillFrame frame, FengjiOptionKind option)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        var recipients = _players.Where(player => player.IsAlive && player.Seat != active.OwnerSeat)
            .Select(player => player.Seat).ToArray();
        if (recipients.Length == 0 || !owner.IsAlive || _winner != Winner.None)
        {
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id), "丰积没有可指定的其他角色，剩余结算取消。");
            return;
        }
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var label = option == FengjiOptionKind.Draw ? "摸牌阶段摸牌数" : "出牌阶段使用【杀】的次数上限";
        var choices = recipients.Select(seat => new PromptChoice(
            new ChoiceId($"fengji-recipient.frame-{frame.Id}.{(option == FengjiOptionKind.Draw ? "draw" : "slash")}.seat-{seat}"),
            $"令 {_players[seat].Name} 的{label}本轮 +2。",
            [], [seat],
            new Dictionary<string, string>
            {
                ["program-action"] = "fengji-recipient",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["option"] = option == FengjiOptionKind.Draw ? "draw" : "slash",
                ["recipient"] = seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择获得“{label} +2”的角色。",
            [], recipients, active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择角色", "该角色本轮的对应项数值 +2。"),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    [ProgramAnswer("fengji-recipient")]
    private void ResolveFengjiRecipientChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Fengji recipient lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.FengjiRoundChoice } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("program-action") != "fengji-recipient" ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("recipient"),
                System.Globalization.CultureInfo.InvariantCulture, out var recipient))
            throw new InvalidOperationException("The Fengji recipient does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Fengji chooser changed while suspended.");
        var option = selected.Parameters.GetValueOrDefault("option") == "draw"
            ? FengjiOptionKind.Draw : FengjiOptionKind.Slash;
        if (option != PendingFengjiOption(active.OwnerSeat))
            throw new InvalidOperationException("The Fengji recipient does not match the pending prompt.");
        ClearPendingDecision();
        if (recipient == active.OwnerSeat || !IsValidPlayerSeat(recipient) || !_players[recipient].IsAlive)
        {
            CancelProgramBindingAndCleanup(active, "丰积的目标已失效，剩余结算取消。");
            return;
        }
        CommitFengjiOption(active, option, accepted: true, recipientSeat: recipient);
        ResumeFengjiRoundChoice(active);
    }

    private void CommitFengjiOption(ProgramSkillFrame active, FengjiOptionKind option, bool accepted, int recipientSeat)
    {
        AdvanceEventRulesAndQueueFact(new ProgramFengjiOptionChosenEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, _roundNumber, option, accepted, recipientSeat));
        var label = option == FengjiOptionKind.Draw ? "摸牌阶段摸牌数" : "出牌阶段使用【杀】的次数上限";
        AddLog("SkillEffect", accepted
            ? $"{_players[active.OwnerSeat].Name} 丰积：{label}本轮 -1，{_players[recipientSeat].Name} 的该数值本轮 +2。"
            : $"{_players[active.OwnerSeat].Name} 丰积：{label}本轮 +1。",
            active.OwnerSeat, accepted ? recipientSeat : active.OwnerSeat);
    }

    private void ResumeFengjiRoundChoice(ProgramSkillFrame active)
    {
        var frame = GetActiveProgramFrame(active.Id);
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { } effect) return;
        FengjiProgramRoundChoice(frame, effect);
    }
}
