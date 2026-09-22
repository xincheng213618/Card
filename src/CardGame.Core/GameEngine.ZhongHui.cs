namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string QuanjiSkillId = "classic:quanji";
    private const string ZiliSkillId = "classic:zili";
    private const string PaiyiSkillId = "classic:paiyi";

    private ZiliResolution? _pendingZili;

    private bool UsesFormalZhongHui =>
        HasClassicGeneralPackage(new Version(1, 86, 0));

    private CommandResult SubmitQuanjiPromptAnswer(PromptChoice selected)
    {
        if (_pendingDamageSkill is not { Effect: DamageSkillEffectKind.StoreAuthority } ||
            _pendingDecision is not { Kind: DecisionKind.Quanji })
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的权计结算。");
        }

        return Accept(() => HumanQuanjiCore(selected, _options.AdvanceAfterHumanCommands));
    }

    private EngineRunResult HumanQuanjiCore(PromptChoice selected, bool advanceToHumanBoundary)
    {
        ResolveQuanjiChoice(selected);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private CommandResult SubmitZiliPromptAnswer(PromptChoice selected)
    {
        if (_pendingZili is null || _pendingDecision is not { Kind: DecisionKind.Zili })
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的自立觉醒选择。");
        }

        return Accept(() => HumanZiliCore(selected, _options.AdvanceAfterHumanCommands));
    }

    private EngineRunResult HumanZiliCore(PromptChoice selected, bool advanceToHumanBoundary)
    {
        ResolveZiliChoice(selected);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private IReadOnlyList<Card> GetAuthority(CharacterState player) =>
        _cardZones.CardsAt(CardLocation.Authority(player.Seat));

    private PendingDecision CreateQuanjiDecision(DamageSkillResolution pending)
    {
        if (pending.Effect != DamageSkillEffectKind.StoreAuthority ||
            pending.QuanjiDamagePoint >= pending.Attack.DamageAmount)
        {
            throw new InvalidOperationException("Quanji has no remaining damage point to resolve.");
        }

        var owner = _players[pending.OwnerSeat];
        var point = pending.QuanjiDamagePoint + 1;
        return new PendingDecision(
            DecisionKind.Quanji,
            owner.Seat,
            $"你受到的第 {point}/{pending.Attack.DamageAmount} 点伤害已结算，是否发动【权计】？",
            [],
            [],
            pending.SourceSeat,
            pending.EffectiveCardKind)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            Choices =
            [
                new PromptChoice(
                    new ChoiceId($"quanji.point-{point}.use"),
                    "发动【权计】：摸一张牌，然后将一张手牌置为“权”。",
                    [],
                    [],
                    new Dictionary<string, string>
                    {
                        ["action"] = "quanji-use",
                        ["damage-point"] = point.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }),
                new PromptChoice(
                    new ChoiceId($"quanji.point-{point}.skip"),
                    "不发动【权计】。",
                    [],
                    [],
                    new Dictionary<string, string>
                    {
                        ["action"] = "quanji-skip",
                        ["damage-point"] = point.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    })
            ]
        };
    }

    private PendingDecision CreateQuanjiCardDecision(DamageSkillResolution pending)
    {
        var owner = _players[pending.OwnerSeat];
        var point = pending.QuanjiDamagePoint + 1;
        var hand = GetHand(owner).OrderBy(card => card.Id).ToArray();
        if (hand.Length == 0)
        {
            throw new InvalidOperationException("Quanji drew no selectable hand card.");
        }

        return new PendingDecision(
            DecisionKind.Quanji,
            owner.Seat,
            $"【权计】第 {point}/{pending.Attack.DamageAmount} 次：选择一张手牌置为“权”。",
            hand.Select(card => card.Id).ToArray(),
            [],
            pending.SourceSeat,
            pending.EffectiveCardKind)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            Choices = hand.Select(card => new PromptChoice(
                new ChoiceId($"quanji.point-{point}.store-{card.Id}"),
                $"将【{card.DisplayName}】置为“权”。",
                [card.Id],
                [],
                new Dictionary<string, string>
                {
                    ["action"] = "quanji-store",
                    ["damage-point"] = point.ToString(System.Globalization.CultureInfo.InvariantCulture)
                })).ToArray()
        };
    }

    private void ResolveQuanjiChoice(PromptChoice selected)
    {
        var pending = _pendingDamageSkill ??
            throw new InvalidOperationException("There is no Quanji damage skill pending.");
        if (pending.Effect != DamageSkillEffectKind.StoreAuthority ||
            _pendingDecision is not { Kind: DecisionKind.Quanji } decision ||
            decision.PlayerSeat != pending.OwnerSeat)
        {
            throw new InvalidOperationException("There is no Quanji choice to resolve.");
        }

        var action = selected.Parameters.GetValueOrDefault("action");
        if (action == "quanji-skip" && selected.Cards.Count == 0 && selected.Targets.Count == 0 &&
            !pending.QuanjiAwaitingCardSelection)
        {
            ClearPendingDecision();
            CompleteQuanjiPoint(pending, used: false, drawnCardId: null, authorityCardId: null);
            return;
        }

        if (action == "quanji-use" && selected.Cards.Count == 0 && selected.Targets.Count == 0 &&
            !pending.QuanjiAwaitingCardSelection)
        {
            ClearPendingDecision();
            var owner = _players[pending.OwnerSeat];
            var drawn = DrawOne(owner, CardMoveReasons.QuanjiDraw);
            pending.QuanjiDrawnCardId = drawn?.Id;
            if (drawn is not null)
            {
                QueueGameEvent(new DamageSkillCardsDrawnEvent(
                    pending.DamageFrameId,
                    owner.Seat,
                    SkillKind.Quanji,
                    [drawn.Id]));
            }

            if (GetHand(owner).Count == 0)
            {
                CompleteQuanjiPoint(pending, used: false, drawn?.Id, authorityCardId: null);
                return;
            }

            pending.QuanjiAwaitingCardSelection = true;
            _pendingDecision = CreateQuanjiCardDecision(pending);
            _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
            return;
        }

        if (action == "quanji-store" && selected.Cards.Count == 1 && selected.Targets.Count == 0 &&
            pending.QuanjiAwaitingCardSelection && decision.ValidCardIds.Contains(selected.Cards[0]))
        {
            var owner = _players[pending.OwnerSeat];
            var card = GetHand(owner).SingleOrDefault(candidate => candidate.Id == selected.Cards[0]) ??
                throw new InvalidOperationException("The selected Quanji card is no longer in hand.");
            ClearPendingDecision();
            MoveCard(card, CardLocation.Hand(owner.Seat), CardLocation.Authority(owner.Seat), CardMoveReasons.QuanjiStore);
            CompleteQuanjiPoint(pending, used: true, pending.QuanjiDrawnCardId, card.Id);
            return;
        }

        throw new InvalidOperationException("The Quanji choice is malformed.");
    }

    private void CompleteQuanjiPoint(
        DamageSkillResolution pending,
        bool used,
        int? drawnCardId,
        int? authorityCardId)
    {
        var owner = _players[pending.OwnerSeat];
        var point = pending.QuanjiDamagePoint + 1;
        pending.QuanjiUsedAny |= used;
        pending.QuanjiAwaitingCardSelection = false;
        pending.QuanjiDrawnCardId = null;
        pending.QuanjiDamagePoint++;
        QueueGameEvent(new QuanjiResolvedEvent(
            pending.DamageFrameId,
            owner.Seat,
            point,
            used,
            drawnCardId,
            authorityCardId,
            GetAuthority(owner).Count));
        AddLog(
            used ? "SkillTriggered" : "SkillSkipped",
            used
                ? $"{owner.Name} 发动【权计】，将一张手牌置为“权”（{GetAuthority(owner).Count} 张）。"
                : $"{owner.Name} 未发动第 {point} 次【权计】。",
            owner.Seat);

        if (pending.QuanjiDamagePoint < pending.Attack.DamageAmount)
        {
            _pendingDecision = CreateQuanjiDecision(pending);
            _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
            return;
        }

        CompleteQuanjiDamageSkill(pending);
    }

    private void CompleteQuanjiDamageSkill(DamageSkillResolution pending)
    {
        var window = _pendingDamageTrigger ??
            throw new InvalidOperationException("Quanji lost its damage trigger window.");
        SetDamageSkillFrameStep(pending.FrameId, ResolutionFrameStep.ResolvingEffect);
        QueueGameEvent(new DamageSkillResolvedEvent(
            pending.DamageFrameId,
            pending.OwnerSeat,
            pending.SourceSeat,
            pending.Card?.Id,
            pending.EffectiveCardKind,
            SkillKind.Quanji,
            pending.QuanjiUsedAny,
            pending.CandidateId,
            pending.Priority));
        PopResolutionFrame(pending.FrameId, ResolutionFrameKind.DamageSkill);
        _pendingDamageSkill = null;
        SetDamageTriggerWindowStep(window.FrameId, ResolutionFrameStep.ResolvingEffect);
        AdvanceDamageTriggerCandidate(window);
    }

    private void ResolvePendingAiQuanji()
    {
        var pending = _pendingDamageSkill ??
            throw new InvalidOperationException("AI Quanji continuation is missing.");
        var decision = _pendingDecision ??
            throw new InvalidOperationException("AI Quanji prompt is missing.");
        if (pending.Effect != DamageSkillEffectKind.StoreAuthority ||
            decision.Kind != DecisionKind.Quanji ||
            decision.PlayerSeat != pending.OwnerSeat)
        {
            throw new InvalidOperationException("AI Quanji state is inconsistent.");
        }

        var action = pending.QuanjiAwaitingCardSelection ? "quanji-store" : "quanji-use";
        var selected = decision.Choices
            .Where(choice => choice.Parameters.GetValueOrDefault("action") == action)
            .OrderBy(choice => choice.Cards.Count == 0
                ? int.MinValue
                : CardCatalog.Get(GetHand(_players[pending.OwnerSeat])
                    .Single(card => card.Id == choice.Cards[0]).Kind).HandKeepValue)
            .ThenBy(choice => choice.Cards.FirstOrDefault())
            .First();
        ResolveQuanjiChoice(selected);
    }

    private bool TryBeginZiliAwakening(CharacterState player)
    {
        if (!UsesFormalZhongHui ||
            !HasLegacyRuntimeSkill(player, ZiliSkillId) ||
            GetAuthority(player).Count < 3 ||
            _skillRuntimeState.GetUsage(
                player.Seat,
                ZiliSkillId,
                AwakeningUsageId,
                SkillUsageScope.Game) != 0)
        {
            return false;
        }

        if (!_skillRuntimeState.TryConsumeUsage(
                player.Seat,
                ZiliSkillId,
                AwakeningUsageId,
                SkillUsageScope.Game,
                limit: 1))
        {
            throw new InvalidOperationException("Zili awakening was consumed twice.");
        }

        _pendingZili = new ZiliResolution(player.Seat);
        var choices = new List<PromptChoice>();
        if (player.Hp < player.MaxHp)
        {
            choices.Add(new PromptChoice(
                new ChoiceId("zili.recover"),
                "回复1点体力。",
                [],
                [],
                new Dictionary<string, string> { ["action"] = "zili-recover" }));
        }
        choices.Add(new PromptChoice(
            new ChoiceId("zili.draw"),
            "摸两张牌。",
            [],
            [],
            new Dictionary<string, string> { ["action"] = "zili-draw" }));
        _pendingDecision = new PendingDecision(
            DecisionKind.Zili,
            player.Seat,
            "【自立】觉醒：选择回复1点体力或摸两张牌，然后获得【排异】。",
            [],
            [])
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            Choices = choices.AsReadOnly()
        };
        _status = player.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return true;
    }

    private void ResolveZiliChoice(PromptChoice selected)
    {
        var pending = _pendingZili ??
            throw new InvalidOperationException("There is no Zili awakening pending.");
        if (_pendingDecision is not { Kind: DecisionKind.Zili, PlayerSeat: var playerSeat } ||
            playerSeat != pending.PlayerSeat)
        {
            throw new InvalidOperationException("There is no Zili choice to resolve.");
        }

        var player = _players[pending.PlayerSeat];
        var action = selected.Parameters.GetValueOrDefault("action");
        var recover = action == "zili-recover";
        if (recover && (selected.Cards.Count != 0 || selected.Targets.Count != 0 || player.Hp >= player.MaxHp))
        {
            throw new InvalidOperationException("Zili recovery is no longer legal.");
        }
        if (!recover && (action != "zili-draw" || selected.Cards.Count != 0 || selected.Targets.Count != 0))
        {
            throw new InvalidOperationException("The Zili choice is malformed.");
        }

        ClearPendingDecision();
        IReadOnlyList<int> drawn = [];
        if (recover)
        {
            var recoveryFrameId = BeginRecovery(0, player.Seat, player.Seat, 1);
            player.Hp = Math.Min(player.MaxHp, player.Hp + 1);
            QueueGameEvent(new RecoveryAppliedEvent(player.Seat, player.Seat, 1, player.Hp));
            PopResolutionFrame(recoveryFrameId, ResolutionFrameKind.Recovery);
        }
        else
        {
            drawn = DrawCards(player, 2, log: true, reason: CardMoveReasons.Draw);
        }

        player.MaxHp = Math.Max(1, player.MaxHp - 1);
        player.Hp = Math.Min(player.Hp, player.MaxHp);
        QueueGameEvent(new MaximumHpChangedEvent(player.Seat, -1, player.MaxHp, ZiliSkillId));

        var acquired = AcquireRuntimeSkills(player, ZiliSkillId, [PaiyiSkillId]);
        QueueGameEvent(new SkillAwakenedEvent(player.Seat, ZiliSkillId, player.MaxHp, acquired));
        QueueGameEvent(new ZiliResolvedEvent(
            player.Seat,
            recover,
            drawn,
            player.Hp,
            player.MaxHp,
            acquired));
        AddLog(
            "SkillTriggered",
            recover
                ? $"{player.Name} 的【自立】觉醒：减1点体力上限，回复1点体力并获得【排异】。"
                : $"{player.Name} 的【自立】觉醒：减1点体力上限，摸两张牌并获得【排异】。",
            player.Seat);
        _pendingZili = null;
        BeginTurnStartAfterZili(player);
    }

    private bool IsAiZiliPending() =>
        _pendingZili is { } pending &&
        _pendingDecision is { Kind: DecisionKind.Zili } decision &&
        decision.PlayerSeat == pending.PlayerSeat &&
        !_players[pending.PlayerSeat].IsHuman;

    private void ResolvePendingAiZili()
    {
        var pending = _pendingZili ?? throw new InvalidOperationException("AI Zili continuation is missing.");
        var player = _players[pending.PlayerSeat];
        var decision = _pendingDecision ?? throw new InvalidOperationException("AI Zili prompt is missing.");
        var action = player.Hp < player.MaxHp && player.Hp <= 1 ? "zili-recover" : "zili-draw";
        var choice = decision.Choices.FirstOrDefault(candidate =>
                         candidate.Parameters.GetValueOrDefault("action") == action) ??
                     decision.Choices.Single();
        ResolveZiliChoice(choice);
    }

    private void BeginTurnStartAfterZili(CharacterState current)
    {
        ResolveDanjiAwakening(current);
        if (TryBeginQianxiChoice(current))
        {
            return;
        }

        BeginTurnStartAfterQianxi(current);
    }

    private void ResolvePaiyi(
        long frameId,
        CharacterState source,
        int authorityCardId,
        int targetSeat)
    {
        var frame = _resolutionStack.OfType<ActiveSkillFrame>().Single(candidate => candidate.Id == frameId);
        if (frame is not
            {
                Skill: SkillKind.Paiyi,
                Effect: ActiveSkillEffectKind.RemoveAuthorityDrawAndDamage
            })
        {
            throw new InvalidOperationException("Paiyi lost its active-skill frame.");
        }

        var authority = GetAuthority(source).SingleOrDefault(card => card.Id == authorityCardId) ??
            throw new InvalidOperationException("The selected Paiyi authority card is unavailable.");
        var target = _players[targetSeat];
        if (!target.IsAlive)
        {
            throw new InvalidOperationException("Paiyi requires a living target.");
        }

        source.UsedActiveSkillKinds.Add(SkillKind.Paiyi);
        MoveCard(authority, CardLocation.Authority(source.Seat), CardLocation.DiscardPile, CardMoveReasons.PaiyiRemove);
        var drawn = DrawCards(target, 2, log: true, reason: CardMoveReasons.PaiyiDraw);
        var damageTriggered = target.Seat != source.Seat && GetHand(target).Count > GetHand(source).Count;
        QueueGameEvent(new PaiyiResolvedEvent(
            frameId,
            source.Seat,
            target.Seat,
            authority.Id,
            drawn,
            damageTriggered));
        AddLog(
            "SkillTriggered",
            damageTriggered
                ? $"{source.Name} 发动【排异】，令 {target.Name} 摸两张牌；其手牌更多，将受到1点伤害。"
                : $"{source.Name} 发动【排异】，令 {target.Name} 摸两张牌。",
            source.Seat,
            target.Seat);

        if (damageTriggered)
        {
            BeginPaiyiDamage(frameId, source, target);
        }
        else
        {
            CompletePaiyiResolution(frameId, dealtDamage: false);
        }
    }

    private void BeginPaiyiDamage(long frameId, CharacterState source, CharacterState target)
    {
        SetActiveSkillFrameStep(frameId, ResolutionFrameStep.ResolvingEffect);
        var attack = new AttackResolution(
            frameId,
            source.Seat,
            target.Seat,
            card: null,
            damageAmount: 1,
            sourceSkill: SkillKind.Paiyi,
            damageNatureOverride: DamageNature.Normal);
        _pendingAttack = attack;
        if (!ApplyAttackDamage(attack))
        {
            CompleteAttack(attack);
        }
    }

    private void CompletePaiyiResolution(long frameId, bool dealtDamage)
    {
        var frame = _resolutionStack.OfType<ActiveSkillFrame>().SingleOrDefault(candidate => candidate.Id == frameId);
        if (frame is not
            {
                Skill: SkillKind.Paiyi,
                Effect: ActiveSkillEffectKind.RemoveAuthorityDrawAndDamage,
                TargetSeats: { Count: 1 }
            })
        {
            throw new InvalidOperationException("The Paiyi active-skill frame is missing.");
        }

        SetActiveSkillFrameStep(frameId, ResolutionFrameStep.Completed);
        QueueGameEvent(new ActiveSkillResolvedEvent(frameId, frame.SourceSeat, frame.Skill, frame.Effect));
        PopResolutionFrame(frameId, ResolutionFrameKind.ActiveSkill);
        if (dealtDamage)
        {
            AddLog(
                "ActiveSkill",
                $"{_players[frame.SourceSeat].Name} 的【排异】对 {_players[frame.TargetSeats[0]].Name} 造成1点伤害。",
                frame.SourceSeat,
                frame.TargetSeats[0]);
        }
    }

    private sealed record ZiliResolution(int PlayerSeat);
}
