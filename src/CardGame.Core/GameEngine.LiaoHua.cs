namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string DangxianSkillId = "classic:dangxian";
    private const string FuliSkillId = "classic:fuli";
    private const string FuliUsageId = "activation";

    private bool _dangxianExtraPlayActive;

    private bool UsesFormalLiaoHua =>
        _rulesVersion >= 110 &&
        IsClassicIdentityMode &&
        _contentRegistry?.Packages.Any(package =>
            package.Id == "standard-classic-generals" &&
            package.Version >= new Version(1, 88, 0)) == true;

    private bool TryBeginDangxianExtraPlay(PlayerRuntime current)
    {
        if (!UsesFormalLiaoHua || !current.IsAlive || !HasRuntimeSkill(current, DangxianSkillId))
        {
            return false;
        }

        _dangxianExtraPlayActive = true;
        QueueGameEvent(new DangxianExtraPlayPhaseEvent(current.Seat, Started: true));
        AddLog(
            "SkillTriggered",
            $"{current.Name} 的锁定技【当先】生效，在正常回合流程前执行一个额外的出牌阶段。",
            current.Seat);
        EnterPlayPhase(current);
        return true;
    }

    private void BeginNormalTurnStartAfterDangxian(PlayerRuntime current)
    {
        if (TryBeginZiliAwakening(current))
        {
            return;
        }

        BeginTurnStartAfterZili(current);
    }

    private void CompleteCurrentPlayPhase()
    {
        if (!_dangxianExtraPlayActive)
        {
            BeginDiscardPhase();
            return;
        }

        var current = _players[_currentSeat];
        _dangxianExtraPlayActive = false;
        QueueGameEvent(new DangxianExtraPlayPhaseEvent(current.Seat, Started: false));
        AddLog("SkillResolved", $"{current.Name} 的【当先】额外出牌阶段结束，继续正常回合流程。", current.Seat);
        BeginNormalTurnStartAfterDangxian(current);
    }

    private void CancelDangxianExtraPlay() => _dangxianExtraPlayActive = false;

    private bool CanUseFuli(PlayerRuntime responder, DyingResolution dying)
    {
        if (!UsesFormalLiaoHua || responder.Seat != dying.VictimSeat || !responder.IsAlive ||
            responder.Hp > 0 || !HasRuntimeSkill(responder, FuliSkillId))
        {
            return false;
        }

        var metadata = _contentRegistry?.GetSkill(FuliSkillId);
        return metadata is { Tags: var tags } && tags.HasFlag(SkillTag.Limited) &&
               _skillRuntimeState.GetUsage(
                   responder.Seat,
                   FuliSkillId,
                   FuliUsageId,
                   SkillUsageScope.Game) == 0;
    }

    private void ResolveFuli()
    {
        var dying = _pendingDying ?? throw new InvalidOperationException("There is no dying resolution for Fuli.");
        var victim = _players[dying.VictimSeat];
        if (!CanUseFuli(victim, dying) ||
            _pendingDecision is { } decision &&
            (decision.Kind != DecisionKind.RescueDying || decision.PlayerSeat != victim.Seat))
        {
            throw new InvalidOperationException("Fuli is not legal in the current dying window.");
        }

        if (!_skillRuntimeState.TryConsumeUsage(
                victim.Seat,
                FuliSkillId,
                FuliUsageId,
                SkillUsageScope.Game,
                limit: 1))
        {
            throw new InvalidOperationException("Fuli has already been used in this game.");
        }

        ClearPendingDecision();
        var livingFactionCount = Math.Max(1, GetLivingFactionCount());
        var previousHp = victim.Hp;
        var targetHp = Math.Min(victim.MaxHp, livingFactionCount);
        var recoveryAmount = targetHp - previousHp;
        var recoveryFrameId = BeginRecovery(dying.FrameId, victim.Seat, victim.Seat, recoveryAmount);
        victim.Hp = targetHp;
        QueueGameEvent(new RecoveryAppliedEvent(
            victim.Seat,
            victim.Seat,
            recoveryAmount,
            victim.Hp));
        PopResolutionFrame(recoveryFrameId, ResolutionFrameKind.Recovery);

        victim.IsFaceDown = !victim.IsFaceDown;
        QueueGameEvent(new FuliResolvedEvent(
            dying.FrameId,
            victim.Seat,
            livingFactionCount,
            previousHp,
            victim.Hp,
            victim.IsFaceDown));
        AddLog(
            "SkillTriggered",
            $"{victim.Name} 发动限定技【伏枥】，按 {livingFactionCount} 个现存势力将体力回复至 {victim.Hp} 点并翻面。",
            victim.Seat);
        CompleteDying(dying, survived: true);
    }

    private EngineRunResult HumanFuliCore(bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.RescueDying);
        ResolveFuli();
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }
}
