namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string JuzhanSkillId = "classic:juzhan";
    private const string JuzhanProhibitionUsagePrefix = "card-target-prohibition";
    private const string JuzhanCardUseUsagePrefix = "card-use";
    private readonly HashSet<long> _resolvedJuzhanCardUses = [];
    private JuzhanResolution? _pendingJuzhan;

    private bool SupportsFormalJuzhan => _rulesVersion >= 99;

    private bool SupportsJuzhanEventLedger => _rulesVersion >= 101;

    private void InitializeStructuredConversionSkills()
    {
        if (!SupportsFormalJuzhan || _contentRegistry is null) return;

        foreach (var player in _players)
            foreach (var skillId in EnabledContentSkillIds(player))
            {
                RegisterTaggedConversionSkill(player, skillId);
            }
    }

    private void RegisterTaggedConversionSkill(CharacterState player, string skillId)
    {
        if (!SupportsFormalJuzhan || _contentRegistry is null) return;
        if ((_contentRegistry.GetSkill(skillId).Tags & SkillTag.Conversion) != 0)
            _skillRuntimeState.RegisterConversionSkill(player.Seat, skillId, SkillPolarity.Yang);
    }

    private static string GetJuzhanProhibitionUsageId(int sourceSeat, int targetSeat) =>
        $"{JuzhanProhibitionUsagePrefix}.source-{sourceSeat}.target-{targetSeat}";

    private static string GetJuzhanCardUseUsageId(long cardUseFrameId) =>
        $"{JuzhanCardUseUsagePrefix}.resolution-{cardUseFrameId}";

    private bool IsJuzhanCardUseResolved(long cardUseFrameId) =>
        SupportsJuzhanEventLedger
            ? _players.Any(owner => HasRuntimeSkill(owner, JuzhanSkillId) &&
                _skillRuntimeState.GetUsage(
                    owner.Seat,
                    JuzhanSkillId,
                    GetJuzhanCardUseUsageId(cardUseFrameId),
                    SkillUsageScope.Event) > 0)
            : _resolvedJuzhanCardUses.Contains(cardUseFrameId);

    private void MarkJuzhanCardUseResolved(long cardUseFrameId, IEnumerable<int> ownerSeats)
    {
        if (!SupportsJuzhanEventLedger)
        {
            _resolvedJuzhanCardUses.Add(cardUseFrameId);
            return;
        }

        foreach (var ownerSeat in ownerSeats.Distinct())
        {
            if (!_skillRuntimeState.TryConsumeUsage(
                    ownerSeat,
                    JuzhanSkillId,
                    GetJuzhanCardUseUsageId(cardUseFrameId),
                    SkillUsageScope.Event,
                    limit: 1))
            {
                throw new InvalidOperationException(
                    "Juzhan tried to process the same card-use event twice for one owner.");
            }

            QueueGameEvent(new SkillUsageConsumedEvent(
                ownerSeat,
                JuzhanSkillId,
                GetJuzhanCardUseUsageId(cardUseFrameId),
                SkillUsageScope.Event,
                Count: 1));
        }
    }

    private void ClearJuzhanCardUseLedger(long cardUseFrameId)
    {
        if (!SupportsJuzhanEventLedger)
        {
            _resolvedJuzhanCardUses.Remove(cardUseFrameId);
            return;
        }

        foreach (var owner in _players.Where(player => HasRuntimeSkill(player, JuzhanSkillId)))
        {
            _skillRuntimeState.ClearUsage(
                owner.Seat,
                JuzhanSkillId,
                GetJuzhanCardUseUsageId(cardUseFrameId),
                SkillUsageScope.Event);
        }
    }

    private bool IsJuzhanCardTargetProhibited(int sourceSeat, int targetSeat) =>
        SupportsFormalJuzhan &&
        _players.Any(owner => HasRuntimeSkill(owner, JuzhanSkillId) &&
            _skillRuntimeState.GetUsage(
                owner.Seat,
                JuzhanSkillId,
                GetJuzhanProhibitionUsageId(sourceSeat, targetSeat),
                SkillUsageScope.Turn) > 0);

    private void AddJuzhanCardTargetProhibition(
        int ownerSeat,
        int sourceSeat,
        int targetSeat)
    {
        if (!_skillRuntimeState.TryConsumeUsage(
                ownerSeat,
                JuzhanSkillId,
                GetJuzhanProhibitionUsageId(sourceSeat, targetSeat),
                SkillUsageScope.Turn,
                limit: 1))
        {
            throw new InvalidOperationException("Juzhan tried to add the same turn-scoped target prohibition twice.");
        }

        QueueGameEvent(new CardTargetProhibitionAddedEvent(
            ownerSeat,
            JuzhanSkillId,
            sourceSeat,
            targetSeat,
            SkillUsageScope.Turn));
    }

    private SkillPolarity ToggleJuzhan(CharacterState owner, SkillPolarity previous)
    {
        var current = _skillRuntimeState.ToggleConversionState(owner.Seat, JuzhanSkillId);
        QueueGameEvent(new SkillConversionStateChangedEvent(
            owner.Seat,
            JuzhanSkillId,
            previous,
            current));
        return current;
    }

    private bool TryBeginJuzhanWindow(AttackResolution attack)
    {
        if (!SupportsFormalJuzhan || IsJuzhanCardUseResolved(attack.ResolutionId))
            return false;
        if (_pendingJuzhan is not null)
            throw new InvalidOperationException("The engine cannot open two Juzhan windows at once.");

        var frame = _resolutionStack.OfType<CardUseFrame>()
            .Single(cardUse => cardUse.Id == attack.ResolutionId);
        var source = _players[frame.SourceSeat];
        var targetSeats = frame.TargetSeats
            .Where(IsValidPlayerSeat)
            .Distinct()
            .ToArray();
        var candidates = new List<JuzhanCandidate>();

        if (source.IsAlive &&
            HasRuntimeSkill(source, JuzhanSkillId) &&
            _skillRuntimeState.GetConversionState(source.Seat, JuzhanSkillId) == SkillPolarity.Yin &&
            targetSeats.Any(seat => _players[seat].IsAlive && HasTargetCard(_players[seat])))
        {
            candidates.Add(new JuzhanCandidate(
                source.Seat,
                SkillPolarity.Yin,
                Array.AsReadOnly(targetSeats)));
        }

        foreach (var targetSeat in targetSeats)
        {
            var target = _players[targetSeat];
            if (target.IsAlive &&
                target.Seat != source.Seat &&
                HasRuntimeSkill(target, JuzhanSkillId) &&
                _skillRuntimeState.GetConversionState(target.Seat, JuzhanSkillId) == SkillPolarity.Yang)
            {
                candidates.Add(new JuzhanCandidate(
                    target.Seat,
                    SkillPolarity.Yang,
                    Array.AsReadOnly(new[] { target.Seat })));
            }
        }

        if (candidates.Count == 0)
        {
            if (!SupportsJuzhanEventLedger)
                _resolvedJuzhanCardUses.Add(attack.ResolutionId);
            return false;
        }

        _pendingJuzhan = new JuzhanResolution(
            attack,
            source.Seat,
            Array.AsReadOnly(candidates.ToArray()));
        PublishNextJuzhanChoice(_pendingJuzhan);
        return true;
    }

    private void PublishNextJuzhanChoice(JuzhanResolution pending)
    {
        if (!ReferenceEquals(_pendingJuzhan, pending) ||
            !ReferenceEquals(_pendingAttack, pending.Attack))
        {
            throw new InvalidOperationException("The Juzhan window lost its Slash continuation.");
        }

        while (pending.CandidateIndex < pending.Candidates.Count)
        {
            var candidate = pending.Candidates[pending.CandidateIndex];
            var owner = _players[candidate.OwnerSeat];
            if (!owner.IsAlive ||
                !HasRuntimeSkill(owner, JuzhanSkillId) ||
                _skillRuntimeState.GetConversionState(owner.Seat, JuzhanSkillId) != candidate.State)
            {
                pending.CandidateIndex++;
                continue;
            }

            var choices = candidate.State == SkillPolarity.Yang
                ? CreateJuzhanYangChoices(pending, candidate)
                : CreateJuzhanYinChoices(pending, candidate);
            if (choices.Count == 1 &&
                choices[0].Parameters.GetValueOrDefault("action") == "juzhan-skip")
            {
                pending.CandidateIndex++;
                continue;
            }

            var publicCardIds = candidate.TargetSeats
                .SelectMany(targetSeat => GetEquipment(_players[targetSeat]).Concat(GetJudgment(targetSeat)))
                .Select(card => card.Id)
                .Order()
                .ToArray();
            _pendingDecision = new PendingDecision(
                DecisionKind.Juzhan,
                owner.Seat,
                candidate.State == SkillPolarity.Yang
                    ? $"{_players[pending.SourceSeat].Name} 对你使用了【杀】，是否发动【拒战·阳】？"
                    : "你使用【杀】指定了目标，是否发动【拒战·阴】获得其中一名目标角色的一张牌？",
                publicCardIds,
                candidate.TargetSeats,
                SourceSeat: pending.SourceSeat,
                IncomingCard: pending.Attack.EffectiveCardKind)
            {
                PromptId = owner.IsHuman ? CreatePromptId() : default,
                IsPrivate = true,
                TargetSeat = candidate.TargetSeats.FirstOrDefault(),
                Choices = choices
            };
            _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
            return;
        }

        CompleteJuzhanWindow(pending);
    }

    private IReadOnlyList<PromptChoice> CreateJuzhanYangChoices(
        JuzhanResolution pending,
        JuzhanCandidate candidate)
    {
        var owner = _players[candidate.OwnerSeat];
        var source = _players[pending.SourceSeat];
        if (!source.IsAlive || candidate.TargetSeats is not [var targetSeat] || targetSeat != owner.Seat)
            return [CreateJuzhanSkipChoice(pending, candidate)];

        return
        [
            new PromptChoice(
                new ChoiceId($"juzhan.yang.use.resolution-{pending.Attack.ResolutionId}.owner-{owner.Seat}"),
                $"发动【拒战·阳】：你与 {source.Name} 各摸一张牌，且其本回合不能再对你使用牌。",
                [],
                [owner.Seat],
                new Dictionary<string, string> { ["action"] = "juzhan-yang-use" }),
            CreateJuzhanSkipChoice(pending, candidate)
        ];
    }

    private IReadOnlyList<PromptChoice> CreateJuzhanYinChoices(
        JuzhanResolution pending,
        JuzhanCandidate candidate)
    {
        var choices = new List<PromptChoice>();
        foreach (var targetSeat in candidate.TargetSeats)
        {
            var target = _players[targetSeat];
            if (!target.IsAlive) continue;

            var hand = GetHand(target);
            for (var slot = 0; slot < hand.Count; slot++)
            {
                choices.Add(new PromptChoice(
                    new ChoiceId($"juzhan.yin.hand-{targetSeat}-{slot}.resolution-{pending.Attack.ResolutionId}"),
                    $"发动【拒战·阴】，获得 {target.Name} 的第 {slot + 1} 个暗手牌位。",
                    [],
                    [targetSeat],
                    new Dictionary<string, string>
                    {
                        ["action"] = "juzhan-yin-obtain",
                        ["target-zone"] = "hand",
                        ["slot-index"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }));
            }

            AddJuzhanPublicCardChoices(choices, pending, target, GetEquipment(target), "equipment", "装备区");
            AddJuzhanPublicCardChoices(choices, pending, target, GetJudgment(target), "judgment", "判定区");
        }
        choices.Add(CreateJuzhanSkipChoice(pending, candidate));
        return Array.AsReadOnly(choices.ToArray());
    }

    private static void AddJuzhanPublicCardChoices(
        ICollection<PromptChoice> choices,
        JuzhanResolution pending,
        CharacterState target,
        IReadOnlyList<Card> cards,
        string zone,
        string zoneLabel)
    {
        foreach (var card in cards)
        {
            choices.Add(new PromptChoice(
                new ChoiceId($"juzhan.yin.{zone}-{target.Seat}-{card.Id}.resolution-{pending.Attack.ResolutionId}"),
                $"发动【拒战·阴】，获得 {target.Name}{zoneLabel}的【{card.DisplayName}】。",
                [card.Id],
                [target.Seat],
                new Dictionary<string, string>
                {
                    ["action"] = "juzhan-yin-obtain",
                    ["target-zone"] = zone
                }));
        }
    }

    private static PromptChoice CreateJuzhanSkipChoice(
        JuzhanResolution pending,
        JuzhanCandidate candidate) =>
        new(
            new ChoiceId($"juzhan.skip.resolution-{pending.Attack.ResolutionId}.owner-{candidate.OwnerSeat}"),
            "不发动【拒战】。",
            [],
            [],
            new Dictionary<string, string> { ["action"] = "juzhan-skip" });

    private void ResolveJuzhanChoice(PromptChoice selected)
    {
        var pending = _pendingJuzhan ??
            throw new InvalidOperationException("There is no Juzhan choice to resolve.");
        if (!ReferenceEquals(_pendingAttack, pending.Attack) ||
            _pendingDecision is not { Kind: DecisionKind.Juzhan } decision ||
            pending.CandidateIndex >= pending.Candidates.Count)
        {
            throw new InvalidOperationException("The Juzhan choice is not the current Slash continuation.");
        }

        var candidate = pending.Candidates[pending.CandidateIndex];
        var owner = _players[candidate.OwnerSeat];
        if (decision.PlayerSeat != owner.Seat ||
            _skillRuntimeState.GetConversionState(owner.Seat, JuzhanSkillId) != candidate.State)
        {
            throw new InvalidOperationException("The Juzhan owner or conversion state changed before resolution.");
        }

        var action = selected.Parameters.GetValueOrDefault("action");
        CardLocation? obtainedFrom = null;
        Card? obtainedCard = null;
        int? resolvedTargetSeat = null;
        var used = false;

        if (action == "juzhan-yang-use")
        {
            if (candidate.State != SkillPolarity.Yang ||
                selected.Cards.Count != 0 ||
                selected.Targets is not [var targetSeat] ||
                targetSeat != owner.Seat ||
                !_players[pending.SourceSeat].IsAlive)
            {
                throw new InvalidOperationException("The Juzhan Yang choice is malformed or no longer legal.");
            }

            var source = _players[pending.SourceSeat];
            DrawCards(owner, 1, log: true, reason: new CardMoveReason("skill.juzhan.yang-draw"));
            DrawCards(source, 1, log: true, reason: new CardMoveReason("skill.juzhan.yang-source-draw"));
            AddJuzhanCardTargetProhibition(owner.Seat, source.Seat, owner.Seat);
            resolvedTargetSeat = owner.Seat;
            used = true;
        }
        else if (action == "juzhan-yin-obtain")
        {
            if (candidate.State != SkillPolarity.Yin ||
                selected.Targets is not [var targetSeat] ||
                !candidate.TargetSeats.Contains(targetSeat) ||
                !_players[targetSeat].IsAlive)
            {
                throw new InvalidOperationException("The Juzhan Yin target is malformed or no longer legal.");
            }

            var target = _players[targetSeat];
            var zone = selected.Parameters.GetValueOrDefault("target-zone");
            if (zone == "hand" &&
                selected.Cards.Count == 0 &&
                selected.Parameters.TryGetValue("slot-index", out var slotText) &&
                int.TryParse(slotText, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var slot) &&
                slot >= 0 && slot < GetHand(target).Count)
            {
                obtainedCard = GetHand(target)[slot];
                obtainedFrom = CardLocation.Hand(target.Seat);
            }
            else if (zone is "equipment" or "judgment" && selected.Cards is [var publicCardId])
            {
                var publicCard = FindPublicTargetCard(target, publicCardId);
                var expectedZone = zone == "equipment" ? CardZoneKind.Equipment : CardZoneKind.Judgment;
                if (publicCard is { } located && located.Location.Zone == expectedZone)
                {
                    obtainedCard = located.Card;
                    obtainedFrom = located.Location;
                }
            }

            if (obtainedCard is null || obtainedFrom is null)
                throw new InvalidOperationException("The selected Juzhan target card is no longer available.");

            MoveCard(
                obtainedCard,
                obtainedFrom.Value,
                CardLocation.Hand(owner.Seat),
                new CardMoveReason("skill.juzhan.obtain"));
            AddJuzhanCardTargetProhibition(owner.Seat, owner.Seat, target.Seat);
            resolvedTargetSeat = target.Seat;
            used = true;
        }
        else if (action != "juzhan-skip" || selected.Cards.Count != 0 || selected.Targets.Count != 0)
        {
            throw new InvalidOperationException("The Juzhan choice is malformed.");
        }

        ClearPendingDecision();
        if (used)
        {
            _ = ToggleJuzhan(owner, candidate.State);
        }
        QueueGameEvent(new JuzhanResolvedEvent(
            pending.Attack.ResolutionId,
            owner.Seat,
            pending.SourceSeat,
            resolvedTargetSeat,
            candidate.State,
            used,
            obtainedFrom?.Zone,
            obtainedFrom is { Zone: CardZoneKind.Equipment or CardZoneKind.Judgment }
                ? obtainedCard?.Id
                : null));
        AddLog(
            used ? "SkillTriggered" : "SkillSkipped",
            used
                ? candidate.State == SkillPolarity.Yang
                    ? $"{owner.Name} 发动【拒战·阳】，与 {_players[pending.SourceSeat].Name} 各摸一张牌。"
                    : $"{owner.Name} 发动【拒战·阴】，获得 {_players[resolvedTargetSeat!.Value].Name} 的一张牌。"
                : $"{owner.Name} 未发动【拒战·{(candidate.State == SkillPolarity.Yang ? "阳" : "阴")}】。",
            owner.Seat,
            resolvedTargetSeat);

        pending.CandidateIndex++;
        PublishNextJuzhanChoice(pending);
    }

    private void CompleteJuzhanWindow(JuzhanResolution pending)
    {
        if (!ReferenceEquals(_pendingJuzhan, pending))
            throw new InvalidOperationException("The completed Juzhan window is no longer current.");

        _pendingJuzhan = null;
        ClearPendingDecision();
        MarkJuzhanCardUseResolved(
            pending.Attack.ResolutionId,
            pending.Candidates.Select(candidate => candidate.OwnerSeat));
        _status = EngineStatus.Running;
        ContinueSlashAfterJuzhan(pending.Attack);
    }

    private bool IsAiJuzhanPending() =>
        _pendingJuzhan is { } pending &&
        pending.CandidateIndex < pending.Candidates.Count &&
        _pendingDecision is { Kind: DecisionKind.Juzhan, PlayerSeat: var ownerSeat } &&
        pending.Candidates[pending.CandidateIndex].OwnerSeat == ownerSeat &&
        !_players[ownerSeat].IsHuman;

    private void ResolvePendingAiJuzhan()
    {
        if (!IsAiJuzhanPending())
            throw new InvalidOperationException("There is no AI Juzhan choice to resolve.");

        var decision = _pendingDecision!;
        var selected = decision.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("action") != "juzhan-skip");
        ResolveJuzhanChoice(selected);
        PublishState();
    }

    private void AssertJuzhanInvariant()
    {
        if (_pendingJuzhan is null)
        {
            if (_pendingDecision?.Kind == DecisionKind.Juzhan)
                throw new InvalidOperationException("A Juzhan prompt cannot exist without its Slash continuation.");
            return;
        }

        var pending = _pendingJuzhan;
        if (pending.CandidateIndex < 0 || pending.CandidateIndex >= pending.Candidates.Count)
            throw new InvalidOperationException("A Juzhan continuation has an invalid candidate cursor.");
        var candidate = pending.Candidates[pending.CandidateIndex];
        var owner = _players[candidate.OwnerSeat];
        var frame = _resolutionStack.LastOrDefault() as CardUseFrame;
        var decision = _pendingDecision;
        if (!SupportsFormalJuzhan ||
            !ReferenceEquals(_pendingAttack, pending.Attack) ||
            !owner.IsAlive ||
            !HasRuntimeSkill(owner, JuzhanSkillId) ||
            _skillRuntimeState.GetConversionState(owner.Seat, JuzhanSkillId) != candidate.State ||
            frame is null ||
            frame.Id != pending.Attack.ResolutionId ||
            frame.Step != ResolutionFrameStep.Declared ||
            decision is not { Kind: DecisionKind.Juzhan, IsPrivate: true } ||
            decision.PlayerSeat != owner.Seat ||
            decision.SourceSeat != pending.SourceSeat ||
            decision.Choices.Count == 0)
        {
            throw new InvalidOperationException(
                "A Juzhan choice must retain its owner, polarity, private prompt and declared Slash continuation.");
        }

        var expectedStatus = owner.IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
        if (_status != expectedStatus)
            throw new InvalidOperationException("A Juzhan prompt status does not match its owner.");
    }

    private IReadOnlyList<LegalAction> FilterJuzhanProhibitedCardActions(
        CharacterState actor,
        IReadOnlyList<LegalAction> actions)
    {
        if (!SupportsFormalJuzhan) return actions;
        return actions.Where(action => !GetJuzhanCardTargets(actor, action)
                .Any(targetSeat => IsJuzhanCardTargetProhibited(actor.Seat, targetSeat)))
            .ToArray();
    }

    private IEnumerable<int> GetJuzhanCardTargets(CharacterState actor, LegalAction action)
    {
        switch (action.Kind)
        {
            case LegalActionKind.BarbarianAssault:
            case LegalActionKind.ArrowBarrage:
                return _players.Where(player => player.IsAlive && player.Seat != actor.Seat)
                    .Select(player => player.Seat);
            case LegalActionKind.PeachGarden:
            case LegalActionKind.FiveGrains:
                return _players.Where(player => player.IsAlive).Select(player => player.Seat);
            case LegalActionKind.BorrowedSword:
                return action.TargetSeats.Take(1);
            case LegalActionKind.Slash:
            case LegalActionKind.Peach:
            case LegalActionKind.Duel:
            case LegalActionKind.Dismantlement:
            case LegalActionKind.Snatch:
            case LegalActionKind.FireAttack:
            case LegalActionKind.Equip:
            case LegalActionKind.IronChain:
            case LegalActionKind.Indulgence:
            case LegalActionKind.SupplyShortage:
            case LegalActionKind.Lightning:
                return action.TargetSeats;
            default:
                return [];
        }
    }

    private bool CanUseJuzhanGlobalCard(CharacterState actor) =>
        !SupportsFormalJuzhan ||
        _players.Where(player => player.IsAlive && player.Seat != actor.Seat)
            .All(player => !IsJuzhanCardTargetProhibited(actor.Seat, player.Seat));

    private sealed record JuzhanCandidate(
        int OwnerSeat,
        SkillPolarity State,
        IReadOnlyList<int> TargetSeats);

    private sealed class JuzhanResolution(
        AttackResolution attack,
        int sourceSeat,
        IReadOnlyList<JuzhanCandidate> candidates)
    {
        public AttackResolution Attack { get; } = attack;
        public int SourceSeat { get; } = sourceSeat;
        public IReadOnlyList<JuzhanCandidate> Candidates { get; } = candidates;
        public int CandidateIndex { get; set; }
    }
}
