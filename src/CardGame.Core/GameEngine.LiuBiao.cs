namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ZishouSkillId = "classic:zishou";
    private const string ZongshiSkillId = "classic:zongshi";
    private const string ZishouActiveUsageId = "active";

    private ZishouDrawResolution? _pendingZishouDraw;

    private bool UsesFormalLiuBiao =>
        HasClassicGeneralPackage(new Version(1, 84, 0));

    private int GetLivingFactionCount() =>
        _players
            .Where(player => player.IsAlive)
            .Select(GetEffectiveFactionId)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Count();

    private bool HasZishouActive(PlayerRuntime player) =>
        UsesFormalLiuBiao &&
        HasRuntimeSkill(player, ZishouSkillId) &&
        _skillRuntimeState.GetUsage(
            player.Seat,
            ZishouSkillId,
            ZishouActiveUsageId,
            SkillUsageScope.Turn) > 0;

    private int GetZongshiHandLimitBonus(PlayerRuntime player) =>
        UsesFormalLiuBiao && HasRuntimeSkill(player, ZongshiSkillId)
            ? GetLivingFactionCount()
            : 0;

    private bool TryBeginZishouDrawChoice(
        PlayerRuntime current,
        DelayedTurnEffects delayedEffects)
    {
        if (!UsesFormalLiuBiao || !HasRuntimeSkill(current, ZishouSkillId))
        {
            return false;
        }

        var livingFactionCount = GetLivingFactionCount();
        _pendingZishouDraw = new ZishouDrawResolution(
            current.Seat,
            delayedEffects,
            livingFactionCount);
        _pendingDecision = new PendingDecision(
            DecisionKind.Zishou,
            current.Seat,
            $"是否发动【自守】，额外摸 {livingFactionCount} 张牌？发动后本回合出牌阶段使用的牌不能指定其他角色为目标。",
            [],
            [])
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            Choices =
            [
                new PromptChoice(
                    new ChoiceId($"zishou.use.turn-{_turnNumber}.seat-{current.Seat}"),
                    $"发动【自守】，额外摸 {livingFactionCount} 张牌；本回合只对自己使用牌。",
                    [],
                    [],
                    new Dictionary<string, string>
                    {
                        ["action"] = "zishou-use",
                        ["living-factions"] = livingFactionCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }),
                new PromptChoice(
                    new ChoiceId($"zishou.skip.turn-{_turnNumber}.seat-{current.Seat}"),
                    "不发动【自守】，按通常数量摸牌。",
                    [],
                    [],
                    new Dictionary<string, string>
                    {
                        ["action"] = "zishou-skip",
                        ["living-factions"] = livingFactionCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    })
            ]
        };
        _status = current.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        PublishState();
        return true;
    }

    private void ResolveZishouDrawChoice(bool useSkill)
    {
        var pending = _pendingZishouDraw ??
            throw new InvalidOperationException("There is no Zishou draw choice to resolve.");
        var current = _players[pending.PlayerSeat];
        var drawCount = GetTurnDrawCount(current) + (useSkill ? pending.LivingFactionCount : 0);

        if (useSkill)
        {
            if (!_skillRuntimeState.TryConsumeUsage(
                    current.Seat,
                    ZishouSkillId,
                    ZishouActiveUsageId,
                    SkillUsageScope.Turn,
                    limit: 1))
            {
                throw new InvalidOperationException("Zishou was already activated this turn.");
            }
            QueueGameEvent(new SkillUsageConsumedEvent(
                current.Seat,
                ZishouSkillId,
                ZishouActiveUsageId,
                SkillUsageScope.Turn,
                Count: 1));
        }

        ClearPendingDecision();
        DrawCards(current, drawCount, log: true);
        QueueGameEvent(new ZishouResolvedEvent(
            current.Seat,
            useSkill,
            pending.LivingFactionCount,
            drawCount));
        AddLog(
            useSkill ? "SkillTriggered" : "SkillSkipped",
            useSkill
                ? $"{current.Name} 发动【自守】，按 {pending.LivingFactionCount} 个现存势力额外摸牌；本回合出牌阶段不能以其他角色为牌的目标。"
                : $"{current.Name} 未发动【自守】，按通常数量摸牌。",
            current.Seat);
        _pendingZishouDraw = null;
        CompleteTurnStartAfterDraw(current, pending.DelayedEffects);
    }

    private CommandResult SubmitZishouPromptAnswer(PromptChoice selected)
    {
        if (_pendingZishouDraw is null ||
            _pendingDecision is not { Kind: DecisionKind.Zishou })
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的自守摸牌阶段窗口。");
        }

        if (!selected.Parameters.TryGetValue("action", out var action) ||
            selected.Cards.Count != 0 ||
            selected.Targets.Count != 0)
        {
            return Reject(CommandErrorCode.InvalidChoice, "自守选择不符合当前摸牌阶段窗口。");
        }

        return action switch
        {
            "zishou-use" => Accept(() => HumanZishouCore(
                useSkill: true,
                _options.AdvanceAfterHumanCommands)),
            "zishou-skip" => Accept(() => HumanZishouCore(
                useSkill: false,
                _options.AdvanceAfterHumanCommands)),
            _ => Reject(CommandErrorCode.InvalidChoice, "自守提示没有可识别的选择效果。")
        };
    }

    private EngineRunResult HumanZishouCore(bool useSkill, bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Zishou);
        ResolveZishouDrawChoice(useSkill);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private bool IsAiZishouPending() =>
        _pendingZishouDraw is { PlayerSeat: var playerSeat } &&
        _pendingDecision is { Kind: DecisionKind.Zishou, PlayerSeat: var decisionSeat } &&
        playerSeat == decisionSeat &&
        !_players[playerSeat].IsHuman;

    private void ResolvePendingAiZishou()
    {
        if (!IsAiZishouPending())
        {
            throw new InvalidOperationException("There is no AI Zishou choice to resolve.");
        }

        var pending = _pendingZishouDraw!;
        var owner = _players[pending.PlayerSeat];
        var canBenefitFromRecovery = owner.Hp < owner.MaxHp;
        var useSkill = pending.LivingFactionCount >= 3 ||
                       canBenefitFromRecovery ||
                       GetHand(owner).Count >= owner.Hp;
        ResolveZishouDrawChoice(useSkill);
        PublishState();
    }

    private IReadOnlyList<LegalAction> FilterZishouProhibitedCardActions(
        PlayerRuntime actor,
        IReadOnlyList<LegalAction> actions)
    {
        if (!HasZishouActive(actor) ||
            _phase != TurnPhase.Play ||
            _currentSeat != actor.Seat)
        {
            return actions;
        }

        return actions.Where(action =>
        {
            if (action.Kind is LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage)
            {
                return false;
            }

            if (action.Kind == LegalActionKind.UseEquipmentEffect && action.MaxTargetCount > 0)
            {
                return false;
            }

            return action.TargetSeats.All(targetSeat => targetSeat == actor.Seat);
        }).ToArray();
    }

    private IReadOnlyList<int> ApplyZishouGroupTargetRestriction(
        PlayerRuntime source,
        IReadOnlyList<int> targets) =>
        HasZishouActive(source)
            ? targets.Where(seat => seat == source.Seat).ToArray()
            : targets;

    private void AssertZishouInvariant()
    {
        if (_pendingZishouDraw is not { } pending)
        {
            if (_pendingDecision?.Kind == DecisionKind.Zishou)
            {
                throw new InvalidOperationException(
                    "A Zishou prompt cannot exist without its draw continuation.");
            }
            return;
        }

        var decision = _pendingDecision;
        if (!UsesFormalLiuBiao ||
            _phase != TurnPhase.Draw ||
            _currentSeat != pending.PlayerSeat ||
            !_players[pending.PlayerSeat].IsAlive ||
            !HasRuntimeSkill(_players[pending.PlayerSeat], ZishouSkillId) ||
            HasZishouActive(_players[pending.PlayerSeat]) ||
            pending.LivingFactionCount != GetLivingFactionCount() ||
            decision is not { Kind: DecisionKind.Zishou, IsPrivate: true } ||
            decision.PlayerSeat != pending.PlayerSeat ||
            decision.Choices.Count != 2 ||
            decision.Choices.Any(choice => choice.Cards.Count != 0 || choice.Targets.Count != 0) ||
            decision.Choices.Count(choice =>
                choice.Parameters.GetValueOrDefault("action") == "zishou-use") != 1 ||
            decision.Choices.Count(choice =>
                choice.Parameters.GetValueOrDefault("action") == "zishou-skip") != 1 ||
            _resolutionStack.Count != 0)
        {
            throw new InvalidOperationException(
                "A Zishou choice must retain its private two-branch prompt at a clean draw-phase boundary.");
        }

        var expectedStatus = _players[pending.PlayerSeat].IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
        if (_status != expectedStatus)
        {
            throw new InvalidOperationException("A Zishou prompt status does not match its owner.");
        }
    }

    private sealed record ZishouDrawResolution(
        int PlayerSeat,
        DelayedTurnEffects DelayedEffects,
        int LivingFactionCount);
}
