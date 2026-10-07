namespace CardGame.Core;

// 落宠 evidence: one scalar record per accepted (option, target) invocation. The
// per-round ledgers (one use per option, one target per round) and the removed
// options are both derived from the committed ledger, so cold recovery replays
// the accepted commands into the same availability state. Drawn cards enter the
// target's hand, so only the count is recorded (hidden card ids stay out of the
// ordinary snapshot); the discarded card ids are public discard-pile movements.
public sealed record ProgramLuochongResolvedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int RoundNumber, string OptionId, int TargetSeat,
    int RecoveredHp, int LostHp, IReadOnlyList<int> DiscardedCardIds, int DrawCount) : IGameEvent;

// 哀尘 evidence: the owner's permanent removal of one remaining 落宠 option at a
// dying entry; RemainingOptionCount is the count after this removal.
public sealed record ProgramAichenRemovedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, string RemovedOptionId, int RemainingOptionCount) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public LuochongPendingState? LuochongPending { get; init; }
}

// The suspension receipt of one accepted 落宠 invocation. A nonzero remaining
// discard count marks the target's own discard prompts; a zero count on op
// re-entry marks the resume after a losing-hp dying child.
public sealed record LuochongPendingState(string OptionId, int TargetSeat,
    int RemainingDiscards, IReadOnlyList<int>? DiscardedCardIds = null)
{
    public IReadOnlyList<int> Discarded { get; init; } = DiscardedCardIds ?? [];
}

public sealed partial class GameEngine
{
    internal static readonly string[] LuochongOptionIds = ["recover", "loseHp", "discardTwo", "drawTwo"];
    private static readonly string[] AichenRemovalPreference = ["recover", "loseHp", "discardTwo", "drawTwo"];

    internal static string LuochongOptionDisplayName(string optionId) => optionId switch
    {
        "recover" => "回复1点体力",
        "loseHp" => "失去1点体力",
        "discardTwo" => "弃置两张牌",
        "drawTwo" => "摸两张牌",
        _ => throw new InvalidOperationException($"Unknown Luochong option '{optionId}'.")
    };

    private static bool IsLuochongOptionId(string optionId) => LuochongOptionIds.Contains(optionId);

    // 哀尘 removals are permanent; the removed set is the owner's event history.
    private HashSet<string> AichenRemovedOptions(int ownerSeat) =>
        CompleteProgramEventHistory().OfType<ProgramAichenRemovedEvent>()
            .Where(fact => fact.OwnerSeat == ownerSeat)
            .Select(fact => fact.RemovedOptionId)
            .ToHashSet(StringComparer.Ordinal);

    private (IReadOnlySet<string> Options, IReadOnlySet<int> Targets) LuochongRoundLedger(int ownerSeat) =>
        (CompleteProgramEventHistory().OfType<ProgramLuochongResolvedEvent>()
                .Where(fact => fact.OwnerSeat == ownerSeat && fact.RoundNumber == _roundNumber)
                .Select(fact => fact.OptionId).ToHashSet(StringComparer.Ordinal),
            CompleteProgramEventHistory().OfType<ProgramLuochongResolvedEvent>()
                .Where(fact => fact.OwnerSeat == ownerSeat && fact.RoundNumber == _roundNumber)
                .Select(fact => fact.TargetSeat).ToHashSet());

    // 落宠 discard pool: the target's own self-discardable hand plus equipment;
    // judgment-zone cards are outside this skill's discard scope.
    private IReadOnlyList<(Card Card, CardLocation Location)> LuochongDiscardPool(int targetSeat)
    {
        var target = _players[targetSeat];
        return GetSelfDiscardableOwnedHand(target)
            .Select(card => (card, CardLocation.Hand(targetSeat)))
            .Concat(GetEquipment(target).Select(card => (card, CardLocation.Equipment(targetSeat))))
            .OrderBy(entry => entry.card.Id)
            .ToArray();
    }

