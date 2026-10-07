namespace CardGame.Core;

public sealed record ProgramJiezhenConvertedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TargetSeat, IReadOnlyList<string> ReplacedSkillIds) : IGameEvent;
public sealed record ProgramJiezhenRestoredEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TargetSeat, IReadOnlyList<string> RestoredSkillIds, int TakenCardId) : IGameEvent;
public sealed record ProgramZecaiRoundSettledEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int SettledRound) : IGameEvent;
public sealed record ProgramZecaiSkillGrantedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TargetSeat, int RoundNumber, string GrantedSkillId, bool ExtraTurnPended) : IGameEvent;
public sealed record ProgramYinshiDamagePreventedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TurnNumber, int Amount) : IGameEvent;
public sealed record ProgramYinshiJudgmentClaimedEvent(long FrameId, string SkillId, string BindingId,
    int ClaimantSeat, int SubjectSeat, int CardId) : IGameEvent;
public sealed record ProgramRoundTrickUsedEvent(int RoundNumber, int ActorSeat,
    long ActionId, CardKind EffectiveKind) : IGameEvent;

public sealed partial class GameEngine
{
    // 解阵 conversions survive across turns until the owner's next turn start or
    // the converted character's 八卦阵 judgment; replay rebuilds the map while the
    // accepted commands re-execute the converting op.
    private readonly Dictionary<int, JiezhenConversionState> _jiezhenConversions = [];

