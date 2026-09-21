namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string JiangchiSkillId = "classic:jiangchi";
    private const string JiangchiDrawMoreUsageId = "draw-more";
    private const string JiangchiAssaultUsageId = "assault";

    private JiangchiDrawResolution? _pendingJiangchiDraw;

    private bool UsesFormalJiangchi =>
        _rulesVersion >= 103 &&
        IsClassicIdentityMode &&
        _contentRegistry?.Packages.Any(package =>
            package.Id == "standard-classic-generals" &&
            package.Version >= new Version(1, 81, 0)) == true;

    private bool HasJiangchiDrawMore(PlayerRuntime player) =>
        UsesFormalJiangchi &&
        HasRuntimeSkill(player, JiangchiSkillId) &&
        _skillRuntimeState.GetUsage(
            player.Seat,
            JiangchiSkillId,
            JiangchiDrawMoreUsageId,
            SkillUsageScope.Turn) > 0;

    private bool HasJiangchiAssault(PlayerRuntime player) =>
        UsesFormalJiangchi &&
        HasRuntimeSkill(player, JiangchiSkillId) &&
        _skillRuntimeState.GetUsage(
            player.Seat,
            JiangchiSkillId,
            JiangchiAssaultUsageId,
            SkillUsageScope.Turn) > 0;

    private bool IsJiangchiSlashForbidden(PlayerRuntime player) =>
        HasJiangchiDrawMore(player);

    private bool IgnoresJiangchiSlashDistance(PlayerRuntime player) =>
        HasJiangchiAssault(player) &&
        _phase == TurnPhase.Play &&
        _currentSeat == player.Seat;

    private int AddJiangchiSlashLimit(PlayerRuntime player, int currentLimit)
    {
        if (!HasJiangchiAssault(player) || currentLimit == int.MaxValue)
        {
            return currentLimit;
        }

        return (int)Math.Clamp((long)currentLimit + 1, 0, int.MaxValue);
    }

    private bool TryBeginJiangchiDrawChoice(
        PlayerRuntime current,
        DelayedTurnEffects delayedEffects)
    {
        if (!UsesFormalJiangchi || !HasRuntimeSkill(current, JiangchiSkillId))
        {
            return false;
        }

        _pendingJiangchiDraw = new JiangchiDrawResolution(current.Seat, delayedEffects);
        _pendingDecision = new PendingDecision(
            DecisionKind.Jiangchi,
            current.Seat,
            "请选择【将驰】本回合的摸牌与出杀方式。",
            [],
            [])
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            Choices =
            [
                new PromptChoice(
                    new ChoiceId("jiangchi.draw-more"),
                    "额外摸一张牌；直到回合结束，不能使用或打出【杀】。",
                    [],
                    [],
                    new Dictionary<string, string> { ["action"] = "jiangchi-draw-more" }),
                new PromptChoice(
                    new ChoiceId("jiangchi.assault"),
                    "少摸一张牌；本回合出牌阶段使用【杀】无距离限制且可额外使用一张。",
                    [],
                    [],
                    new Dictionary<string, string> { ["action"] = "jiangchi-assault" }),
                new PromptChoice(
                    new ChoiceId("jiangchi.skip"),
                    "不发动【将驰】，按通常数量摸牌。",
                    [],
                    [],
                    new Dictionary<string, string> { ["action"] = "jiangchi-skip" })
            ]
        };
        _status = current.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        PublishState();
        return true;
    }

    private void ResolveJiangchiDrawChoice(JiangchiMode mode)
    {
        var pending = _pendingJiangchiDraw ??
            throw new InvalidOperationException("There is no Jiangchi draw choice to resolve.");
        var current = _players[pending.PlayerSeat];
        var normalDrawCount = GetTurnDrawCount(current);
        var drawCount = mode switch
        {
            JiangchiMode.DrawMore => normalDrawCount + 1,
            JiangchiMode.Assault => Math.Max(0, normalDrawCount - 1),
            JiangchiMode.Skipped => normalDrawCount,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Jiangchi mode.")
        };

        var usageId = mode switch
        {
            JiangchiMode.DrawMore => JiangchiDrawMoreUsageId,
            JiangchiMode.Assault => JiangchiAssaultUsageId,
            _ => null
        };
        if (usageId is not null)
        {
            if (!_skillRuntimeState.TryConsumeUsage(
                    current.Seat,
                    JiangchiSkillId,
                    usageId,
                    SkillUsageScope.Turn,
                    limit: 1))
            {
                throw new InvalidOperationException("Jiangchi selected the same turn mode twice.");
            }
            QueueGameEvent(new SkillUsageConsumedEvent(
                current.Seat,
                JiangchiSkillId,
                usageId,
                SkillUsageScope.Turn,
                Count: 1));
        }

        ClearPendingDecision();
        DrawCards(current, drawCount, log: true);
        QueueGameEvent(new JiangchiResolvedEvent(current.Seat, mode, drawCount));
        AddLog(
            mode == JiangchiMode.Skipped ? "SkillSkipped" : "SkillTriggered",
            mode switch
            {
                JiangchiMode.DrawMore =>
                    $"{current.Name} 发动【将驰】多摸一张牌，本回合不能使用或打出【杀】。",
                JiangchiMode.Assault =>
                    $"{current.Name} 发动【将驰】少摸一张牌，本回合出牌阶段使用【杀】无距离限制且次数上限 +1。",
                _ => $"{current.Name} 未发动【将驰】，按通常数量摸牌。"
            },
            current.Seat);
        _pendingJiangchiDraw = null;
        CompleteTurnStartAfterDraw(current, pending.DelayedEffects);
    }

    private CommandResult SubmitJiangchiPromptAnswer(PromptChoice selected)
    {
        if (_pendingJiangchiDraw is null ||
            _pendingDecision is not { Kind: DecisionKind.Jiangchi })
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的将驰摸牌阶段窗口。");
        }

        if (!selected.Parameters.TryGetValue("action", out var action) ||
            selected.Cards.Count != 0 ||
            selected.Targets.Count != 0)
        {
            return Reject(CommandErrorCode.InvalidChoice, "将驰选择不符合当前摸牌阶段窗口。");
        }

        return action switch
        {
            "jiangchi-draw-more" => Accept(() => HumanJiangchiCore(
                JiangchiMode.DrawMore,
                _options.AdvanceAfterHumanCommands)),
            "jiangchi-assault" => Accept(() => HumanJiangchiCore(
                JiangchiMode.Assault,
                _options.AdvanceAfterHumanCommands)),
            "jiangchi-skip" => Accept(() => HumanJiangchiCore(
                JiangchiMode.Skipped,
                _options.AdvanceAfterHumanCommands)),
            _ => Reject(CommandErrorCode.InvalidChoice, "将驰提示没有可识别的选择效果。")
        };
    }

    private EngineRunResult HumanJiangchiCore(
        JiangchiMode mode,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Jiangchi);
        ResolveJiangchiDrawChoice(mode);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private bool IsAiJiangchiPending() =>
        _pendingJiangchiDraw is { PlayerSeat: var playerSeat } &&
        _pendingDecision is { Kind: DecisionKind.Jiangchi, PlayerSeat: var decisionSeat } &&
        playerSeat == decisionSeat &&
        !_players[playerSeat].IsHuman;

    private void ResolvePendingAiJiangchi()
    {
        if (!IsAiJiangchiPending())
        {
            throw new InvalidOperationException("There is no AI Jiangchi choice to resolve.");
        }

        var current = _players[_pendingJiangchiDraw!.PlayerSeat];
        var slashCount = GetSlashUseCards(current).Count;
        var mode = slashCount >= 2
            ? JiangchiMode.Assault
            : slashCount == 0
                ? JiangchiMode.DrawMore
                : JiangchiMode.Skipped;
        ResolveJiangchiDrawChoice(mode);
        PublishState();
    }

    private void AssertJiangchiInvariant()
    {
        if (_pendingJiangchiDraw is not { } pending)
        {
            if (_pendingDecision?.Kind == DecisionKind.Jiangchi)
            {
                throw new InvalidOperationException(
                    "A Jiangchi prompt cannot exist without its draw continuation.");
            }
            return;
        }

        var decision = _pendingDecision;
        if (!UsesFormalJiangchi ||
            _phase != TurnPhase.Draw ||
            _currentSeat != pending.PlayerSeat ||
            !_players[pending.PlayerSeat].IsAlive ||
            !HasRuntimeSkill(_players[pending.PlayerSeat], JiangchiSkillId) ||
            HasJiangchiDrawMore(_players[pending.PlayerSeat]) ||
            HasJiangchiAssault(_players[pending.PlayerSeat]) ||
            decision is not { Kind: DecisionKind.Jiangchi, IsPrivate: true } ||
            decision.PlayerSeat != pending.PlayerSeat ||
            decision.Choices.Count != 3 ||
            decision.Choices.Any(choice => choice.Cards.Count != 0 || choice.Targets.Count != 0) ||
            decision.Choices.Count(choice =>
                choice.Parameters.GetValueOrDefault("action") == "jiangchi-draw-more") != 1 ||
            decision.Choices.Count(choice =>
                choice.Parameters.GetValueOrDefault("action") == "jiangchi-assault") != 1 ||
            decision.Choices.Count(choice =>
                choice.Parameters.GetValueOrDefault("action") == "jiangchi-skip") != 1 ||
            _resolutionStack.Count != 0)
        {
            throw new InvalidOperationException(
                "A Jiangchi choice must retain its private three-branch prompt at a clean draw-phase boundary.");
        }

        var expectedStatus = _players[pending.PlayerSeat].IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
        if (_status != expectedStatus)
        {
            throw new InvalidOperationException("A Jiangchi prompt status does not match its owner.");
        }
    }

    private sealed record JiangchiDrawResolution(
        int PlayerSeat,
        DelayedTurnEffects DelayedEffects);
}
