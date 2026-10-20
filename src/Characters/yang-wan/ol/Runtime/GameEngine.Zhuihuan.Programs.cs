namespace CardGame.Core;

public sealed partial class GameEngine
{

    // 追还 arm: the owner secretly picks any living character; the prompt and
    // its command stay private, and the public log names no seat.
    private SkillProgramStepOutcome ZhuihuanProgramArm(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var candidates = _players.Where(player => player.IsAlive).Select(player => player.Seat).ToArray();
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = candidates.Select(seat => new PromptChoice(
            new ChoiceId($"zhuihuan-arm.frame-{frame.Id}.seat-{seat}"),
            $"秘密选择 {_players[seat].Name}。",
            [], [seat],
            new Dictionary<string, string>
            {
                ["program-action"] = "zhuihuan-arm",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["chosen"] = seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请秘密选择一名角色。",
            [], candidates, active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 秘密选择",
                "直到该角色的下个准备阶段，此期间内对其造成过伤害的角色将在该准备阶段受到清算。"),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    [ProgramAnswer("zhuihuan-arm")]
    private void ResolveZhuihuanArmChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Zhuihuan arm choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.ZhuihuanArm } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Zhuihuan arm choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("chosen"),
                System.Globalization.CultureInfo.InvariantCulture, out var chosen) ||
            !IsValidPlayerSeat(chosen) || !_players[chosen].IsAlive)
            throw new InvalidOperationException("The Zhuihuan arm choice lost its chooser or candidate.");
        ClearPendingDecision();
        _zhuihuanArmChoices[active.Id] = chosen;
        AdvanceEventRulesAndQueueFact(new ProgramZhuihuanArmedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, _turnNumber));
        AddLog("SkillTriggered",
            $"{_players[active.OwnerSeat].Name} 发动【{_contentRegistry.GetSkill(active.SkillId).Name}】，秘密选择了一名角色。",
            active.OwnerSeat);
        AdvanceRuntimeProgram(active.Id);
    }

    // 追还 punishment: runs at the marked character's next preparation phase and
    // repeats one damager per invocation; reflect damage suspends as a child and
    // its typed continuation re-enters this instruction until the ledger empties.
    private SkillProgramStepOutcome ZhuihuanProgramRetaliate(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,
                SourceSeat: { } boundarySeat } || boundarySeat != _currentSeat)
            return SkillProgramStepOutcome.Continue;
        var chosenSeat = _currentSeat;
        var state = frame.ZhuihuanRetaliate;
        if (state is null || state.ChosenSeat != chosenSeat)
        {
            var damagers = CollectZhuihuanPendingDamagers(owner.Seat, chosenSeat);
            if (damagers.Count == 0) return SkillProgramStepOutcome.Continue;
            state = new ZhuihuanRetaliateState(chosenSeat, damagers);
        }
        var chosen = _players[state.ChosenSeat];
        while (state.PendingDamagers.Count > 0)
        {
            var damagerSeat = state.PendingDamagers[0];
            state = state with { PendingDamagers = state.PendingDamagers.Skip(1).ToArray() };
            if (!_players[damagerSeat].IsAlive) continue;
            if (!chosen.IsAlive)
            {
                AddLog("SkillEffect",
                    $"{owner.Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】因所选角色已死亡，剩余清算取消。",
                    active.OwnerSeat, chosenSeat);
                break;
            }
            var damager = _players[damagerSeat];
            if (damager.Hp > chosen.Hp)
            {
                // Keep the committed cursor while the damage child is active;
                // the typed receipt resumes this instruction after it returns.
                ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
                {
                    ReexecuteParticipantInstruction = true,
                    ZhuihuanRetaliate = state with { DamagedSeats = state.DamagedSeats.Append(damagerSeat).ToArray() }
                });
                return BeginProgramSkillDamage(GetActiveProgramFrame(frame.Id), damagerSeat, 2,
                    explicitSourceSeat: state.ChosenSeat);
            }
            var hand = GetHand(damager);
            var count = Math.Min(2, hand.Count);
            if (count > 0)
            {
                var picked = new List<Card>();
                var pool = hand.ToList();
                for (var index = 0; index < count; index++)
                {
                    var card = pool[_random.Next(pool.Count)];
                    pool.Remove(card);
                    picked.Add(card);
                }
                MoveLogicalDiscardCards(active.OwnerSeat, damagerSeat, picked, CardLocation.Hand(damagerSeat),
                    new($"skill-program.{active.SkillId}.zhuihuan-discard"));
            }
            AddLog("SkillEffect", count > 0
                ? $"{owner.Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】：{_players[damagerSeat].Name} 的体力值不大于 {_players[chosenSeat].Name}，随机弃置 {count} 张手牌。"
                : $"{owner.Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】：{_players[damagerSeat].Name} 没有手牌可弃置。",
                active.OwnerSeat, damagerSeat);
            state = state with
            {
                DiscardedSeats = state.DiscardedSeats.Append(damagerSeat).ToArray(),
                DiscardedCardCount = state.DiscardedCardCount + count
            };
        }
        var pendingArms = PendingZhuihuanArmFrameIds(owner.Seat, chosenSeat);
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
        { ZhuihuanRetaliate = null, ReexecuteParticipantInstruction = false });
        AdvanceEventRulesAndQueueFact(new ProgramZhuihuanResolvedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), owner.Seat, chosenSeat, pendingArms,
            state.DamagedSeats, state.DiscardedSeats, state.DiscardedCardCount));
        foreach (var armFrameId in pendingArms) _zhuihuanArmChoices.Remove(armFrameId);
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool IsValidZhuihuanContinuation(ProgramSkillFrame frame, SkillProgramEffect paused) =>
        paused.Op == SkillProgramEffectOp.ZhuihuanRetaliate && frame.ZhuihuanRetaliate is { } state &&
        frame.WindowContext is { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, SourceSeat: { } boundarySeat } &&
        state.ChosenSeat == boundarySeat && state.ChosenSeat == _currentSeat && IsValidPlayerSeat(state.ChosenSeat) &&
        state.DamagedSeats.Count > 0 && state.DamagedSeats.All(IsValidPlayerSeat) &&
        state.PendingDamagers.All(IsValidPlayerSeat) && state.DiscardedSeats.All(IsValidPlayerSeat) &&
        state.DiscardedCardCount >= 0 && state.DiscardedCardCount <= 2 * state.DiscardedSeats.Count;

    // One history pass: every own armed event for this marked seat opens a
    // damage ledger that the matching resolution event closes; damages recorded
    // between arm and now attribute to every still-open ledger.
    private IReadOnlyList<int> CollectZhuihuanPendingDamagers(int ownerSeat, int chosenSeat)
    {
        var facts = CompleteProgramEventHistory().ToArray();
        var order = new List<long>();
        var ledgers = new Dictionary<long, List<int>>();
        var resolved = new HashSet<long>();
        foreach (var fact in facts)
        {
            switch (fact)
            {
                case ProgramZhuihuanArmedEvent armed when armed.OwnerSeat == ownerSeat &&
                    _zhuihuanArmChoices.TryGetValue(armed.FrameId, out var armedSeat) && armedSeat == chosenSeat:
                    order.Add(armed.FrameId);
                    ledgers[armed.FrameId] = [];
                    break;
                case ProgramZhuihuanResolvedEvent resolution when resolution.OwnerSeat == ownerSeat:
                    foreach (var armFrameId in resolution.ArmFrameIds)
                    {
                        resolved.Add(armFrameId);
                        ledgers.Remove(armFrameId);
                    }
                    break;
                case DamageAppliedEvent damage when damage.TargetSeat == chosenSeat && damage.Amount > 0 &&
                    !damage.SourceLess:
                    foreach (var ledger in ledgers.Values)
                        if (!ledger.Contains(damage.SourceSeat))
                            ledger.Add(damage.SourceSeat);
                    break;
            }
        }
        return order.Where(armFrameId => !resolved.Contains(armFrameId))
            .SelectMany(armFrameId => ledgers[armFrameId])
            .ToArray();
    }

    private IReadOnlyList<long> PendingZhuihuanArmFrameIds(int ownerSeat, int chosenSeat)
    {
        var resolved = CompleteProgramEventHistory().OfType<ProgramZhuihuanResolvedEvent>()
            .Where(resolution => resolution.OwnerSeat == ownerSeat)
            .SelectMany(resolution => resolution.ArmFrameIds).ToHashSet();
        return CompleteProgramEventHistory().OfType<ProgramZhuihuanArmedEvent>()
            .Where(armed => armed.OwnerSeat == ownerSeat && !resolved.Contains(armed.FrameId) &&
                _zhuihuanArmChoices.TryGetValue(armed.FrameId, out var armedSeat) && armedSeat == chosenSeat)
            .Select(armed => armed.FrameId).ToArray();
    }

    [ProgramAiChoice(SkillProgramEffectOp.ZhuihuanArm)]
    private PromptChoice SelectAiZhuihuanArmChoice(PendingDecision decision) =>
        decision.Choices
            .OrderByDescending(choice => choice.Targets.Contains(decision.PlayerSeat))
            .ThenBy(choice => choice.Targets.Select(seat => _players[seat].Hp).FirstOrDefault())
            .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal)
            .First();
}