    private bool TracksRoundTrickUses =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ZecaiRoundSettlement);

    private sealed record JiezhenConversionState(int OwnerSeat, IReadOnlyList<string> ReplacedSkillIds,
        string BazhenGrantId);

    // 解阵 replace: the selected counterpart's replaceable skills (every enabled
    // content skill that is not 锁定技/限定技/主公技) are disabled and 八阵 is granted.
    private SkillProgramStepOutcome JiezhenProgramReplaceSkills(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SelectedTargetSeats is not [var targetSeat] || targetSeat == active.OwnerSeat)
        {
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id), "解阵没有可选的其他角色，剩余结算取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var target = _players[targetSeat];
        if (!target.IsAlive || _jiezhenConversions.ContainsKey(targetSeat))
        {
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id),
                _jiezhenConversions.ContainsKey(targetSeat) ? "该角色的技能已被“解阵”替换，剩余结算取消。" : "解阵的目标角色已失效，剩余结算取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var replaceable = EnabledContentSkillIds(target)
            .Where(skillId => (_contentRegistry!.GetSkill(skillId).Tags &
                (SkillTag.Locked | SkillTag.Limited | SkillTag.Lord)) == 0)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (var skillId in replaceable)
            foreach (var grant in target.SkillGrants.Grants
                         .Where(grant => grant.IsEnabled && grant.SkillId == skillId).ToArray())
                target.SkillGrants.SetEnabled(grant.GrantId, false);
        var sourceSkillId = $"jiezhen:{targetSeat}";
        var bazhenGrantId = $"acquired:{sourceSkillId}:classic:bazhen";
        AcquireRuntimeSkills(target, sourceSkillId, ["classic:bazhen"]);
        _jiezhenConversions[targetSeat] = new(active.OwnerSeat, replaceable, bazhenGrantId);
        AddLog("SkillTriggered",
            replaceable.Length == 0
                ? $"{target.Name} 的技能已被替换为“八阵”。"
                : $"{target.Name} 的【{string.Join("】【", replaceable.Select(GetSkillDisplayName))}】被替换为“八阵”。",
            active.OwnerSeat, targetSeat);
        AdvanceEventRulesAndQueueFact(new ProgramJiezhenConvertedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, targetSeat, replaceable));
        AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.Continue;
    }

    private string GetSkillDisplayName(string skillId)
    {
        try { return _contentRegistry!.GetSkill(skillId).Name; }
        catch (InvalidOperationException) { return skillId; }
    }

    // 解阵 restore: the owner's next turn start or the converted character's
    // 八卦阵 judgment restores the replaced skills, removes the granted 八阵 and
    // then takes one card from that character's zones.
    private SkillProgramStepOutcome JiezhenProgramRestoreSkills(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var window = frame.WindowContext?.Window;
        int? targetSeat = window switch
        {
            SkillProgramTriggerWindow.TurnStartBeforeNormalFlow => _jiezhenConversions
                .Where(pair => pair.Value.OwnerSeat == active.OwnerSeat)
                .Select(pair => (int?)pair.Key).FirstOrDefault(),
            SkillProgramTriggerWindow.JudgmentFinalized when frame.WindowContext?.Judgment is { } judgment &&
                _jiezhenConversions.TryGetValue(judgment.SubjectSeat, out var converted) &&
                converted.OwnerSeat == active.OwnerSeat => judgment.SubjectSeat,
            _ => null
        };
        if (targetSeat is not { } seat) return SkillProgramStepOutcome.Continue;
        var state = _jiezhenConversions[seat];
        var target = _players[seat];
        foreach (var skillId in state.ReplacedSkillIds)
            foreach (var grant in target.SkillGrants.Grants
                         .Where(grant => !grant.IsEnabled && grant.SkillId == skillId &&
                             grant.GrantId != state.BazhenGrantId).ToArray())
                target.SkillGrants.SetEnabled(grant.GrantId, true);
        if (target.SkillGrants.Grants.Any(grant => grant.GrantId == state.BazhenGrantId))
            target.SkillGrants.RemoveGrant(state.BazhenGrantId);
        _jiezhenConversions.Remove(seat);
        var takenCardId = JiezhenTakeOneCardFromZones(active, seat);
        AddLog("SkillTriggered",
            $"{target.Name} 失去“八阵”并获得原技能。", active.OwnerSeat, seat);
        AdvanceEventRulesAndQueueFact(new ProgramJiezhenRestoredEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, seat, state.ReplacedSkillIds, takenCardId));
        if (takenCardId >= 0 && !TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
        return takenCardId >= 0 ? SkillProgramStepOutcome.AwaitChild : SkillProgramStepOutcome.Continue;
    }

    // The gainer draws one uniform-random card across the character's hand,
    // equipment and judgment zones (the established multi-zone 口径).
    private int JiezhenTakeOneCardFromZones(ProgramSkillFrame frame, int targetSeat)
    {
        var target = _players[targetSeat];
        if (!target.IsAlive || !_players[frame.OwnerSeat].IsAlive) return -1;
        var candidates = GetHand(target).Select(card => (Card: card, Location: CardLocation.Hand(target.Seat)))
            .Concat(GetEquipment(target).Select(card => (Card: card, Location: CardLocation.Equipment(target.Seat))))
            .Concat(GetJudgment(target).Select(card => (Card: card, Location: CardLocation.Judgment(target.Seat))))
            .OrderBy(entry => entry.Card.Id).ToArray();
        if (candidates.Length == 0) return -1;
        var pick = candidates[_random.Next(candidates.Length)];
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.jiezhen-take");
        MoveCard(pick.Card, pick.Location, CardLocation.Processing, reason);
        if (!MoveProcessingCardUnlessDestroyed(pick.Card, CardLocation.Hand(frame.OwnerSeat), reason)) return -1;
        AddLog("CardGained",
            $"{_players[frame.OwnerSeat].Name} 获得 {target.Name} 区域里的【{pick.Card.DisplayName}】。",
            frame.OwnerSeat, targetSeat);
        return pick.Card.Id;
    }

    // 择才 settlement: at the owner's turn start after a round boundary, the
    // finished round is settled once; the limited activation offers one other
    // character the 集智 grant until this round ends.
    private SkillProgramStepOutcome ZecaiProgramRoundSettlement(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        var settledRound = _roundNumber - 1;
        if (!owner.IsAlive || settledRound < 1 ||
            CompleteProgramEventHistory().OfType<ProgramZecaiRoundSettledEvent>()
                .Any(fact => fact.OwnerSeat == active.OwnerSeat && fact.SettledRound == settledRound))
            return SkillProgramStepOutcome.Continue;
        PurgeExpiredRoundGrants();
        if (CompleteProgramEventHistory().OfType<ProgramZecaiSkillGrantedEvent>()
                .Any(fact => fact.OwnerSeat == active.OwnerSeat))
            return SkillProgramStepOutcome.Continue;
        var others = _players.Where(player => player.IsAlive && player.Seat != active.OwnerSeat)
            .OrderBy(player => player.Seat).ToArray();
        if (others.Length == 0) return SkillProgramStepOutcome.Continue;
        AdvanceEventRulesAndQueueFact(new ProgramZecaiRoundSettledEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, settledRound));
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var leader = UniqueRoundTrickLeaderSeat(settledRound);
        var choices = others.Select(player => new PromptChoice(
                new ChoiceId($"zecai.frame-{frame.Id}.seat-{player.Seat}"),
                $"令 {player.Name} 获得“集智”直到本轮游戏结束。",
                [], [player.Seat],
                new Dictionary<string, string>
                {
                    ["program-action"] = "zecai-target",
                    ["target-seat"] = player.Seat.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["unique-trick-leader"] =
                        (player.Seat == leader).ToString(System.Globalization.CultureInfo.InvariantCulture)
                }))
            .ToList<PromptChoice>();
        choices.Add(new PromptChoice(
            new ChoiceId($"zecai.frame-{frame.Id}.decline"),
            "不发动【择才】。",
            [], [],
            new Dictionary<string, string> { ["program-action"] = "zecai-decline" }));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】你可以令一名其他角色获得技能“集智”直到本轮游戏结束；若其是上一轮使用锦囊牌数唯一最多的角色，其执行一个额外的回合。",
            [], choices.Where(choice => choice.Targets.Count > 0).SelectMany(choice => choice.Targets).ToArray(),
            active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择获得“集智”的角色", presentation.Description),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    // Grants sourced "acquired:round:{N}:…" stay enabled during round N and are
    // removed once a later round has begun (the "直到下一轮游戏结束" scope).
    private void PurgeExpiredRoundGrants()
    {
        foreach (var player in _players)
            foreach (var grant in player.SkillGrants.Grants
                         .Where(grant => grant.SourceId.StartsWith("acquired:round:", StringComparison.Ordinal))
                         .ToArray())
            {
                var segment = grant.SourceId["acquired:round:".Length..];
                var separator = segment.IndexOf(':');
                if (separator > 0 &&
                    int.TryParse(segment[..separator], System.Globalization.CultureInfo.InvariantCulture, out var round) &&
                    round < _roundNumber)
                    player.SkillGrants.RemoveGrant(grant.GrantId);
            }
    }

    private int UniqueRoundTrickLeaderSeat(int round)
    {
        var counts = CompleteProgramEventHistory().OfType<ProgramRoundTrickUsedEvent>()
            .Where(fact => fact.RoundNumber == round)
            .GroupBy(fact => fact.ActorSeat)
            .ToDictionary(group => group.Key, group => group.Count());
        if (counts.Count == 0) return -1;
        var maximum = counts.Values.Max();
        var leaders = counts.Where(pair => pair.Value == maximum).Select(pair => pair.Key).ToArray();
        return leaders.Length == 1 ? leaders[0] : -1;
    }

    private void ResolveZecaiChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Zecai choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.ZecaiRoundSettlement })
            throw new InvalidOperationException("The Zecai choice does not match the suspended instruction.");
        ClearPendingDecision();
        var active = GetActiveProgramFrame(frame.Id);
        if (selected.Parameters.GetValueOrDefault("program-action") == "zecai-decline")
        {
            AdvanceRuntimeProgram(active.Id);
            return;
        }
        if (!int.TryParse(selected.Parameters.GetValueOrDefault("target-seat"),
                System.Globalization.CultureInfo.InvariantCulture, out var targetSeat) ||
            !_players[targetSeat].IsAlive || targetSeat == active.OwnerSeat)
            throw new InvalidOperationException("The Zecai beneficiary is no longer legal.");
        var round = _roundNumber;
        AcquireRuntimeSkills(_players[targetSeat], $"round:{round}:{active.SkillId}:{GetProgramBindingId(active)}",
            ["classic:jizhi"]);
        var extraTurn = UniqueRoundTrickLeaderSeat(round - 1) == targetSeat;
        if (extraTurn) PendProgramExtraTurn(active, targetSeat);
        AddLog("SkillTriggered",
            extraTurn
                ? $"{_players[targetSeat].Name} 获得“集智”直到本轮游戏结束，并执行一个额外的回合。"
                : $"{_players[targetSeat].Name} 获得“集智”直到本轮游戏结束。",
            active.OwnerSeat, targetSeat);
        AdvanceEventRulesAndQueueFact(new ProgramZecaiSkillGrantedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, targetSeat, round, "classic:jizhi", extraTurn));
        AdvanceRuntimeProgram(active.Id);
    }

    // 隐世 ①: the first damage each turn that no colored game card caused is
    // prevented. In this ruleset every game card carries a suit, so the
    // expressible scope is "damage without a source game card".
    private SkillProgramStepOutcome YinshiProgramPreventSourcelessDamage(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var attempt = CurrentDamageAttempt;
        if (attempt is null || attempt.TargetSeat != active.OwnerSeat ||
            attempt.IsSourceLess == false && attempt.Card is not null)
            return SkillProgramStepOutcome.Continue;
        if (CompleteProgramEventHistory().OfType<ProgramYinshiDamagePreventedEvent>()
                .Any(fact => fact.OwnerSeat == active.OwnerSeat && fact.TurnNumber == _turnNumber))
            return SkillProgramStepOutcome.Continue;
        if (_resolutionStack.Count < 2 || _resolutionStack[^2] is not BeforeDamageProgramWindowFrame parent ||
            parent.Id != frame.WindowContext?.ParentFrameId || parent.Prevented)
            return SkillProgramStepOutcome.Continue;
        AdvanceEventRulesAndQueueFact(new ProgramYinshiDamagePreventedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, _turnNumber, frame.WindowContext?.Amount ?? 0));
        AddLog("SkillTriggered",
            $"【隐世】防止了 {_players[active.OwnerSeat].Name} 本回合首次受到的无来源伤害。",
            active.OwnerSeat, active.OwnerSeat);
        PreventProgramCurrentDamage(frame);
        return SkillProgramStepOutcome.Continue;
    }

    // 隐世 ②: when any character resolves a 八卦阵 judgment, the owner claims the
    // effective judgment card.
    private SkillProgramStepOutcome YinshiProgramClaimBaguaJudgmentCard(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var context = frame.WindowContext;
        var judgment = context?.Judgment;
        var pending = ActiveJudgment;
        var active = GetActiveProgramFrame(frame.Id);
        if (context is null || judgment is null || pending is null ||
            context.Window != SkillProgramTriggerWindow.JudgmentFinalized ||
            pending.Id != judgment.JudgmentFrameId ||
            GetJudgmentCard(pending) is not { } card || card.Id != judgment.CardId ||
            active.PendingMovementContinuation is not null || !_players[active.OwnerSeat].IsAlive)
            return SkillProgramStepOutcome.Continue;
        var from = CardLocation.Judgment(judgment.SubjectSeat);
        if (_cardZones.GetLocation(card.Id) != from) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(active with
        {
            PendingMovementContinuation = new ProgramMovementContinuation(active.OwnerSeat, 0, null)
        });
        MoveCard(card, from, CardLocation.Hand(active.OwnerSeat),
            new CardMoveReason($"skill-program.{active.SkillId}.yinshi-claim"));
        AddLog("CardGained",
            $"{_players[active.OwnerSeat].Name} 获得【八卦阵】生效的判定牌【{card.DisplayName}】。",
            active.OwnerSeat, judgment.SubjectSeat);
        AdvanceEventRulesAndQueueFact(new ProgramYinshiJudgmentClaimedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, judgment.SubjectSeat, card.Id));
        if (!TryBeginCardsMovedProgramWindow())
            ReturnRuntimeProgramMovement(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private void ObserveRoundTrickUse(IGameEvent payload)
    {
        if (!TracksRoundTrickUses) return;
        CardActionContext? action = payload switch
        {
            CardUseDeclaredEvent declared => _resolutionStack.OfType<CardUseFrame>()
                .SingleOrDefault(frame => frame.Id == declared.ResolutionId)?.Action,
            CardActionAcceptedEvent accepted when accepted.Action.Type == CardActionType.Response &&
                accepted.Action.EffectiveKind == CardKind.Nullification &&
                accepted.Action.ActorSeat == accepted.Action.ProviderSeat &&
                accepted.Action.ResponderSeat == accepted.Action.ActorSeat &&
                accepted.Action.RequesterSeat is null &&
                _resolutionStack.OfType<NullificationWindowFrame>().Any(window =>
                    _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Id == window.ParentFrameId)
                        ?.Action?.ActionId == accepted.Action.ParentActionId) => accepted.Action,
            _ => null
        };
        if (action is null || GetProgramCardCategory(action.EffectiveKind) != SkillProgramCardCategory.Trick ||
            CompleteProgramEventHistory().OfType<ProgramRoundTrickUsedEvent>()
                .Any(fact => fact.ActionId == action.ActionId)) return;
        AdvanceEventRulesAndQueueFact(new ProgramRoundTrickUsedEvent(
            _roundNumber, action.ActorSeat, action.ActionId, action.EffectiveKind));
    }

    private PromptChoice SelectAiZecaiChoice(PendingDecision decision)
    {
        var leader = decision.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("unique-trick-leader") == "True");
        if (leader is not null) return leader;
        return decision.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "zecai-decline");
    }

    private sealed partial class ProgramSkillHost : IHuangChengYanProgramHost
    {
        public SkillProgramStepOutcome JiezhenReplaceSkills(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.JiezhenProgramReplaceSkills(frame, effect);
        public SkillProgramStepOutcome JiezhenRestoreSkills(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.JiezhenProgramRestoreSkills(frame, effect);
        public SkillProgramStepOutcome ZecaiRoundSettlement(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZecaiProgramRoundSettlement(frame, effect);
        public SkillProgramStepOutcome YinshiPreventSourcelessDamage(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.YinshiProgramPreventSourcelessDamage(frame, effect);
        public SkillProgramStepOutcome YinshiClaimBaguaJudgmentCard(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.YinshiProgramClaimBaguaJudgmentCard(frame, effect);
    }
}
