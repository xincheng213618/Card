namespace CardGame.Core;

// 丰积 evidence: one scalar fact per resolved option of the owner's round
// ledger; the redistribution target is public once committed.
public enum FengjiOptionKind { Draw = 1, Slash = 2 }
public sealed record ProgramFengjiOptionChosenEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int RoundNumber, FengjiOptionKind Option, bool Accepted, int RecipientSeat) : IGameEvent;

// 旋回 evidence: the round effects swap is public and the skill disables until
// a character dies; the disable state derives from this fact versus deaths.
public sealed record ProgramXuanhuiEffectsSwappedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int RoundNumber, int SwappedOptionCount) : IGameEvent;

public sealed partial class GameEngine
{
    // The 丰积 round ledger feeds public rule queries; it exists only when a
    // registered program actually carries the bespoke round choice operation.
    internal bool TracksFengjiRoundLedger =>
        _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.FengjiRoundChoice) == true;

    private IReadOnlyList<ProgramFengjiOptionChosenEvent> CurrentRoundFengjiOptions(int ownerSeat)
    {
        if (_roundNumber == 0) return [];
        return [.. CompleteProgramEventHistory().OfType<ProgramFengjiOptionChosenEvent>()
            .Where(e => e.RoundNumber == _roundNumber && e.OwnerSeat == ownerSeat)];
    }

    // The choice opportunity belongs to exactly the actual turn whose begin
    // advanced the round ledger; extra turns never re-open it.
    internal bool IsFengjiRoundChoicePending(int ownerSeat)
    {
        if (_winner != Winner.None || _roundNumber == 0 || ownerSeat != _currentSeat ||
            _turnProgression.Kind == ActualTurnKind.Extra)
            return false;
        var latestRound = CompleteProgramEventHistory().OfType<RoundStartedEvent>().LastOrDefault();
        if (latestRound is not { } round || round.RoundNumber != _roundNumber || round.ActorSeat != _currentSeat)
            return false;
        return !CompleteProgramEventHistory().OfType<ProgramFengjiOptionChosenEvent>()
            .Any(e => e.RoundNumber == _roundNumber && e.OwnerSeat == ownerSeat);
    }

    // Round-scoped 丰积 adjustments for one seat and one public numeric query.
    // Every owner's option facts participate: accepted options swap the
    // owner↔recipient attribution per the option owner's 旋回 swap parity;
    // a declined option's +1 stays with its owner (it has no counterpart).
    internal int GetFengjiRoundAdjustment(int seat, SkillRuleQuery query)
    {
        if (_roundNumber == 0) return 0;
        var facts = CompleteProgramEventHistory();
        var options = facts.OfType<ProgramFengjiOptionChosenEvent>()
            .Where(e => e.RoundNumber == _roundNumber).ToArray();
        if (options.Length == 0) return 0;
        var swapsByOwner = facts.OfType<ProgramXuanhuiEffectsSwappedEvent>()
            .Where(e => e.RoundNumber == _roundNumber)
            .GroupBy(e => e.OwnerSeat)
            .ToDictionary(group => group.Key, group => group.Count());
        var total = 0;
        foreach (var option in options)
        {
            if (query == SkillRuleQuery.DrawCount && option.Option != FengjiOptionKind.Draw ||
                query == SkillRuleQuery.SlashLimit && option.Option != FengjiOptionKind.Slash)
                continue;
            var swapCount = swapsByOwner.GetValueOrDefault(option.OwnerSeat);
            if (option.Accepted)
            {
                if (option.OwnerSeat == seat)
                    total += swapCount % 2 == 0 ? -1 : 2;
                else if (option.RecipientSeat == seat)
                    total += swapCount % 2 == 0 ? 2 : -1;
            }
            else if (option.OwnerSeat == seat)
            {
                total += 1;
            }
        }
        return total;
    }

    // 旋回 disables from its own swap until any character dies afterwards;
    // multiple owners disable only their own instance.
    private bool IsXuanhuiDisabledByOwnSwap(int ownerSeat)
    {
        var facts = CompleteProgramEventHistory().ToArray();
        var swapIndex = Array.FindLastIndex(facts, item =>
            item is ProgramXuanhuiEffectsSwappedEvent swapped && swapped.OwnerSeat == ownerSeat);
        if (swapIndex < 0) return false;
        var deathIndex = Array.FindLastIndex(facts, item => item is PlayerDiedEvent);
        return deathIndex < swapIndex;
    }

    internal bool IsFengjiSwapAvailable(int ownerSeat)
    {
        if (_winner != Winner.None || IsXuanhuiDisabledByOwnSwap(ownerSeat)) return false;
        return CurrentRoundFengjiOptions(ownerSeat).Any(option => option.Accepted &&
            option.RecipientSeat != ownerSeat && _players[option.RecipientSeat].IsAlive);
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
        var chosen = CurrentRoundFengjiOptions(active.OwnerSeat);
        var pendingOption = chosen.Any(e => e.Option == FengjiOptionKind.Draw)
            ? chosen.Any(e => e.Option == FengjiOptionKind.Slash) ? (FengjiOptionKind?)null : FengjiOptionKind.Slash
            : FengjiOptionKind.Draw;
        if (pendingOption is not { } option)
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
        if (CurrentRoundFengjiOptions(active.OwnerSeat).Any(e => e.Option == option))
            throw new InvalidOperationException("The Fengji option was already resolved this round.");
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
        if (CurrentRoundFengjiOptions(active.OwnerSeat).Any(e => e.Option == option))
            throw new InvalidOperationException("The Fengji option was already resolved this round.");
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

    // 旋回: the owner and every current 丰积 beneficiary exchange the matching
    // accepted-option effects for the rest of the round; the skill then
    // disables until a character dies.
    private SkillProgramStepOutcome XuanhuiProgramSwapEffects(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive || _winner != Winner.None || _roundNumber == 0 ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            IsXuanhuiDisabledByOwnSwap(active.OwnerSeat))
            return SkillProgramStepOutcome.Continue;
        var swapped = CurrentRoundFengjiOptions(active.OwnerSeat).Where(option => option.Accepted &&
            option.RecipientSeat != active.OwnerSeat).ToArray();
        if (swapped.Length == 0) return SkillProgramStepOutcome.Continue;
        AdvanceEventRulesAndQueueFact(new ProgramXuanhuiEffectsSwappedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, _roundNumber, swapped.Length));
        AddLog("SkillEffect",
            $"{owner.Name} 旋回：与 {_players[swapped[0].RecipientSeat].Name} 等 {swapped.Length} 项丰积效果交换，【旋回】失效直到一名角色死亡。",
            active.OwnerSeat);
        AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.Continue;
    }

    private PromptChoice SelectAiFengjiChoice(PendingDecision decision) =>
        decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("accept") == "false") ??
        decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();

    private sealed partial class ProgramSkillHost : IChenDengProgramHost
    {
        public SkillProgramStepOutcome FengjiRoundChoice(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.FengjiProgramRoundChoice(frame, effect);
        public SkillProgramStepOutcome XuanhuiSwapEffects(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.XuanhuiProgramSwapEffects(frame, effect);
    }
}
