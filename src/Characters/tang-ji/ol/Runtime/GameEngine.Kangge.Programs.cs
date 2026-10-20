namespace CardGame.Core;

public sealed partial class GameEngine
{
    // 抗歌: the 歌 marker is attributed to the owner, so two sources can never
    // confuse their targets and the public marker rides snapshots/checkpoints.
    private bool IsKanggeMarkedSeat(int seat, int ownerSeat) =>
        IsValidPlayerSeat(seat) &&
        _players[seat].MarkerSourceCounts.GetValueOrDefault((PlayerMarkerKind.Kangge, ownerSeat)) > 0;

    private int? GetKanggeMarkedSeat(int ownerSeat) => _players
        .Where(player => player.MarkerSourceCounts.GetValueOrDefault((PlayerMarkerKind.Kangge, ownerSeat)) > 0)
        .Select(player => (int?)player.Seat).FirstOrDefault();

    // 抗歌 choice: the first turn start publicly marks one other living
    // character for the rest of the game; the 歌 marker is the public record.
    private SkillProgramStepOutcome KanggeProgramChooseTarget(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            GetKanggeMarkedSeat(owner.Seat) is not null)
            return SkillProgramStepOutcome.Continue;
        var candidates = _players.Where(player => player.IsAlive && player.Seat != owner.Seat)
            .Select(player => player.Seat).ToArray();
        if (candidates.Length == 0) return SkillProgramStepOutcome.Continue;
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = candidates.Select(seat => new PromptChoice(
            new ChoiceId($"kangge-choose.frame-{frame.Id}.seat-{seat}"),
            $"选择 {_players[seat].Name}。",
            [], [seat],
            new Dictionary<string, string>
            {
                ["program-action"] = "kangge-choose",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["chosen"] = seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择一名其他角色作为“抗歌”对象。",
            [], candidates, active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择“抗歌”对象",
                "本局中：其于其回合外获得手牌时你摸等量的牌（每回合至多三张）；每轮限一次，其进入濒死状态时你可以令其回复体力至1点；其死亡时你弃置所有牌并失去1点体力。"),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    [ProgramAnswer("kangge-choose")]
    private void ResolveKanggeChooseChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Kangge choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.KanggeChooseTarget } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Kangge choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("chosen"),
                System.Globalization.CultureInfo.InvariantCulture, out var chosen) ||
            !IsValidPlayerSeat(chosen) || chosen == active.OwnerSeat || !_players[chosen].IsAlive)
            throw new InvalidOperationException("The Kangge choice lost its chooser or candidate.");
        ClearPendingDecision();
        SetAttributedNatureMarker(active.OwnerSeat, chosen, PlayerMarkerKind.Kangge, 1, active.Id);
        AddLog("SkillEffect",
            $"{_players[active.OwnerSeat].Name} 的【{_contentRegistry.GetSkill(active.SkillId).Name}】：选择 {_players[chosen].Name} 作为“抗歌”对象。",
            active.OwnerSeat, chosen);
        AdvanceEventRulesAndQueueFact(new ProgramKanggeChosenEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, chosen));
        AdvanceRuntimeProgram(active.Id);
    }

    // 抗歌 gain: one candidate per observed hand-card gain of the marked
    // character outside their turn; the per-turn cap derives from committed
    // evidence, so replays reproduce it exactly.
    private SkillProgramStepOutcome KanggeProgramGainDraw(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.CardsGained, TargetSeat: { } gainer } ||
            !IsKanggeMarkedSeat(gainer, owner.Seat) || gainer == _currentSeat)
            return SkillProgramStepOutcome.Continue;
        var drawnThisTurn = CompleteProgramEventHistory().OfType<ProgramKanggeGainedDrawEvent>()
            .Where(item => item.OwnerSeat == owner.Seat && item.TurnNumber == _turnNumber)
            .Sum(item => item.DrawnCount);
        var drawn = Math.Min(1, Math.Max(0, ProgramKanggeTurnDrawCap - drawnThisTurn));
        if (drawn == 0) return SkillProgramStepOutcome.Continue;
        DrawProgramCards(active.Id, owner.Seat, drawn, null, null,
            SkillProgramCardSetVisibility.Private,
            new($"skill-program.{active.SkillId}.kangge-gain-draw"));
        AddLog("SkillEffect",
            $"{owner.Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】:{_players[gainer].Name} 于回合外获得手牌，{owner.Name} 摸 {drawn} 张牌。",
            active.OwnerSeat, gainer);
        AdvanceEventRulesAndQueueFact(new ProgramKanggeGainedDrawEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), owner.Seat, gainer, _turnNumber, drawn));
        return SkillProgramStepOutcome.Continue;
    }

    private bool KanggeHealUsedThisRound(int ownerSeat) =>
        CompleteProgramEventHistory().OfType<ProgramKanggeHealedEvent>()
            .Any(item => item.OwnerSeat == ownerSeat && item.RoundNumber == _roundNumber);

    // 抗歌 rescue: the trigger fires for every dying character; the operation
    // filters to the marked victim and presents the voluntary recovery itself.
    private SkillProgramStepOutcome KanggeProgramHealVictim(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.DyingEntering, TargetSeat: { } victim } ||
            victim == owner.Seat || !IsKanggeMarkedSeat(victim, owner.Seat) ||
            _players[victim].Hp > 0 || KanggeHealUsedThisRound(owner.Seat))
            return SkillProgramStepOutcome.Continue;
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = new[]
        {
            new PromptChoice(
                new ChoiceId($"kangge-heal.frame-{frame.Id}.heal"),
                $"令 {_players[victim].Name} 回复体力至1点。",
                [], [victim],
                new Dictionary<string, string>
                {
                    ["program-action"] = "kangge-heal",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }),
            new PromptChoice(
                new ChoiceId($"kangge-heal.frame-{frame.Id}.decline"),
                "不发动【抗歌】。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "kangge-heal",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["decline"] = "true"
                })
        };
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】是否令 {_players[victim].Name} 回复体力至1点？（每轮限一次）",
            [], [victim], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 濒死回复", presentation.Description),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    [ProgramAnswer("kangge-heal")]
    private void ResolveKanggeHealChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Kangge heal choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.KanggeHealVictim } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Kangge heal choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Kangge heal choice lost its chooser.");
        ClearPendingDecision();
        if (selected.Parameters.GetValueOrDefault("decline") == "true")
        {
            AddLog("SkillTriggered",
                $"{owner.Name} 未发动【{_contentRegistry.GetSkill(active.SkillId).Name}】的濒死回复。",
                active.OwnerSeat);
            AdvanceRuntimeProgram(active.Id);
            return;
        }
        if (frame.WindowContext is not { TargetSeat: { } victim } ||
            !IsKanggeMarkedSeat(victim, owner.Seat) || owner.Seat == victim ||
            KanggeHealUsedThisRound(owner.Seat) || _players[victim].Hp > 0)
        {
            // The victim left the dying window between prompt and answer; there
            // is nothing left to rescue, so the trigger settles without effect.
            AdvanceRuntimeProgram(active.Id);
            return;
        }
        RecoverOtherDyingVictim(GetActiveProgramFrame(frame.Id), 1);
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame resumed || resumed.Id != frame.Id) return;
        AddLog("SkillEffect",
            $"{owner.Name} 的【{_contentRegistry.GetSkill(active.SkillId).Name}】：令 {_players[victim].Name} 回复体力至1点。",
            active.OwnerSeat, victim);
        AdvanceEventRulesAndQueueFact(new ProgramKanggeHealedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), owner.Seat, victim, _roundNumber));
        AdvanceRuntimeProgram(active.Id);
    }

    // 抗歌 price: when the marked character dies, the owner discards every card
    // in their hand, equipment and judgment zones and loses 1 hp.
    private SkillProgramStepOutcome KanggeProgramDeathPrice(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.CharacterDied, TargetSeat: { } victim } ||
            !IsKanggeMarkedSeat(victim, owner.Seat))
            return SkillProgramStepOutcome.Continue;
        var hand = GetHand(owner).ToArray();
        var equipment = GetEquipment(owner).ToArray();
        var judgment = _cardZones.CardsAt(CardLocation.Judgment(owner.Seat)).ToArray();
        var reason = new CardMoveReason($"skill-program.{active.SkillId}.kangge-death-price");
        var logicalDiscard = CaptureNativeLogicalDiscardInvocation(active.OwnerSeat, owner.Seat,
            hand.Concat(equipment).Concat(judgment).Select(card => card.Id).ToArray(), reason, active.Id);
        foreach (var card in hand) MoveCard(card, CardLocation.Hand(owner.Seat), CardLocation.DiscardPile, reason);
        foreach (var card in equipment) MoveCard(card, _cardZones.GetLocation(card.Id), CardLocation.DiscardPile, reason);
        foreach (var card in judgment) MoveCard(card, CardLocation.Judgment(owner.Seat), CardLocation.DiscardPile, reason);
        CompleteNativeLogicalDiscardInvocation(logicalDiscard);
        var discarded = hand.Length + equipment.Length + judgment.Length;
        AddLog("SkillEffect",
            $"{owner.Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】:{_players[victim].Name} 死亡，{owner.Name} 弃置所有牌（{discarded} 张）并失去1点体力。",
            active.OwnerSeat, victim);
        var before = owner.Hp;
        owner.Hp = Math.Max(0, before - 1);
        RecordHpChange(active.Id, null, owner.Seat, before, owner.Hp, HpChangeKind.Loss);
        AdvanceEventRulesAndQueueFact(new ProgramSkillHpLostEvent(active.Id, active.SkillId, owner.Seat,
            Math.Min(before, 1), owner.Hp));
        AdvanceEventRulesAndQueueFact(new ProgramKanggeDeathPriceEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), owner.Seat, victim, discarded));
        if (owner.Hp == 0)
        {
            BeginProgramSkillDying(active.Id, owner);
            return SkillProgramStepOutcome.AwaitChild;
        }
        return SkillProgramStepOutcome.Continue;
    }

    [ProgramAiChoice(SkillProgramEffectOp.KanggeChooseTarget)]
    private PromptChoice SelectAiKanggeChooseChoice(PendingDecision decision) =>
        decision.Choices
            .OrderByDescending(choice => choice.Targets.Select(seat => _players[seat].Hp).FirstOrDefault())
            .ThenBy(choice => choice.Targets.FirstOrDefault())
            .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal)
            .First();
}