    private bool IsLuochongOptionAvailableFor(int ownerSeat, string optionId, int targetSeat,
        IReadOnlySet<string> usedOptions, IReadOnlySet<int> usedTargets, IReadOnlySet<string> removedOptions)
    {
        if (removedOptions.Contains(optionId) || usedOptions.Contains(optionId) ||
            usedTargets.Contains(targetSeat) || !_players[targetSeat].IsAlive)
            return false;
        var target = _players[targetSeat];
        return optionId switch
        {
            // Recovery on an unwounded target is a no-op and never offered.
            "recover" => target.Hp < target.MaxHp,
            "loseHp" => true,
            "discardTwo" => LuochongDiscardPool(targetSeat).Count > 0,
            "drawTwo" => true,
            _ => false
        };
    }

    private IReadOnlyList<(string OptionId, int TargetSeat)> CollectLuochongAvailablePairs(
        int ownerSeat, out (IReadOnlySet<string> Options, IReadOnlySet<int> Targets) ledger)
    {
        var (options, targets) = LuochongRoundLedger(ownerSeat);
        ledger = (options, targets);
        var removed = AichenRemovedOptions(ownerSeat);
        return LuochongOptionIds
            .Where(optionId => !removed.Contains(optionId) && !options.Contains(optionId))
            .SelectMany(optionId => _players.Where(player => player.IsAlive).Select(player => player.Seat)
                .Where(targetSeat => !targets.Contains(targetSeat))
                .Where(targetSeat => IsLuochongOptionAvailableFor(ownerSeat, optionId, targetSeat,
                    options, targets, removed))
                .Select(targetSeat => (optionId, targetSeat)))
            .OrderBy(entry => Array.IndexOf(LuochongOptionIds, entry.optionId))
            .ThenBy(entry => entry.targetSeat)
            .ToArray();
    }

    internal bool CanRunLuochongResolve(int ownerSeat)
    {
        if (_winner != Winner.None || !IsValidPlayerSeat(ownerSeat) || !_players[ownerSeat].IsAlive)
            return false;
        return CollectLuochongAvailablePairs(ownerSeat, out _).Count > 0;
    }

