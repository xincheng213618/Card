namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TryBeginWuhunDeathTargetSelection(
        DeathResolution death,
        PlayerRuntime owner)
    {
        if (!SupportsWuhunDeathTargetSelection ||
            _winner != Winner.None ||
            !EnabledPassiveSkills(owner).Any(skill => skill.Kind == SkillKind.Wuhun))
        {
            return false;
        }

        var candidates = GameRules.GetMaximumMarkerCandidates(_players.Select(player =>
            new PlayerMarkerCandidateState(
                player.Seat,
                player.IsAlive,
                GetMarkerSourceCount(player, PlayerMarkerKind.Nightmare, owner.Seat))));
        if (candidates.Count == 0)
        {
            return false;
        }

        var frameId = ++_resolutionSequence;
        _resolutionStack.Add(new DeathSkillFrame(
            frameId,
            death.FrameId,
            owner.Seat,
            SkillKind.Wuhun,
            candidates));
        var pending = new DeathSkillResolution(
            frameId,
            death,
            owner.Seat,
            SkillKind.Wuhun,
            candidates,
            _pendingDeathSkill);
        _pendingDeathSkill = pending;
        QueueGameEvent(new DeathSkillStartedEvent(
            frameId,
            death.FrameId,
            owner.Seat,
            SkillKind.Wuhun,
            candidates));
        AddLog(
            "SkillTriggered",
            $"{owner.Name} 死亡时触发【武魂】，须从梦魇标记最多的角色中选择一名。",
            owner.Seat);

        _pendingDecision = CreateWuhunTargetDecision(pending);
        _status = owner.IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
        return true;
    }

    private PendingDecision CreateWuhunTargetDecision(DeathSkillResolution pending)
    {
        var owner = _players[pending.OwnerSeat];
        var choices = pending.CandidateSeats
            .Select(targetSeat => new PromptChoice(
                new ChoiceId($"wuhun.target-{targetSeat}"),
                $"令 {_players[targetSeat].Name} 进行【武魂】判定。",
                [],
                [targetSeat],
                new Dictionary<string, string> { ["action"] = "wuhun-target" }))
            .ToArray();
        return new PendingDecision(
            DecisionKind.WuhunTarget,
            owner.Seat,
            "请选择一名梦魇标记最多的存活角色进行【武魂】判定。",
            [],
            pending.CandidateSeats,
            SourceSeat: owner.Seat)
        {
            PromptId = CreatePromptId(),
            Choices = Array.AsReadOnly(choices)
        };
    }

    private bool IsAiWuhunTargetPending() =>
        _pendingDeathSkill is { } pending &&
        _pendingDecision is { Kind: DecisionKind.WuhunTarget, PlayerSeat: var ownerSeat } &&
        ownerSeat == pending.OwnerSeat &&
        !_players[ownerSeat].IsHuman;

    private void ResolvePendingAiWuhunTarget()
    {
        var pending = _pendingDeathSkill ??
            throw new InvalidOperationException("An AI Wuhun target choice has no death-skill continuation.");
        ResolveWuhunTargetSelection(pending, pending.CandidateSeats[0]);
        PublishState();
    }

    private void ResolveWuhunTargetSelection(
        DeathSkillResolution pending,
        int targetSeat)
    {
        if (!ReferenceEquals(_pendingDeathSkill, pending) ||
            _pendingDecision is not { Kind: DecisionKind.WuhunTarget, PlayerSeat: var ownerSeat } ||
            ownerSeat != pending.OwnerSeat ||
            !pending.CandidateSeats.Contains(targetSeat) ||
            !_players[targetSeat].IsAlive)
        {
            throw new InvalidOperationException("The Wuhun target is not legal for the current death-skill window.");
        }

        ClearPendingDecision();
        pending.TargetSeat = targetSeat;
        ReplaceDeathSkillFrame(pending, ResolutionFrameStep.ResolvingEffect);
        QueueGameEvent(new DeathSkillTargetSelectedEvent(
            pending.FrameId,
            pending.OwnerSeat,
            SkillKind.Wuhun,
            targetSeat));
        AddLog(
            "SkillTriggered",
            $"{_players[pending.OwnerSeat].Name} 令 {_players[targetSeat].Name} 进行【武魂】判定。",
            pending.OwnerSeat,
            targetSeat);

        var result = BeginJudgment(
            attack: null,
            targetSeat,
            JudgmentReasons.Wuhun,
            pending.FrameId,
            sourceCard: null,
            JudgmentContinuationKind.Wuhun,
            damageSkill: null,
            sourceSeat: pending.OwnerSeat);
        if (result is { } succeeded)
        {
            CompleteWuhunJudgment(pending, succeeded);
        }
    }

    private void CompleteWuhunJudgment(
        JudgmentResolution judgment,
        bool causesDirectDeath)
    {
        var pending = _pendingDeathSkill ??
            throw new InvalidOperationException("A Wuhun judgment has no death-skill continuation.");
        if (pending.TargetSeat != judgment.TargetSeat ||
            pending.FrameId != judgment.ParentFrameId ||
            pending.OwnerSeat != judgment.SourceSeat)
        {
            throw new InvalidOperationException("The Wuhun judgment does not belong to the active death skill.");
        }

        CompleteWuhunJudgment(pending, causesDirectDeath);
    }

    private void CompleteWuhunJudgment(
        DeathSkillResolution pending,
        bool causesDirectDeath)
    {
        if (!ReferenceEquals(_pendingDeathSkill, pending) ||
            pending.TargetSeat is not { } targetSeat)
        {
            throw new InvalidOperationException("The completed Wuhun judgment is not current.");
        }

        var target = _players[targetSeat];
        if (causesDirectDeath && target.IsAlive && _winner == Winner.None)
        {
            QueueGameEvent(new DirectDeathDeclaredEvent(
                pending.FrameId,
                pending.OwnerSeat,
                SkillKind.Wuhun,
                targetSeat));
            AddLog(
                "DirectDeath",
                $"{target.Name} 的【武魂】判定结果不为【桃】或【桃园结义】，其直接死亡。",
                pending.OwnerSeat,
                targetSeat);
            BeginPlayerDeath(
                pending.FrameId,
                target,
                killer: null,
                attack: null,
                dying: null,
                causingDeathSkill: pending);
            return;
        }

        CompleteWuhunDeathSkill(pending);
    }

    private void CompleteWuhunDeathSkill(DeathSkillResolution pending)
    {
        if (!ReferenceEquals(_pendingDeathSkill, pending) || pending.Skill != SkillKind.Wuhun)
        {
            throw new InvalidOperationException("The completed Wuhun death skill is not current.");
        }

        CompleteDeathSkillResolution(pending);
    }

    private void CompleteDeathSkillResolution(DeathSkillResolution pending)
    {
        if (!ReferenceEquals(_pendingDeathSkill, pending))
        {
            throw new InvalidOperationException("The completed death skill is not current.");
        }

        ReplaceDeathSkillFrame(pending, ResolutionFrameStep.Completed);
        QueueGameEvent(new DeathSkillResolvedEvent(
            pending.FrameId,
            pending.OwnerSeat,
            pending.Skill,
            pending.TargetSeat));
        PopResolutionFrame(pending.FrameId, ResolutionFrameKind.DeathSkill);
        _pendingDeathSkill = pending.Parent;
        pending.Death.ResolvedSkills.Add(pending.Skill);
        ContinueDeathResolution(pending.Death);
    }

    private void CompleteDeathResolution(DeathResolution death)
    {
        if (!ReferenceEquals(_pendingDeath, death))
        {
            throw new InvalidOperationException("The completed death resolution is not current.");
        }

        var victim = _players[death.VictimSeat];
        ClearWuhunMarkerSource(victim, death.FrameId);
        PopResolutionFrame(death.FrameId, ResolutionFrameKind.Death);
        _pendingDeath = death.Parent;

        if (death.Dying is not null)
        {
            CompleteDyingAfterDeath(death.Dying, survived: false);
            return;
        }

        if (death.CausingDeathSkill is not null)
        {
            CompleteWuhunDeathSkill(death.CausingDeathSkill);
            return;
        }

        if (death.CausingProgramCauseDeath is not null)
        {
            CompleteProgramCauseDeath(death.CausingProgramCauseDeath);
            return;
        }

        throw new InvalidOperationException("A death resolution has no continuation.");
    }

    private int GetMarkerSourceCount(
        PlayerRuntime player,
        PlayerMarkerKind marker,
        int skillOwnerSeat) =>
        player.MarkerSourceCounts.GetValueOrDefault((marker, skillOwnerSeat));

    private void ClearWuhunMarkerSource(PlayerRuntime owner, long resolutionId)
    {
        if (!SupportsWuhunDeathTargetSelection)
        {
            return;
        }

        foreach (var player in _players)
        {
            var sourceKey = (PlayerMarkerKind.Nightmare, owner.Seat);
            if (!player.MarkerSourceCounts.Remove(sourceKey, out var sourceCount) || sourceCount <= 0)
            {
                continue;
            }

            var total = player.Markers.GetValueOrDefault(PlayerMarkerKind.Nightmare);
            if (total < sourceCount)
            {
                throw new InvalidOperationException("A Wuhun marker source exceeds its public total.");
            }

            var remaining = total - sourceCount;
            if (remaining == 0)
            {
                player.Markers.Remove(PlayerMarkerKind.Nightmare);
            }
            else
            {
                player.Markers[PlayerMarkerKind.Nightmare] = remaining;
            }
            QueueGameEvent(new PlayerMarkerChangedEvent(
                resolutionId,
                player.Seat,
                PlayerMarkerKind.Nightmare,
                Delta: -sourceCount,
                Count: remaining,
                SkillOwnerSeat: owner.Seat,
                Reason: "skill.wuhun.death-clear"));
        }
    }

    private void ReplaceDeathSkillFrame(
        DeathSkillResolution pending,
        ResolutionFrameStep step)
    {
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == pending.FrameId);
        if (index < 0 || _resolutionStack[index] is not DeathSkillFrame frame)
        {
            throw new InvalidOperationException($"Resolution frame {pending.FrameId} is not a DeathSkill frame.");
        }

        _resolutionStack[index] = frame with
        {
            TargetSeat = pending.TargetSeat,
            Step = step
        };
    }

    private void AssertWuhunDeathSkillInvariant()
    {
        var pending = _pendingDeathSkill ??
            throw new InvalidOperationException("A Wuhun death-skill invariant requires an active continuation.");
        if (pending.Skill != SkillKind.Wuhun ||
            !SupportsWuhunDeathTargetSelection ||
            !ReferenceEquals(_pendingDeath, pending.Death) ||
            _winner != Winner.None ||
            _players[pending.OwnerSeat].IsAlive)
        {
            throw new InvalidOperationException(
                "A Wuhun death-skill continuation has an invalid owner, winner or death parent.");
        }

        var deathIndex = _resolutionStack.FindLastIndex(frame => frame.Id == pending.Death.FrameId);
        if (deathIndex < 0 ||
            deathIndex + 1 >= _resolutionStack.Count ||
            _resolutionStack[deathIndex] is not DeathFrame deathFrame ||
            deathFrame.ParentFrameId != pending.Death.ParentFrameId ||
            deathFrame.VictimSeat != pending.OwnerSeat ||
            _resolutionStack[deathIndex + 1] is not DeathSkillFrame skillFrame ||
            skillFrame.Id != pending.FrameId ||
            skillFrame.ParentFrameId != pending.Death.FrameId ||
            skillFrame.OwnerSeat != pending.OwnerSeat ||
            skillFrame.Skill != SkillKind.Wuhun ||
            !skillFrame.CandidateSeats.SequenceEqual(pending.CandidateSeats) ||
            skillFrame.TargetSeat != pending.TargetSeat)
        {
            throw new InvalidOperationException(
                "A Wuhun death-skill continuation must retain its Death and DeathSkill frames.");
        }

        foreach (var player in _players)
        {
            var attributedTotal = player.MarkerSourceCounts
                .Where(item => item.Key.Marker == PlayerMarkerKind.Nightmare)
                .Sum(item => item.Value);
            if (attributedTotal != player.Markers.GetValueOrDefault(PlayerMarkerKind.Nightmare) ||
                player.MarkerSourceCounts.Values.Any(value => value <= 0))
            {
                throw new InvalidOperationException(
                    "Attributed Nightmare sources must equal the public marker total.");
            }
        }

        if (pending.TargetSeat is null)
        {
            var decision = _pendingDecision;
            if (skillFrame.Step != ResolutionFrameStep.AwaitingResponse ||
                !pending.CandidateSeats.SequenceEqual(GameRules.GetMaximumMarkerCandidates(
                    _players.Select(player => new PlayerMarkerCandidateState(
                        player.Seat,
                        player.IsAlive,
                        GetMarkerSourceCount(
                            player,
                            PlayerMarkerKind.Nightmare,
                            pending.OwnerSeat))))) ||
                decision is not { Kind: DecisionKind.WuhunTarget } ||
                decision.PlayerSeat != pending.OwnerSeat ||
                !decision.ValidTargetSeats.SequenceEqual(pending.CandidateSeats) ||
                !decision.Choices.SelectMany(choice => choice.Targets)
                    .SequenceEqual(pending.CandidateSeats))
            {
                throw new InvalidOperationException(
                    "A Wuhun target cursor must retain its exact maximum-marker prompt.");
            }

            var expectedStatus = _players[pending.OwnerSeat].IsHuman
                ? EngineStatus.AwaitingHumanResponse
                : EngineStatus.Running;
            if (_status != expectedStatus)
            {
                throw new InvalidOperationException(
                    "A Wuhun target prompt status does not match its dead owner.");
            }
            return;
        }

        if (skillFrame.Step != ResolutionFrameStep.ResolvingEffect ||
            _pendingDecision is { Kind: DecisionKind.WuhunTarget } ||
            _pendingJudgment is not
            {
                Continuation: JudgmentContinuationKind.Wuhun,
                ParentFrameId: var judgmentParent,
                TargetSeat: var judgmentTarget,
                SourceSeat: var judgmentSource
            } ||
            judgmentParent != pending.FrameId ||
            judgmentTarget != pending.TargetSeat ||
            judgmentSource != pending.OwnerSeat)
        {
            throw new InvalidOperationException(
                "A selected Wuhun target must retain its active judgment continuation.");
        }
    }

    private sealed class DeathResolution(
        long frameId,
        long parentFrameId,
        int victimSeat,
        int? killerSeat,
        DyingResolution? dying,
        DeathSkillResolution? causingDeathSkill,
        ProgramCauseDeathResolution? causingProgramCauseDeath,
        DeathResolution? parent)
    {
        public long FrameId { get; } = frameId;
        public long ParentFrameId { get; } = parentFrameId;
        public int VictimSeat { get; } = victimSeat;
        public int? KillerSeat { get; } = killerSeat;
        public DyingResolution? Dying { get; } = dying;
        public DeathSkillResolution? CausingDeathSkill { get; } = causingDeathSkill;
        public ProgramCauseDeathResolution? CausingProgramCauseDeath { get; } = causingProgramCauseDeath;
        public DeathResolution? Parent { get; } = parent;
        public HashSet<SkillKind> ResolvedSkills { get; } = [];
    }

    private sealed class DeathSkillResolution(
        long frameId,
        DeathResolution death,
        int ownerSeat,
        SkillKind skill,
        IReadOnlyList<int> candidateSeats,
        DeathSkillResolution? parent)
    {
        public long FrameId { get; } = frameId;
        public DeathResolution Death { get; } = death;
        public int OwnerSeat { get; } = ownerSeat;
        public SkillKind Skill { get; } = skill;
        public IReadOnlyList<int> CandidateSeats { get; } =
            Array.AsReadOnly(candidateSeats.ToArray());
        public int? TargetSeat { get; set; }
        public DeathSkillResolution? Parent { get; } = parent;
    }
}
