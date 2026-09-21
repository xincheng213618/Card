namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string AnxuSkillId = "classic:anxu";
    private const string ZhuiyiSkillId = "classic:zhuiyi";

    private AnxuResolution? _pendingAnxu;

    private bool UsesFormalBuLianShi =>
        HasClassicGeneralPackage(new Version(1, 90, 0));

    private bool CanUseAnxu(PlayerRuntime owner) =>
        UsesFormalBuLianShi &&
        HasRuntimeSkill(owner, AnxuSkillId) &&
        owner.IsAlive &&
        _phase == TurnPhase.Play &&
        _currentSeat == owner.Seat &&
        !owner.UsedActiveSkillKinds.Contains(SkillKind.Anxu) &&
        HasAnxuTargetPair(owner);

    private bool HasAnxuTargetPair(PlayerRuntime owner)
    {
        var others = _players.Where(player => player.IsAlive && player.Seat != owner.Seat).ToArray();
        for (var first = 0; first < others.Length; first++)
        {
            for (var second = first + 1; second < others.Length; second++)
            {
                if (GetHand(others[first]).Count != GetHand(others[second]).Count)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private IReadOnlySet<int> GetAnxuTargetSeats(PlayerRuntime owner)
    {
        var others = _players.Where(player => player.IsAlive && player.Seat != owner.Seat).ToArray();
        return others
            .Where(candidate => others.Any(other =>
                other.Seat != candidate.Seat &&
                GetHand(other).Count != GetHand(candidate).Count))
            .Select(player => player.Seat)
            .ToHashSet();
    }

    private bool IsAnxuTargetPair(IReadOnlyList<int> targetSeats) =>
        targetSeats.Count == 2 &&
        GetHand(_players[targetSeats[0]]).Count != GetHand(_players[targetSeats[1]]).Count;

    private CommandResult SubmitAnxuPromptAnswer(PromptChoice selected)
    {
        if (_pendingAnxu is not { } pending ||
            _pendingDecision is not { Kind: DecisionKind.Anxu } decision ||
            decision.PlayerSeat != pending.ReceiverSeat ||
            selected.Parameters.GetValueOrDefault("action") != "anxu-hand-slot" ||
            !selected.Parameters.TryGetValue("slot-index", out var slotText) ||
            !int.TryParse(
                slotText,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var slot) ||
            selected.Cards.Count != 0 ||
            !selected.Targets.SequenceEqual([pending.DonorSeat]) ||
            slot < 0 || slot >= pending.DonorCardIds.Count)
        {
            return Reject(CommandErrorCode.InvalidChoice,
                "The choice is malformed for the Anxu hidden-hand selection.");
        }

        return Accept(() => HumanAnxuCore(
            pending,
            slot,
            _options.AdvanceAfterHumanCommands));
    }

    private EngineRunResult HumanAnxuCore(
        AnxuResolution pending,
        int slot,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Anxu);
        ResolveAnxuSelection(pending, slot);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private void BeginAnxuSelection(
        long frameId,
        PlayerRuntime owner,
        IReadOnlyList<int> targetSeats)
    {
        if (!CanUseAnxu(owner) || !IsAnxuTargetPair(targetSeats) || _pendingAnxu is not null)
        {
            throw new InvalidOperationException("The selected Anxu targets are no longer legal.");
        }

        var first = _players[targetSeats[0]];
        var second = _players[targetSeats[1]];
        var firstHandCount = GetHand(first).Count;
        var receiver = firstHandCount < GetHand(second).Count ? first : second;
        var donor = receiver.Seat == first.Seat ? second : first;
        var donorCardIds = GetHand(donor).Select(card => card.Id).ToArray();
        if (donorCardIds.Length == 0)
        {
            throw new InvalidOperationException("Anxu's higher-hand target has no transferable hand card.");
        }

        owner.UsedActiveSkillKinds.Add(SkillKind.Anxu);
        var pending = new AnxuResolution(
            frameId,
            owner.Seat,
            receiver.Seat,
            donor.Seat,
            donorCardIds);
        _pendingAnxu = pending;
        SetActiveSkillFrameStep(frameId, ResolutionFrameStep.AwaitingResponse);

        var choices = donorCardIds.Select((_, slot) => new PromptChoice(
            new ChoiceId($"anxu.hand-slot-{slot}.resolution-{frameId}"),
            $"获得 {donor.Name} 的第 {slot + 1} 个暗置手牌牌位并展示。",
            [],
            [donor.Seat],
            new Dictionary<string, string>
            {
                ["action"] = "anxu-hand-slot",
                ["slot-index"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.Anxu,
            receiver.Seat,
            $"{owner.Name} 对你与 {donor.Name} 发动【安恤】：请选择获得其一张暗置手牌。",
            [],
            [donor.Seat],
            SourceSeat: owner.Seat)
        {
            PromptId = receiver.IsHuman ? CreatePromptId() : default,
            IsPrivate = true,
            TargetSeat = donor.Seat,
            Choices = choices
        };
        _status = receiver.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AddLog(
            "ActiveSkill",
            $"{owner.Name} 对 {receiver.Name} 与 {donor.Name} 发动【安恤】，由手牌较少的 {receiver.Name} 选择获得一张暗置手牌。",
            owner.Seat,
            receiver.Seat);
    }

    private bool IsAiAnxuPending() =>
        _pendingAnxu is { } pending &&
        _pendingDecision is { Kind: DecisionKind.Anxu, PlayerSeat: var responderSeat } &&
        responderSeat == pending.ReceiverSeat &&
        !_players[responderSeat].IsHuman;

    private void ResolvePendingAiAnxu()
    {
        var pending = _pendingAnxu ??
            throw new InvalidOperationException("There is no AI Anxu selection to resolve.");
        var decision = _pendingDecision ??
            throw new InvalidOperationException("The AI Anxu selection has no prompt.");
        var (choiceId, thought) = _aiBrains[pending.ReceiverSeat].ChooseTargetCardSlot(
            CreateSnapshot(pending.ReceiverSeat),
            pending.DonorSeat,
            LegalActionKind.UseSkill,
            decision.Choices,
            ++_thoughtSequence,
            "安恤");
        AddThought(thought);
        var selected = decision.Choices.Single(choice => choice.Id == choiceId);
        var slot = int.Parse(
            selected.Parameters["slot-index"],
            System.Globalization.CultureInfo.InvariantCulture);
        ResolveAnxuSelection(pending, slot);
        PublishState();
    }

    private void ResolveAnxuSelection(AnxuResolution pending, int slot)
    {
        if (!ReferenceEquals(_pendingAnxu, pending) ||
            _pendingDecision is not { Kind: DecisionKind.Anxu, PlayerSeat: var responderSeat } ||
            responderSeat != pending.ReceiverSeat ||
            slot < 0 || slot >= pending.DonorCardIds.Count)
        {
            throw new InvalidOperationException("The Anxu hidden-hand selection is not current.");
        }

        var owner = _players[pending.OwnerSeat];
        var receiver = _players[pending.ReceiverSeat];
        var donor = _players[pending.DonorSeat];
        var cardId = pending.DonorCardIds[slot];
        var card = GetHand(donor).SingleOrDefault(candidate => candidate.Id == cardId) ??
            throw new InvalidOperationException("The selected Anxu hand slot is no longer available.");

        ClearPendingDecision();
        SetActiveSkillFrameStep(pending.FrameId, ResolutionFrameStep.ResolvingEffect);
        MoveCard(card, CardLocation.Hand(donor.Seat), CardLocation.Processing, CardMoveReasons.AnxuTransfer);
        MoveCard(card, CardLocation.Processing, CardLocation.Hand(receiver.Seat), CardMoveReasons.AnxuTransfer);
        QueueGameEvent(new CardsRevealedEvent(pending.FrameId, [ToSnapshot(card)]));

        var effectiveSuit = EffectiveSuit(receiver, card);
        var drawn = effectiveSuit == Suit.Spade
            ? []
            : DrawCards(owner, 1, log: true, reason: CardMoveReasons.AnxuDraw);
        QueueGameEvent(new AnxuResolvedEvent(
            pending.FrameId,
            owner.Seat,
            receiver.Seat,
            donor.Seat,
            card.Id,
            card.Kind,
            effectiveSuit,
            drawn.Count > 0));
        AddLog(
            "SkillTriggered",
            drawn.Count > 0
                ? $"{receiver.Name} 通过【安恤】获得并展示【{card.DisplayName}】；其有效花色不是黑桃，{owner.Name} 摸一张牌。"
                : $"{receiver.Name} 通过【安恤】获得并展示【{card.DisplayName}】；{owner.Name} 未因此摸牌。",
            owner.Seat,
            receiver.Seat);

        _pendingAnxu = null;
        SetActiveSkillFrameStep(pending.FrameId, ResolutionFrameStep.Completed);
        QueueGameEvent(new ActiveSkillResolvedEvent(
            pending.FrameId,
            owner.Seat,
            SkillKind.Anxu,
            ActiveSkillEffectKind.TransferHandBetweenUnequalTargets));
        PopResolutionFrame(pending.FrameId, ResolutionFrameKind.ActiveSkill);
    }

    private bool TryBeginZhuiyiDeathTargetSelection(
        DeathResolution death,
        PlayerRuntime owner)
    {
        if (!UsesFormalBuLianShi ||
            death.ResolvedSkills.Contains(SkillKind.Zhuiyi) ||
            _winner != Winner.None ||
            !HasRuntimeSkill(owner, ZhuiyiSkillId))
        {
            return false;
        }

        var candidates = _players
            .Where(player =>
                player.IsAlive &&
                player.Seat != owner.Seat &&
                player.Seat != death.KillerSeat)
            .Select(player => player.Seat)
            .Order()
            .ToArray();
        if (candidates.Length == 0)
        {
            death.ResolvedSkills.Add(SkillKind.Zhuiyi);
            return false;
        }

        var frameId = ++_resolutionSequence;
        _resolutionStack.Add(new DeathSkillFrame(
            frameId,
            death.FrameId,
            owner.Seat,
            SkillKind.Zhuiyi,
            candidates));
        var pending = new DeathSkillResolution(
            frameId,
            death,
            owner.Seat,
            SkillKind.Zhuiyi,
            candidates,
            _pendingDeathSkill);
        _pendingDeathSkill = pending;
        QueueGameEvent(new DeathSkillStartedEvent(
            frameId,
            death.FrameId,
            owner.Seat,
            SkillKind.Zhuiyi,
            candidates));
        AddLog(
            "SkillTriggered",
            $"{owner.Name} 死亡时可以发动【追忆】，令除杀死其角色外的一名其他角色摸三张牌并回复1点体力。",
            owner.Seat);

        _pendingDecision = CreateZhuiyiTargetDecision(pending);
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return true;
    }

    private PendingDecision CreateZhuiyiTargetDecision(DeathSkillResolution pending)
    {
        var owner = _players[pending.OwnerSeat];
        var choices = pending.CandidateSeats.Select(targetSeat => new PromptChoice(
            new ChoiceId($"zhuiyi.target-{targetSeat}.resolution-{pending.FrameId}"),
            $"对 {_players[targetSeat].Name} 发动【追忆】：其摸三张牌，然后回复1点体力。",
            [],
            [targetSeat],
            new Dictionary<string, string> { ["action"] = "zhuiyi-target" }))
            .Append(new PromptChoice(
                new ChoiceId($"zhuiyi.skip.resolution-{pending.FrameId}"),
                "不发动【追忆】。",
                [],
                [],
                new Dictionary<string, string> { ["action"] = "zhuiyi-skip" }))
            .ToArray();
        return new PendingDecision(
            DecisionKind.ZhuiyiTarget,
            owner.Seat,
            "你已经死亡，是否发动【追忆】？不能选择杀死你的角色。",
            [],
            pending.CandidateSeats,
            SourceSeat: owner.Seat)
        {
            PromptId = owner.IsHuman ? CreatePromptId() : default,
            IsPrivate = true,
            Choices = choices
        };
    }

    private CommandResult SubmitZhuiyiTargetAnswer(PromptChoice selected)
    {
        var pending = _pendingDeathSkill;
        if (pending is null || pending.Skill != SkillKind.Zhuiyi ||
            _pendingDecision is not { Kind: DecisionKind.ZhuiyiTarget } ||
            selected.Cards.Count != 0)
        {
            return Reject(CommandErrorCode.InvalidChoice,
                "The choice is malformed for the Zhuiyi death target window.");
        }

        var action = selected.Parameters.GetValueOrDefault("action");
        int? targetSeat = action switch
        {
            "zhuiyi-skip" when selected.Targets.Count == 0 => null,
            "zhuiyi-target" when selected.Targets.Count == 1 &&
                pending.CandidateSeats.Contains(selected.Targets[0]) => selected.Targets[0],
            _ => -1
        };
        if (targetSeat == -1)
        {
            return Reject(CommandErrorCode.InvalidChoice,
                "The selected Zhuiyi target is not a current candidate.");
        }

        return Accept(() => HumanZhuiyiTargetCore(
            pending,
            targetSeat,
            _options.AdvanceAfterHumanCommands));
    }

    private EngineRunResult HumanZhuiyiTargetCore(
        DeathSkillResolution pending,
        int? targetSeat,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.ZhuiyiTarget);
        ResolveZhuiyiTargetSelection(pending, targetSeat);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private bool IsAiZhuiyiTargetPending() =>
        _pendingDeathSkill is { Skill: SkillKind.Zhuiyi } pending &&
        _pendingDecision is { Kind: DecisionKind.ZhuiyiTarget, PlayerSeat: var ownerSeat } &&
        ownerSeat == pending.OwnerSeat &&
        !_players[ownerSeat].IsHuman;

    private void ResolvePendingAiZhuiyiTarget()
    {
        var pending = _pendingDeathSkill ??
            throw new InvalidOperationException("There is no AI Zhuiyi target choice to resolve.");
        var (targetSeat, thought) = _aiBrains[pending.OwnerSeat].ChooseZhuiyiTarget(
            CreateSnapshot(pending.OwnerSeat),
            pending.CandidateSeats,
            ++_thoughtSequence);
        AddThought(thought);
        ResolveZhuiyiTargetSelection(pending, targetSeat);
        PublishState();
    }

    private void ResolveZhuiyiTargetSelection(
        DeathSkillResolution pending,
        int? targetSeat)
    {
        if (!ReferenceEquals(_pendingDeathSkill, pending) ||
            pending.Skill != SkillKind.Zhuiyi ||
            _pendingDecision is not { Kind: DecisionKind.ZhuiyiTarget, PlayerSeat: var ownerSeat } ||
            ownerSeat != pending.OwnerSeat ||
            targetSeat is { } selected &&
            (!pending.CandidateSeats.Contains(selected) || !_players[selected].IsAlive))
        {
            throw new InvalidOperationException("The Zhuiyi target is not legal for the current death-skill window.");
        }

        ClearPendingDecision();
        pending.TargetSeat = targetSeat;
        ReplaceDeathSkillFrame(pending, ResolutionFrameStep.ResolvingEffect);
        if (targetSeat is null)
        {
            AddLog("SkillSkipped", $"{_players[pending.OwnerSeat].Name} 未发动【追忆】。", pending.OwnerSeat);
            CompleteDeathSkillResolution(pending);
            return;
        }

        var target = _players[targetSeat.Value];
        QueueGameEvent(new DeathSkillTargetSelectedEvent(
            pending.FrameId,
            pending.OwnerSeat,
            SkillKind.Zhuiyi,
            target.Seat));
        var drawn = DrawCards(target, 3, log: true, reason: CardMoveReasons.ZhuiyiDraw);
        var recovered = Math.Min(1, Math.Max(0, target.MaxHp - target.Hp));
        if (recovered > 0)
        {
            var recoveryFrameId = BeginRecovery(
                pending.FrameId,
                pending.OwnerSeat,
                target.Seat,
                recovered);
            try
            {
                target.Hp += recovered;
                QueueGameEvent(new RecoveryAppliedEvent(
                    pending.OwnerSeat,
                    target.Seat,
                    recovered,
                    target.Hp));
            }
            finally
            {
                PopResolutionFrame(recoveryFrameId, ResolutionFrameKind.Recovery);
            }
        }

        QueueGameEvent(new ZhuiyiResolvedEvent(
            pending.FrameId,
            pending.OwnerSeat,
            target.Seat,
            drawn.Count,
            recovered,
            target.Hp));
        AddLog(
            "SkillTriggered",
            recovered > 0
                ? $"{_players[pending.OwnerSeat].Name} 对 {target.Name} 发动【追忆】：其摸 {drawn.Count} 张牌并回复 {recovered} 点体力。"
                : $"{_players[pending.OwnerSeat].Name} 对 {target.Name} 发动【追忆】：其摸 {drawn.Count} 张牌；因体力已满未回复。",
            pending.OwnerSeat,
            target.Seat);
        CompleteDeathSkillResolution(pending);
    }

    private void ContinueDeathResolution(DeathResolution death)
    {
        if (!ReferenceEquals(_pendingDeath, death))
        {
            throw new InvalidOperationException("The continued death resolution is not current.");
        }

        var owner = _players[death.VictimSeat];
        if (TryBeginZhuiyiDeathTargetSelection(death, owner) ||
            (!death.ResolvedSkills.Contains(SkillKind.Wuhun) &&
             TryBeginWuhunDeathTargetSelection(death, owner)))
        {
            return;
        }

        CompleteDeathResolution(death);
    }

    private void AssertDeathSkillInvariant()
    {
        switch (_pendingDeathSkill?.Skill)
        {
            case SkillKind.Wuhun:
                AssertWuhunDeathSkillInvariant();
                break;
            case SkillKind.Zhuiyi:
                AssertZhuiyiDeathSkillInvariant();
                break;
            default:
                throw new InvalidOperationException("The active death-skill continuation has no supported skill.");
        }
    }

    private void AssertZhuiyiDeathSkillInvariant()
    {
        var pending = _pendingDeathSkill ??
            throw new InvalidOperationException("A Zhuiyi invariant requires an active continuation.");
        if (!UsesFormalBuLianShi ||
            pending.Skill != SkillKind.Zhuiyi ||
            !ReferenceEquals(_pendingDeath, pending.Death) ||
            _winner != Winner.None ||
            _players[pending.OwnerSeat].IsAlive)
        {
            throw new InvalidOperationException(
                "A Zhuiyi continuation has an invalid owner, winner or death parent.");
        }

        var deathIndex = _resolutionStack.FindLastIndex(frame => frame.Id == pending.Death.FrameId);
        if (deathIndex < 0 ||
            deathIndex + 1 >= _resolutionStack.Count ||
            _resolutionStack[deathIndex] is not DeathFrame deathFrame ||
            deathFrame.VictimSeat != pending.OwnerSeat ||
            _resolutionStack[deathIndex + 1] is not DeathSkillFrame skillFrame ||
            skillFrame.Id != pending.FrameId ||
            skillFrame.ParentFrameId != pending.Death.FrameId ||
            skillFrame.OwnerSeat != pending.OwnerSeat ||
            skillFrame.Skill != SkillKind.Zhuiyi ||
            !skillFrame.CandidateSeats.SequenceEqual(pending.CandidateSeats) ||
            pending.TargetSeat is not null ||
            skillFrame.TargetSeat is not null ||
            skillFrame.Step != ResolutionFrameStep.AwaitingResponse ||
            _pendingDecision is not
            {
                Kind: DecisionKind.ZhuiyiTarget,
                IsPrivate: true,
                PlayerSeat: var responderSeat
            } decision ||
            responderSeat != pending.OwnerSeat ||
            !decision.ValidTargetSeats.SequenceEqual(pending.CandidateSeats) ||
            decision.Choices.Count != pending.CandidateSeats.Count + 1)
        {
            throw new InvalidOperationException(
                "A Zhuiyi continuation must retain its exact optional target prompt and death frames.");
        }

        var expectedCandidates = _players
            .Where(player =>
                player.IsAlive &&
                player.Seat != pending.OwnerSeat &&
                player.Seat != pending.Death.KillerSeat)
            .Select(player => player.Seat)
            .Order();
        if (!pending.CandidateSeats.SequenceEqual(expectedCandidates) ||
            _status != (_players[pending.OwnerSeat].IsHuman
                ? EngineStatus.AwaitingHumanResponse
                : EngineStatus.Running))
        {
            throw new InvalidOperationException("Zhuiyi candidates or prompt status drifted.");
        }
    }

    private void AssertAnxuInvariant()
    {
        if (_pendingAnxu is not { } pending)
        {
            if (_pendingDecision?.Kind == DecisionKind.Anxu)
            {
                throw new InvalidOperationException("An Anxu prompt cannot exist without its continuation.");
            }
            return;
        }

        if (!UsesFormalBuLianShi ||
            _resolutionStack.OfType<ActiveSkillFrame>().LastOrDefault() is not
            {
                Id: var frameId,
                Skill: SkillKind.Anxu,
                Effect: ActiveSkillEffectKind.TransferHandBetweenUnequalTargets,
                Step: ResolutionFrameStep.AwaitingResponse
            } ||
            frameId != pending.FrameId ||
            _pendingDecision is not
            {
                Kind: DecisionKind.Anxu,
                IsPrivate: true,
                PlayerSeat: var responderSeat,
                TargetSeat: var donorSeat
            } decision ||
            responderSeat != pending.ReceiverSeat ||
            donorSeat != pending.DonorSeat ||
            !pending.DonorCardIds.SequenceEqual(GetHand(_players[pending.DonorSeat]).Select(card => card.Id)) ||
            decision.Choices.Count != pending.DonorCardIds.Count ||
            decision.Choices.Any(choice =>
                choice.Cards.Count != 0 ||
                !choice.Targets.SequenceEqual([pending.DonorSeat]) ||
                choice.Parameters.GetValueOrDefault("action") != "anxu-hand-slot"))
        {
            throw new InvalidOperationException(
                "Anxu must retain an opaque receiver-owned donor-hand prompt and active-skill frame.");
        }
    }

    private sealed class AnxuResolution(
        long frameId,
        int ownerSeat,
        int receiverSeat,
        int donorSeat,
        IReadOnlyList<int> donorCardIds)
    {
        public long FrameId { get; } = frameId;
        public int OwnerSeat { get; } = ownerSeat;
        public int ReceiverSeat { get; } = receiverSeat;
        public int DonorSeat { get; } = donorSeat;
        public IReadOnlyList<int> DonorCardIds { get; } =
            Array.AsReadOnly(donorCardIds.ToArray());
    }
}