    // 落宠 resolve: the fresh invocation offers every available pair; a pending
    // state on re-entry resumes after the losing-hp dying child and finalizes.
    private SkillProgramStepOutcome LuochongProgramResolve(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        if (frame.LuochongPending is { } pending)
            return FinalizeLuochongResolution(active, pending);
        if (!owner.IsAlive)
            return SkillProgramStepOutcome.Continue;
        var pairs = CollectLuochongAvailablePairs(active.OwnerSeat, out _);
        if (pairs.Count == 0) return SkillProgramStepOutcome.Continue;
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = pairs.Select(pair => new PromptChoice(
                new ChoiceId($"luochong-pick.frame-{frame.Id}.{pair.OptionId}-{pair.TargetSeat}"),
                $"令 {_players[pair.TargetSeat].Name} {LuochongOptionDisplayName(pair.OptionId)}。",
                [], [pair.TargetSeat],
                new Dictionary<string, string>
                {
                    ["program-action"] = "luochong-pick",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["option"] = pair.OptionId,
                    ["target-seat"] = pair.TargetSeat.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }))
            .ToList<PromptChoice>();
        choices.Add(new PromptChoice(
            new ChoiceId($"luochong-decline.frame-{frame.Id}"),
            "不发动【落宠】。",
            [], [],
            new Dictionary<string, string> { ["program-action"] = "luochong-decline" }));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】你可以选择一项，令一名角色执行。每轮每项每名角色限一次。",
            [], choices.Where(choice => choice.Targets.Count > 0).SelectMany(choice => choice.Targets).ToArray(),
            active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择选项与目标", presentation.Description),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveLuochongPickChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Luochong choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.LuochongResolve } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Luochong choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        ClearPendingDecision();
        if (selected.Parameters.GetValueOrDefault("program-action") == "luochong-decline")
        {
            AdvanceRuntimeProgram(active.Id);
            return;
        }
        if (!int.TryParse(selected.Parameters.GetValueOrDefault("target-seat"),
                System.Globalization.CultureInfo.InvariantCulture, out var targetSeat) ||
            !IsValidPlayerSeat(targetSeat) || !_players[targetSeat].IsAlive ||
            selected.Parameters.GetValueOrDefault("option") is not { } option || !IsLuochongOptionId(option))
            throw new InvalidOperationException("The Luochong choice lost its option or target.");
        if (!CanRunLuochongResolve(active.OwnerSeat))
            throw new InvalidOperationException("The Luochong choice has no remaining available pair.");
        var pending = new LuochongPendingState(option, targetSeat, 0);
        switch (option)
        {
            case "recover":
                new ProgramSkillHost(this).Recover(active.Id, active.OwnerSeat, targetSeat, 1, null, null);
                AddLog("SkillTriggered",
                    $"{_players[active.OwnerSeat].Name} 发动【{_contentRegistry.GetSkill(active.SkillId).Name}】，令 {_players[targetSeat].Name} 回复1点体力。",
                    active.OwnerSeat, targetSeat);
                AdvanceEventRulesAndQueueFact(LuochongResolvedEvent(active, pending));
                AdvanceRuntimeProgram(active.Id);
                return;
            case "loseHp" when _players[targetSeat].Hp == 1:
                // The dying child suspends below this frame; rewinding the cursor
                // makes the child completion re-enter this instruction, where the
                // pending state finalizes the resolution.
                ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
                {
                    InstructionIndex = frame.InstructionIndex - 1,
                    LuochongPending = pending
                });
                AddLog("SkillTriggered",
                    $"{_players[active.OwnerSeat].Name} 发动【{_contentRegistry.GetSkill(active.SkillId).Name}】，令 {_players[targetSeat].Name} 失去1点体力。",
                    active.OwnerSeat, targetSeat);
                new ProgramSkillHost(this).LoseHp(active.Id, active.SkillId, targetSeat, 1);
                return;
            case "loseHp":
                new ProgramSkillHost(this).LoseHp(active.Id, active.SkillId, targetSeat, 1);
                AddLog("SkillTriggered",
                    $"{_players[active.OwnerSeat].Name} 发动【{_contentRegistry.GetSkill(active.SkillId).Name}】，令 {_players[targetSeat].Name} 失去1点体力。",
                    active.OwnerSeat, targetSeat);
                AdvanceEventRulesAndQueueFact(LuochongResolvedEvent(active, pending));
                AdvanceRuntimeProgram(active.Id);
                return;
            case "discardTwo":
            {
                var remaining = Math.Min(2, LuochongDiscardPool(targetSeat).Count);
                ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
                {
                    LuochongPending = pending with { RemainingDiscards = remaining }
                });
                AddLog("SkillTriggered",
                    $"{_players[active.OwnerSeat].Name} 发动【{_contentRegistry.GetSkill(active.SkillId).Name}】，令 {_players[targetSeat].Name} 弃置牌。",
                    active.OwnerSeat, targetSeat);
                PresentLuochongDiscardPrompt(GetActiveProgramFrame(frame.Id));
                return;
            }
            default:
                DrawCards(_players[targetSeat], 2, true, new($"skill-program.{active.SkillId}.luochong-draw"));
                AddLog("SkillTriggered",
                    $"{_players[active.OwnerSeat].Name} 发动【{_contentRegistry.GetSkill(active.SkillId).Name}】，令 {_players[targetSeat].Name} 摸两张牌。",
                    active.OwnerSeat, targetSeat);
                AdvanceEventRulesAndQueueFact(LuochongResolvedEvent(active, pending));
                if (!TryBeginCardsMovedProgramWindow())
                    AdvanceRuntimeProgram(active.Id);
                return;
        }
    }

    private ProgramLuochongResolvedEvent LuochongResolvedEvent(ProgramSkillFrame active, LuochongPendingState pending) =>
        new(active.Id, active.SkillId, GetProgramBindingId(active), active.OwnerSeat, _roundNumber,
            pending.OptionId, pending.TargetSeat,
            pending.OptionId == "recover" ? 1 : 0,
            pending.OptionId == "loseHp" ? 1 : 0,
            pending.Discarded,
            pending.OptionId == "drawTwo" ? 2 : 0);

    // The finalize after a dying child: no card movements belong to this frame,
    // so the cursor advances directly.
    private SkillProgramStepOutcome FinalizeLuochongResolution(ProgramSkillFrame frame, LuochongPendingState pending)
    {
        var active = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { LuochongPending = null });
        AdvanceEventRulesAndQueueFact(LuochongResolvedEvent(active, pending));
        AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private void PresentLuochongDiscardPrompt(ProgramSkillFrame frame)
    {
        var pending = frame.LuochongPending ??
            throw new InvalidOperationException("The Luochong discard lost its pending state.");
        var active = GetActiveProgramFrame(frame.Id);
        var pool = LuochongDiscardPool(pending.TargetSeat);
        if (pending.RemainingDiscards <= 0 || pool.Count == 0)
        {
            CompleteLuochongDiscardStage(GetActiveProgramFrame(frame.Id));
            return;
        }
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = pool.Select(entry => new PromptChoice(
            new ChoiceId($"luochong-discard.frame-{frame.Id}.card-{entry.Card.Id}"),
            $"弃置【{entry.Card.DisplayName}】。",
            [entry.Card.Id], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "luochong-discard",
                ["card-id"] = entry.Card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, pending.TargetSeat,
            $"【{presentation.Name}】请弃置 {pending.RemainingDiscards} 张牌。",
            pool.Select(entry => entry.Card.Id).ToArray(), [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            TargetSeat = pending.TargetSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 弃牌",
                $"{_players[active.OwnerSeat].Name} 令你弃置 {pending.RemainingDiscards} 张牌。"),
            Choices = choices.AsReadOnly()
        };
        _status = _players[pending.TargetSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveLuochongDiscardChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Luochong discard choice lost its program frame.");
        var pending = frame.LuochongPending ??
            throw new InvalidOperationException("The Luochong discard lost its pending state.");
        if (!int.TryParse(selected.Parameters.GetValueOrDefault("card-id"),
                System.Globalization.CultureInfo.InvariantCulture, out var cardId) ||
            LuochongDiscardPool(pending.TargetSeat).SingleOrDefault(entry => entry.Card.Id == cardId).Card is null)
            throw new InvalidOperationException("The Luochong discard card is no longer discardable.");
        ClearPendingDecision();
        var active = GetActiveProgramFrame(frame.Id);
        var entry = LuochongDiscardPool(pending.TargetSeat).Single(item => item.Card.Id == cardId);
        MoveCard(entry.Card, entry.Location, CardLocation.DiscardPile,
            new($"skill-program.{active.SkillId}.luochong-discard"));
        AddLog("CardDiscarded",
            $"{_players[pending.TargetSeat].Name} 因【{_contentRegistry!.GetSkill(active.SkillId).Name}】弃置【{entry.Card.DisplayName}】。",
            pending.TargetSeat, active.OwnerSeat);
        var discarded = pending.Discarded.Append(cardId).ToArray();
        if (pending.RemainingDiscards > 1)
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
            {
                LuochongPending = pending with
                {
                    RemainingDiscards = pending.RemainingDiscards - 1,
                    DiscardedCardIds = Array.AsReadOnly(discarded)
                }
            });
            PresentLuochongDiscardPrompt(GetActiveProgramFrame(frame.Id));
            return;
        }
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
        {
            LuochongPending = pending with { RemainingDiscards = 0, DiscardedCardIds = Array.AsReadOnly(discarded) }
        });
        CompleteLuochongDiscardStage(GetActiveProgramFrame(frame.Id));
    }

    private void CompleteLuochongDiscardStage(ProgramSkillFrame frame)
    {
        var pending = frame.LuochongPending ??
            throw new InvalidOperationException("The Luochong discard stage lost its pending state.");
        var active = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { LuochongPending = null });
        AdvanceEventRulesAndQueueFact(LuochongResolvedEvent(active, pending));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }

    // 哀尘: the locked dying-entry removal; with at most one option left the
    // trigger resolves silently.
    private SkillProgramStepOutcome AichenProgramRemoveOption(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var removed = AichenRemovedOptions(active.OwnerSeat);
        var remaining = LuochongOptionIds.Where(optionId => !removed.Contains(optionId)).ToArray();
        if (remaining.Length <= 1) return SkillProgramStepOutcome.Continue;
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = remaining.Select(optionId => new PromptChoice(
                new ChoiceId($"aichen-remove.frame-{frame.Id}.{optionId}"),
                $"移除“落宠”的选项：{LuochongOptionDisplayName(optionId)}。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "aichen-remove",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["option"] = optionId
                }))
            .ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】“落宠”选项数大于1，请选择移除其中一项。",
            [], [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 移除选项", presentation.Description),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveAichenRemoveChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Aichen choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.AichenRemoveOption } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Aichen choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (selected.Parameters.GetValueOrDefault("option") is not { } option || !IsLuochongOptionId(option) ||
            AichenRemovedOptions(active.OwnerSeat).Contains(option))
            throw new InvalidOperationException("The Aichen removal option is no longer available.");
        ClearPendingDecision();
        var remainingCount = LuochongOptionIds.Length - AichenRemovedOptions(active.OwnerSeat).Count - 1;
        AddLog("SkillTriggered",
            $"{_players[active.OwnerSeat].Name} 发动【{_contentRegistry.GetSkill(active.SkillId).Name}】，移除“落宠”的选项：{LuochongOptionDisplayName(option)}。",
            active.OwnerSeat);
        AdvanceEventRulesAndQueueFact(new ProgramAichenRemovedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, option, remainingCount));
        AdvanceRuntimeProgram(active.Id);
    }

    // 落宠 pick: price every offered pair through the shared target scoring; the
    // decline choice only survives when every pair scores negatively.
    private PromptChoice SelectAiLuochongResolveChoice(PendingDecision decision)
    {
        if (decision.Choices[0].Parameters.GetValueOrDefault("program-action") == "luochong-discard")
            return decision.Choices
                .OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();
        var view = CreateSnapshot(decision.PlayerSeat);
        var brain = _aiBrains[decision.PlayerSeat];
        return decision.Choices
            .Select(choice =>
            {
                if (choice.Parameters.GetValueOrDefault("program-action") == "luochong-decline")
                    return (Choice: choice, Score: double.NegativeInfinity);
                var option = choice.Parameters.GetValueOrDefault("option") ?? "";
                var targetSeat = int.Parse(choice.Parameters.GetValueOrDefault("target-seat", "-1"),
                    System.Globalization.CultureInfo.InvariantCulture);
                var hint = option switch
                {
                    "drawTwo" => new SkillProgramAiHint(0, 0, 0, 2, 0, 0, false, false),
                    "recover" => new SkillProgramAiHint(0, 0, 0, 0, 1, 0, false, false),
                    "loseHp" => new SkillProgramAiHint(0, 0, 0, 0, 0, 1, false, false),
                    "discardTwo" => new SkillProgramAiHint(0, 0, 0, 0, 0, 0, false, false)
                        { TargetValueAdjustment = -10d },
                    _ => new SkillProgramAiHint(0, 0, 0, 0, 0, 0, false, false)
                };
                return (Choice: choice, Score: brain.ScoreProgramTarget(view, targetSeat, hint));
            })
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Choice.Id.Value, StringComparer.Ordinal)
            .First().Choice;
    }

    // 哀尘 removal: drop the lowest retention value first — recovery, then the
    // direct hp loss, then the two-card discard; the two-card draw outlasts all.
    private PromptChoice SelectAiAichenRemoveChoice(PendingDecision decision) =>
        decision.Choices
            .OrderBy(choice => Array.IndexOf(AichenRemovalPreference,
                choice.Parameters.GetValueOrDefault("option")))
            .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal)
            .First();

    private sealed partial class ProgramSkillHost : ITengFangLanProgramHost
    {
        public SkillProgramStepOutcome LuochongResolve(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.LuochongProgramResolve(frame, effect);
        public SkillProgramStepOutcome AichenRemoveOption(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.AichenProgramRemoveOption(frame, effect);
    }
}
