namespace CardGame.Core;

/// <summary>
/// A deliberately small, inspectable heuristic AI. It receives the same filtered
/// snapshot as a human player: its own role and hand, the public Lord/dead roles,
/// and only other players' hand counts.
/// </summary>
public sealed class SimpleAiBrain
{
    private readonly Dictionary<int, double> _rebelSuspicion = [];
    private readonly DeterministicRandom _random;

    public SimpleAiBrain(int seat, int seed)
    {
        Seat = seat;
        _random = new DeterministicRandom(seed == 0 ? seat + 1 : seed);
    }

    public int Seat { get; }

    public IReadOnlyDictionary<int, double> RebelSuspicion => _rebelSuspicion;

    public void ObserveSlash(int sourceSeat, int targetSeat, int lordSeat, Role? revealedTargetRole)
    {
        if (sourceSeat == Seat)
        {
            return;
        }

        _rebelSuspicion.TryAdd(sourceSeat, 0d);
        if (targetSeat == lordSeat)
        {
            _rebelSuspicion[sourceSeat] += 3d;
        }

        if (revealedTargetRole == Role.Rebel)
        {
            _rebelSuspicion[sourceSeat] -= 1.5d;
        }

        if (targetSeat != lordSeat && _rebelSuspicion.GetValueOrDefault(targetSeat) > 0d)
        {
            _rebelSuspicion[sourceSeat] -= Math.Min(1.5d, _rebelSuspicion[targetSeat] * 0.35d);
        }
    }

    /// <summary>
    /// Duel is a public hostile action with the same first-pass evidence signal
    /// as Slash. Keeping this adapter explicit lets the suspicion model evolve
    /// without making GameEngine reach into its private state.
    /// </summary>
    public void ObserveDuel(int sourceSeat, int targetSeat, int lordSeat, Role? revealedTargetRole) =>
        ObserveSlash(sourceSeat, targetSeat, lordSeat, revealedTargetRole);

    /// <summary>
    /// A group attack exposes the same public hostile-action signal as Slash. The
    /// explicit adapter keeps the engine's content vocabulary separate from the
    /// suspicion model while preserving one information boundary.
    /// </summary>
    public void ObserveGroupAttack(int sourceSeat, int targetSeat, int lordSeat, Role? revealedTargetRole) =>
        ObserveSlash(sourceSeat, targetSeat, lordSeat, revealedTargetRole);

    /// <summary>
    /// Death reveals a role publicly, so every observer can revise its model without
    /// reading hidden state. Killing a Rebel is loyal-looking; killing a Loyalist is not.
    /// </summary>
    public void ObserveDeath(int? killerSeat, Role revealedVictimRole)
    {
        if (killerSeat is null || killerSeat == Seat)
        {
            return;
        }

        _rebelSuspicion.TryAdd(killerSeat.Value, 0d);
        _rebelSuspicion[killerSeat.Value] += revealedVictimRole switch
        {
            Role.Rebel => -3d,
            Role.Loyalist => 2.5d,
            Role.Lord => 5d,
            Role.Renegade => -0.5d,
            _ => 0d
        };
    }

    public (LegalAction Action, AiThoughtRecord Thought) ChoosePlay(
        GameSnapshot view,
        IReadOnlyList<LegalAction> legalActions,
        int thoughtSequence)
    {
        if (legalActions.Count == 0)
        {
            throw new InvalidOperationException("AI was asked to choose from an empty action set.");
        }

        var self = view.Players.Single(player => player.Seat == Seat);
        var selfRole = self.Role ?? throw new InvalidOperationException("An AI must see its own role.");
        var candidates = new List<AiCandidateScore>(legalActions.Count);

        foreach (var action in legalActions)
        {
            var (score, reason) = ScoreAction(view, self, selfRole, action);
            // A very small seeded jitter resolves exact ties without making replays unstable.
            score += _random.NextDouble() * 0.001d;
            candidates.Add(new AiCandidateScore(action, Math.Round(score, 3), reason));
        }

        var selected = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Action.CardId ?? int.MaxValue)
            .First();

        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Action.Description,
            candidates.OrderByDescending(candidate => candidate.Score).ToArray(),
            $"选择最高分动作：{selected.Action.Description}（{selected.Score:0.###} 分）。");

        return (selected.Action, thought);
    }

    /// <summary>
    /// Chooses only from the private candidates supplied for this seat. The
    /// engine never gives this method another player's candidate list.
    /// </summary>
    public (GeneralDefinition General, AiGeneralThought Thought) ChooseGeneral(
        GameSnapshot view,
        Role role,
        IReadOnlyList<GeneralDefinition> candidates,
        int thoughtSequence)
    {
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("AI was asked to choose from an empty general set.");
        }

        var scored = candidates
            .Select(candidate =>
            {
                var (baseScore, reason) = ScoreGeneral(role, candidate);
                var score = Math.Round(baseScore + _random.NextDouble() * 0.001d, 3);
                return new AiGeneralCandidateScore(
                    candidate.Id,
                    candidate.Name,
                    candidate.SkillName,
                    score,
                    reason);
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.GeneralId, StringComparer.Ordinal)
            .ToArray();

        var selected = candidates.Single(candidate => candidate.Id == scored[0].GeneralId);
        var thought = new AiGeneralThought(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Id,
            selected.Name,
            scored,
            $"在 {scored.Length} 个私有候选中选择 {selected.Name}（{selected.SkillName}）。");
        return (selected, thought);
    }

    public (bool UseDodge, AiThoughtRecord Thought) ChooseDodge(
        GameSnapshot view,
        int attackerSeat,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var dodgeProfile = CardCatalog.Get(CardKind.Dodge);
        var useScore = self.Hp <= 1
            ? dodgeProfile.AiResponseValue + 35d
            : dodgeProfile.AiResponseValue + (self.MaxHp - self.Hp) * 8d;
        var passScore = self.Hp <= 1 ? -100d : 5d;
        var useDodge = useScore >= passScore;
        var pseudoAction = new LegalAction(
            LegalActionKind.EndPlay,
            null,
            attackerSeat,
            useDodge ? "打出闪" : "不出闪");

        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            pseudoAction.Description,
            [new(pseudoAction, useDodge ? useScore : passScore, "避免一点伤害，并保留生存空间。")],
            $"生命值 {self.Hp}/{self.MaxHp}，决定{pseudoAction.Description}。");

        return (useDodge, thought);
    }

    /// <summary>
    /// Chooses whether to use the private Feedback trigger. The incoming card
    /// kind is public combat context; the AI does not inspect the engine zone or
    /// another player's hand to make this choice.
    /// </summary>
    public (bool UseFeedback, AiThoughtRecord Thought) ChooseFeedback(
        GameSnapshot view,
        int sourceSeat,
        CardKind incomingCard,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var profile = CardCatalog.Get(incomingCard);
        var useScore = profile.HandKeepValue +
                       Math.Max(0, self.MaxHp - self.HandCount) * 8d -
                       self.HandCount * 4d;
        var skipScore = self.HandCount > self.MaxHp ? 52d : 8d;
        var useAction = new LegalAction(
            LegalActionKind.Feedback,
            null,
            sourceSeat,
            $"发动【反馈】获得{profile.DisplayName}");
        var skipAction = new LegalAction(
            LegalActionKind.SkipFeedback,
            null,
            sourceSeat,
            "不发动【反馈】");
        var candidates = new[]
        {
            new AiCandidateScore(
                useAction,
                Math.Round(useScore + _random.NextDouble() * 0.001d, 3),
                $"获得{profile.DisplayName}并扩大资源；只使用本座可见的生命和手牌数量。"),
            new AiCandidateScore(
                skipAction,
                Math.Round(skipScore + _random.NextDouble() * 0.001d, 3),
                "手牌接近上限时保留当前结构，避免无条件继续拿牌。")
        };
        var selected = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Action.Kind)
            .First();
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Action.Description,
            candidates,
            $"反馈触发：手牌 {self.HandCount}/{self.MaxHp}，决定{selected.Action.Description}（{selected.Score:0.###} 分）。");
        return (selected.Action.Kind == LegalActionKind.Feedback, thought);
    }

    /// <summary>
    /// Chooses the bounded Yiji gift from the owner's private hand and the
    /// published legal target seats. The AI never reads another player's cards
    /// or the engine's zone store.
    /// </summary>
    public (int? CardId, int? TargetSeat, AiThoughtRecord Thought) ChooseYijiGift(
        GameSnapshot view,
        IReadOnlyList<int> drawnCardIds,
        IReadOnlyList<int> targetSeats,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var cards = drawnCardIds
            .Select(cardId => self.Hand.Single(card => card.Id == cardId))
            .ToArray();
        var targets = targetSeats
            .Select(targetSeat => view.Players.Single(player => player.Seat == targetSeat))
            .Where(player => player.IsAlive && player.Seat != Seat)
            .ToArray();

        var candidates = (
            from card in cards
            from target in targets
            let profile = CardCatalog.Get(card.Kind)
            let score = profile.HandKeepValue +
                        Math.Max(0, self.MaxHp - target.HandCount) * 6d +
                        (target.Role is Role.Lord && self.Role is Role.Loyalist ? 8d : 0d)
            select new AiCandidateScore(
                new LegalAction(
                    LegalActionKind.YijiGift,
                    card.Id,
                    target.Seat,
                    $"将【{profile.DisplayName}】交给座位 {target.Seat + 1}"),
                Math.Round(score + _random.NextDouble() * 0.001d, 3),
                $"将自己可见的候选牌交给公开可见的存活座位；不读取目标手牌牌面。"))
            .ToList();

        candidates.Add(new AiCandidateScore(
            new LegalAction(
                LegalActionKind.SkipYiji,
                null,
                null,
                "不发动遗计分配，保留摸到的牌"),
            self.HandCount > self.MaxHp + 2 ? 58d : 4d,
            "保留两张牌；只使用自己的手牌数量判断是否资源过载。"));

        var selected = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Action.CardId ?? int.MaxValue)
            .ThenBy(candidate => candidate.Action.TargetSeat ?? int.MaxValue)
            .First();
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Action.Description,
            candidates.OrderByDescending(candidate => candidate.Score).ToArray(),
            $"遗计：从 {cards.Length} 张私有候选牌和 {targets.Length} 个公开合法目标中选择 {selected.Action.Description}。");
        return (
            selected.Action.Kind == LegalActionKind.YijiGift ? selected.Action.CardId : null,
            selected.Action.Kind == LegalActionKind.YijiGift ? selected.Action.TargetSeat : null,
            thought);
    }

    /// <summary>
    /// Chooses a public target for Jieming from the exact seats published by the
    /// prompt. Only hand counts, max HP, and roles already visible in the AI's
    /// snapshot are used; no target hand or engine zone is inspected.
    /// </summary>
    public (int? TargetSeat, AiThoughtRecord Thought) ChooseJiemingTarget(
        GameSnapshot view,
        IReadOnlyList<int> targetSeats,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var selfRole = self.Role ?? throw new InvalidOperationException("An AI must see its own role.");
        var candidates = targetSeats
            .Select(targetSeat => view.Players.Single(player => player.Seat == targetSeat))
            .Where(player => player.IsAlive && player.HandCount < player.MaxHp)
            .Select(target =>
            {
                var deficit = target.MaxHp - target.HandCount;
                var roleBonus = target.Role switch
                {
                    Role.Lord when selfRole is Role.Lord or Role.Loyalist => 8d,
                    Role.Loyalist when selfRole is Role.Lord or Role.Loyalist => 4d,
                    Role.Rebel when selfRole == Role.Rebel => 4d,
                    _ => 0d
                };
                var score = Math.Round(
                    deficit * 20d + roleBonus + _random.NextDouble() * 0.001d,
                    3);
                return new AiCandidateScore(
                    new LegalAction(
                        LegalActionKind.Jieming,
                        null,
                        target.Seat,
                        $"令座位 {target.Seat + 1} 摸牌至体力上限"),
                    score,
                    $"按公开手牌数量补足 {deficit} 张；不读取目标隐藏牌面。 ");
            })
            .ToList();
        candidates.Add(new AiCandidateScore(
            new LegalAction(LegalActionKind.SkipJieming, null, null, "不发动节命"),
            self.HandCount > self.MaxHp + 2 ? 36d : 2d,
            "保留当前结算；只使用自己的公开手牌数量。"));

        var selected = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Action.TargetSeat ?? int.MaxValue)
            .First();
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Action.Description,
            candidates.OrderByDescending(candidate => candidate.Score).ToArray(),
            $"节命：从 {candidates.Count - 1} 个公开合法目标中选择 {selected.Action.Description}。 ");
        return (
            selected.Action.Kind == LegalActionKind.Jieming
                ? selected.Action.TargetSeat
                : null,
            thought);
    }

    /// <summary>
    /// Chooses the discard cost for the cross-seat Yuanhu trigger. The target
    /// is already fixed by the damage event; only the owner's hand and public
    /// target HP/role data are consulted.
    /// </summary>
    public (int? CardId, AiThoughtRecord Thought) ChooseYuanhuCard(
        GameSnapshot view,
        IReadOnlyList<int> cardIds,
        int targetSeat,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var target = view.Players.Single(player => player.Seat == targetSeat);
        var missingHp = Math.Max(0, target.MaxHp - target.Hp);
        var selfRole = self.Role ?? throw new InvalidOperationException("An AI must see its own role.");
        var candidates = cardIds
            .Select(cardId => self.Hand.SingleOrDefault(card => card.Id == cardId))
            .Where(card => card is not null)
            .Select(card =>
            {
                var profile = CardCatalog.Get(card!.Kind);
                var roleBonus = target.Role switch
                {
                    Role.Lord when selfRole is Role.Lord or Role.Loyalist => 12d,
                    Role.Loyalist when selfRole is Role.Lord or Role.Loyalist => 6d,
                    Role.Rebel when selfRole == Role.Rebel => 6d,
                    _ => 0d
                };
                var score = missingHp * 35d + roleBonus - profile.HandKeepValue * 0.15d;
                if (self.HandCount > self.MaxHp)
                {
                    score += 8d;
                }

                return new AiCandidateScore(
                    new LegalAction(
                        LegalActionKind.Yuanhu,
                        card.Id,
                        targetSeat,
                        $"弃置【{profile.DisplayName}】令座位 {targetSeat + 1} 回复 1 点体力"),
                    Math.Round(score + _random.NextDouble() * 0.001d, 3),
                    $"目标公开缺少 {missingHp} 点体力；选择一张自己的手牌作为代价，不读取目标手牌。 ");
            })
            .ToList();
        candidates.Add(new AiCandidateScore(
            new LegalAction(LegalActionKind.SkipYuanhu, null, targetSeat, "不发动援护"),
            self.HandCount <= self.MaxHp ? 5d : 1d,
            "保留自己的手牌；只使用公开体力差和自己的手牌数量。"));

        var selected = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Action.CardId ?? int.MaxValue)
            .First();
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Action.Description,
            candidates.OrderByDescending(candidate => candidate.Score).ToArray(),
            $"援护：目标公开缺少 {missingHp} 点体力，选择 {selected.Action.Description}。 ");
        return (
            selected.Action.Kind == LegalActionKind.Yuanhu ? selected.Action.CardId : null,
            thought);
    }

    public (bool UseSlash, AiThoughtRecord Thought) ChooseDuelResponse(
        GameSnapshot view,
        int opponentSeat,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var slashProfile = CardCatalog.Get(CardKind.Slash);
        var useScore = self.Hp <= 1
            ? slashProfile.AiResponseValue + 40d
            : slashProfile.AiResponseValue + Math.Max(0, self.MaxHp - self.Hp) * 4d;
        var passScore = self.Hp <= 1 ? -100d : 5d;
        var useSlash = useScore >= passScore;
        var pseudoAction = new LegalAction(
            LegalActionKind.Slash,
            null,
            opponentSeat,
            useSlash ? "打出杀应战决斗" : "不打出杀");
        var candidates = new[]
        {
            new AiCandidateScore(
                pseudoAction,
                useSlash ? useScore : passScore,
                "决斗要求交替出杀；只使用本座可见的手牌和体力信息。")
        };
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            pseudoAction.Description,
            candidates,
            $"决斗响应：生命值 {self.Hp}/{self.MaxHp}，决定{pseudoAction.Description}。 ");
        return (useSlash, thought);
    }

    public (bool UseSlash, AiThoughtRecord Thought) ChooseGroupResponse(
        GameSnapshot view,
        int opponentSeat,
        int thoughtSequence)
    {
        var (useResponse, thought) = ChooseGroupResponse(
            view,
            opponentSeat,
            CardKind.BarbarianAssault,
            CardKind.Slash,
            thoughtSequence);
        return (useResponse, thought);
    }

    public (bool UseResponse, AiThoughtRecord Thought) ChooseGroupResponse(
        GameSnapshot view,
        int opponentSeat,
        CardKind incomingCard,
        CardKind requiredCardKind,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var responseProfile = CardCatalog.Get(requiredCardKind);
        var useScore = self.Hp <= 1
            ? responseProfile.AiResponseValue + 40d
            : responseProfile.AiResponseValue + Math.Max(0, self.MaxHp - self.Hp) * 4d;
        var passScore = self.Hp <= 1 ? -100d : 5d;
        var useResponse = useScore >= passScore;
        var incomingName = CardCatalog.Get(incomingCard).DisplayName;
        var responseName = CardCatalog.Get(requiredCardKind).DisplayName;
        var responseText = requiredCardKind == CardKind.Dodge
            ? $"打出闪响应{incomingName}"
            : $"打出杀响应{incomingName}";
        var pseudoAction = new LegalAction(
            requiredCardKind == CardKind.Slash
                ? LegalActionKind.Slash
                : LegalActionKind.EndPlay,
            null,
            opponentSeat,
            useResponse ? responseText : $"不打出{responseName}");
        var candidates = new[]
        {
            new AiCandidateScore(
                pseudoAction,
                useResponse ? useScore : passScore,
                $"{incomingName}要求逐个角色响应{responseName}；只使用本座可见的手牌和体力信息。")
        };
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            pseudoAction.Description,
            candidates,
            $"{incomingName}响应：生命值 {self.Hp}/{self.MaxHp}，决定{pseudoAction.Description}。 ");
        return (useResponse, thought);
    }

    /// <summary>
    /// Chooses one card from the public FiveGrains reveal. The option list is
    /// supplied by the current public prompt; no engine zone or another
    /// player's hand is consulted here.
    /// </summary>
    public (int CardId, AiThoughtRecord Thought) ChooseHarvestCard(
        GameSnapshot view,
        IReadOnlyList<CardSnapshot> options,
        int thoughtSequence)
    {
        if (options.Count == 0)
        {
            throw new InvalidOperationException("AI was asked to harvest from an empty public reveal.");
        }

        var candidates = options
            .Select(option =>
            {
                var profile = CardCatalog.Get(option.Kind);
                var score = Math.Round(profile.HandKeepValue + _random.NextDouble() * 0.001d, 3);
                var action = new LegalAction(
                    LegalActionKind.SelectHarvestCard,
                    option.Id,
                    null,
                    $"选择公开牌【{option.DisplayName}】");
                return new AiCandidateScore(
                    action,
                    score,
                    $"公开选牌价值 {profile.HandKeepValue}；只使用五谷丰登展示区中的牌。 ");
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Action.CardId ?? int.MaxValue)
            .ToArray();
        var selected = candidates[0];
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Action.Description,
            candidates,
            $"五谷丰登：从 {options.Count} 张公开牌中选择 {selected.Action.Description}（{selected.Score:0.###} 分）。");
        return (selected.Action.CardId ?? throw new InvalidOperationException("Harvest choice has no card id."), thought);
    }

    /// <summary>
    /// Chooses which private hand card to reveal for FireAttack. The candidate
    /// ids are supplied by the target's private prompt; the AI never asks the
    /// engine for another player's hand or deck state.
    /// </summary>
    public (int CardId, AiThoughtRecord Thought) ChooseFireAttackReveal(
        GameSnapshot view,
        IReadOnlyList<int> candidateCardIds,
        int sourceSeat,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var cards = candidateCardIds
            .Select(cardId => self.Hand.Single(card => card.Id == cardId))
            .ToArray();
        if (cards.Length == 0)
        {
            throw new InvalidOperationException("AI was asked to reveal from an empty FireAttack hand.");
        }

        var candidates = cards
            .Select(card =>
            {
                var profile = CardCatalog.Get(card.Kind);
                var score = Math.Round(-profile.HandKeepValue + _random.NextDouble() * 0.001d, 3);
                return new AiCandidateScore(
                    new LegalAction(
                        LegalActionKind.FireAttackReveal,
                        card.Id,
                        sourceSeat,
                        $"展示【{card.DisplayName}】"),
                    score,
                    "从自己的私有手牌中选择较低保留价值的牌展示；不读取攻击者手牌。 ");
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Action.CardId ?? int.MaxValue)
            .ToArray();
        var selected = candidates[0];
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Action.Description,
            candidates,
            $"火攻：从自己的 {cards.Length} 张手牌中选择 {selected.Action.Description} 展示。");
        return (selected.Action.CardId ?? throw new InvalidOperationException("FireAttack reveal has no card id."), thought);
    }

    /// <summary>
    /// Chooses whether the source spends a private same-suit card after the
    /// FireAttack reveal. Only the published matching ids and the source's own
    /// snapshot are consulted.
    /// </summary>
    public (int? CardId, AiThoughtRecord Thought) ChooseFireAttackDiscard(
        GameSnapshot view,
        int targetSeat,
        Suit revealedSuit,
        IReadOnlyList<int> matchingCardIds,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var target = view.Players.Single(player => player.Seat == targetSeat);
        var selfRole = self.Role ?? throw new InvalidOperationException("An AI must see its own role.");
        var hostility = GetHostility(view, selfRole, target);
        var useBase = 54d + hostility + (target.Hp <= 1 ? 18d : 0d);
        var candidates = matchingCardIds
            .Select(cardId => self.Hand.Single(card => card.Id == cardId))
            .Select(card =>
            {
                var profile = CardCatalog.Get(card.Kind);
                var score = Math.Round(
                    useBase - profile.HandKeepValue * 0.35d + _random.NextDouble() * 0.001d,
                    3);
                return new AiCandidateScore(
                    new LegalAction(
                        LegalActionKind.FireAttackDiscard,
                        card.Id,
                        targetSeat,
                        $"弃置【{card.DisplayName}】造成火攻伤害"),
                    score,
                    $"弃置自己可见的{revealedSuit}牌造成火焰伤害；目标手牌数量和身份只按公开快照判断。");
            })
            .ToList();
        candidates.Add(new AiCandidateScore(
            new LegalAction(
                LegalActionKind.SkipFireAttack,
                null,
                targetSeat,
                "不弃置同花色牌，火攻不造成伤害"),
            20d,
            "保留自己的同花色手牌；不读取目标隐藏手牌。"));

        var selected = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Action.CardId ?? int.MaxValue)
            .First();
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Action.Description,
            candidates,
            $"火攻：目标公开了 {revealedSuit} 牌，决定{selected.Action.Description}（{selected.Score:0.###} 分）。");
        return (
            selected.Action.Kind == LegalActionKind.FireAttackDiscard
                ? selected.Action.CardId
                : null,
            thought);
    }

    /// <summary>
    /// Chooses whether to spend one private Peach during a dying response window.
    /// The victim role is read only from the responder's filtered snapshot; a
    /// hidden victim is never resolved from engine state here.
    /// </summary>
    public (bool UsePeach, int? PeachCardId, AiThoughtRecord Thought) ChooseDyingResponse(
        GameSnapshot view,
        int victimSeat,
        IReadOnlyList<Card> peaches,
        int thoughtSequence)
    {

        var result = ChooseDyingResponseWithAlcohol(
            view,
            victimSeat,
            peaches,
            [],
            thoughtSequence);
        return (result.UsePeach, result.PeachCardId, result.Thought);
    }

    /// <summary>
    /// Chooses Peach for any legal responder and Alcohol only when the responder
    /// is the dying holder. Both the view and the private card lists are scoped
    /// to this AI seat; no other hand is inspected.
    /// </summary>
    public (bool UsePeach, int? PeachCardId, bool UseAlcohol, int? AlcoholCardId, AiThoughtRecord Thought)
        ChooseDyingResponseWithAlcohol(
        GameSnapshot view,
        int victimSeat,
        IReadOnlyList<Card> peaches,
        IReadOnlyList<Card> alcohols,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var victim = view.Players.Single(player => player.Seat == victimSeat);
        var selfRole = self.Role ?? throw new InvalidOperationException("An AI must see its own role.");
        var useScore = ScoreDyingResponse(selfRole, self.Seat, victim);
        var candidates = peaches
            .Select(peach => new AiCandidateScore(
                new LegalAction(
                    LegalActionKind.Peach,
                    peach.Id,
                    victimSeat,
                    $"使用桃救援 {victim.Name}"),
                Math.Round(useScore + _random.NextDouble() * 0.001d, 3),
                "消耗一张自己的桃，使濒死角色回到 1 点体力。"))
            .ToList();
        if (self.Seat == victimSeat)
        {
            candidates.AddRange(alcohols.Select(alcohol => new AiCandidateScore(
                new LegalAction(
                    LegalActionKind.Alcohol,
                    alcohol.Id,
                    victimSeat,
                    $"使用酒自救 {victim.Name}"),
                Math.Round(useScore - 1d + _random.NextDouble() * 0.001d, 3),
                "仅在自己濒死时消耗一张酒，回复 1 点体力。")));
        }
        candidates.Add(new AiCandidateScore(
            new LegalAction(
                LegalActionKind.EndPlay,
                null,
                victimSeat,
                $"不救援 {victim.Name}"),
            0d,
            "保留手牌；濒死响应只使用自己的可见手牌。"));

        var selected = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Action.CardId ?? int.MaxValue)
            .First();
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Action.Description,
            candidates.OrderByDescending(candidate => candidate.Score).ToArray(),
            $"濒死响应：{selected.Action.Description}（{selected.Score:0.###} 分）。");
        return (
            selected.Action.Kind == LegalActionKind.Peach,
            selected.Action.Kind == LegalActionKind.Peach ? selected.Action.CardId : null,
            selected.Action.Kind == LegalActionKind.Alcohol,
            selected.Action.Kind == LegalActionKind.Alcohol ? selected.Action.CardId : null,
            thought);
    }
    private static (double Score, string Reason) ScoreGeneral(
        Role role,
        GeneralDefinition candidate)
    {
        var score = candidate.Skill switch
        {
            SkillKind.Jianxiong => role == Role.Lord ? 42d : 26d,
            SkillKind.Paoxiao => role == Role.Rebel ? 40d : 24d,
            SkillKind.Yingzi => role == Role.Renegade ? 39d : 30d,
            SkillKind.Kongcheng => role == Role.Loyalist ? 34d : 25d,
            SkillKind.Feedback => role == Role.Lord ? 34d : 31d,
            SkillKind.Wusheng => role == Role.Rebel ? 38d : 30d,
            SkillKind.Longdan => role == Role.Rebel ? 37d : 30d,
            SkillKind.Yiji => role == Role.Loyalist ? 39d : 34d,
            SkillKind.Jieming => role is Role.Lord or Role.Loyalist ? 40d : 35d,
            SkillKind.Yuanhu => role is Role.Lord or Role.Loyalist ? 38d : 33d,
            _ => 12d
        };
        var reason = candidate.Skill switch
        {
            SkillKind.Jianxiong => "伤害后取得牌，适合持续制造资源优势。",
            SkillKind.Paoxiao => "不受杀次数限制，适合主动施压。",
            SkillKind.Yingzi => "额外摸牌，稳定扩大资源。",
            SkillKind.Kongcheng => "空手时降低被杀风险。",
            SkillKind.Feedback => "受伤后取得伤害牌，适合在处理区中获取资源。",
            SkillKind.Wusheng => "红色牌转化为杀，适合主动施压。",
            SkillKind.Longdan => "杀闪互转，既能主动施压也能保留响应空间。",
            SkillKind.Yiji => "受伤后摸牌并向其他角色分配资源，适合建立协作优势。",
            SkillKind.Jieming => "受伤后按公开手牌数量补足一名角色，适合稳住阵营资源。",
            SkillKind.Yuanhu => "其他角色受伤后可用自己的手牌换取公开回复，适合保护队友。",
            _ => "当前演示版没有主动技能，作为稳定基础候选。"
        };
        return (score, reason);
    }

    private static double ScoreDyingResponse(
        Role selfRole,
        int selfSeat,
        PlayerSnapshot victim)
    {
        if (victim.Seat == selfSeat)
        {
            return 120d;
        }

        return victim.Role switch
        {
            Role.Lord when selfRole is Role.Lord or Role.Loyalist => 105d,
            Role.Lord when selfRole == Role.Rebel => -80d,
            Role.Loyalist when selfRole is Role.Lord or Role.Loyalist => 80d,
            Role.Loyalist when selfRole == Role.Rebel => -45d,
            Role.Rebel when selfRole == Role.Rebel => 70d,
            Role.Rebel when selfRole is Role.Lord or Role.Loyalist => -35d,
            Role.Renegade when selfRole == Role.Renegade => 65d,
            Role.Renegade => -20d,
            _ => -5d
        };
    }

    private (double Score, string Reason) ScoreAction(
        GameSnapshot view,
        PlayerSnapshot self,
        Role selfRole,
        LegalAction action)
    {
        if (action.Kind == LegalActionKind.EndPlay)
        {
            return (0d, "结束出牌是所有局面的保底动作。");
        }

        var card = self.Hand.Single(candidate => candidate.Id == action.CardId);
        var playedCardKind = action.PlayedCardKind ?? card.Kind;
        var cardProfile = CardCatalog.Get(playedCardKind);

        if (action.Kind == LegalActionKind.Equip)
        {
            var equipment = EquipmentCatalog.Get(card.Kind);
            var existing = self.Equipment.FirstOrDefault(existingCard =>
                EquipmentCatalog.Get(existingCard.Kind).Slot == equipment.Slot);
            var replacementAdjustment = existing is null ? 8d : -6d;
            return (
                cardProfile.AiPlayValue + replacementAdjustment,
                existing is null
                    ? $"装备{equipment.DisplayName}，启用{EquipmentCatalog.GetSlotName(equipment.Slot)}修正；只读取公开装备状态。"
                    : $"用{equipment.DisplayName}替换{EquipmentCatalog.GetSlotName(equipment.Slot)}上的公开装备；只读取公开装备状态。");
        }

        if (action.Kind == LegalActionKind.Peach)
        {
            var missingHp = self.MaxHp - self.Hp;
            var score = missingHp * cardProfile.AiPlayValue + (self.Hp <= 1 ? 50d : 0d);
            return (score, $"回复体力；当前已损失 {missingHp} 点体力。");
        }

        if (action.Kind == LegalActionKind.DrawTwo)
        {
            var availableDraws = Math.Min(2, view.DrawPileCount + view.DiscardPileCount);
            var score = cardProfile.AiPlayValue + availableDraws * 4d;
            return (
                score,
                $"摸 {availableDraws} 张牌；牌差策略值 {cardProfile.AiPlayValue:0.#}，只使用公开牌堆数量。");
        }

        if (action.Kind == LegalActionKind.PeachGarden)
        {
            var recoverableCount = view.Players.Count(player =>
                player.IsAlive && player.Hp < player.MaxHp);
            var score = recoverableCount == 0
                ? -15d
                : cardProfile.AiPlayValue + recoverableCount * 6d;
            return (
                score,
                $"使 {recoverableCount} 名受伤角色回复体力；群体恢复策略值 {cardProfile.AiPlayValue:0.#}，只使用公开体力信息。");
        }

        if (action.Kind == LegalActionKind.FiveGrains)
        {
            var pickerCount = view.Players.Count(player => player.IsAlive);
            var score = cardProfile.AiPlayValue + pickerCount * 4d;
            return (
                score,
                $"公开展示并让 {pickerCount} 名存活角色依次选牌；公共选牌策略值 {cardProfile.AiPlayValue:0.#}，不读取隐藏牌堆顺序。");
        }

        if (action.Kind == LegalActionKind.BarbarianAssault)
        {
            var targetCount = view.Players.Count(player =>
                player.IsAlive && player.Seat != self.Seat);
            var score = cardProfile.AiPlayValue + targetCount * 3d;
            return (
                score,
                $"对 {targetCount} 名存活角色依次施压；群体牌策略值 {cardProfile.AiPlayValue:0.#}，只使用公开存活信息。");
        }

        if (action.Kind == LegalActionKind.ArrowBarrage)
        {
            var targetCount = view.Players.Count(player =>
                player.IsAlive && player.Seat != self.Seat);
            var score = cardProfile.AiPlayValue + targetCount * 3d;
            return (
                score,
                $"对 {targetCount} 名存活角色依次施压；群体闪响应牌策略值 {cardProfile.AiPlayValue:0.#}，只使用公开存活信息。");
        }

        if (action.Kind == LegalActionKind.Alcohol)
        {
            var slashCount = self.Hand.Count(card =>
                IsSlashCard(card.Kind) ||
                self.Skill == SkillKind.Wusheng && IsRedCard(card.Suit) ||
                self.Skill == SkillKind.Longdan && card.Kind == CardKind.Dodge);
            var score = slashCount == 0
                ? -10d
                : cardProfile.AiPlayValue + Math.Min(slashCount, 2) * 10d;
            return (
                score,
                $"为下一张杀准备 +1 伤害；当前可见手牌中有 {slashCount} 张杀，不读取其他玩家暗牌。");
        }

        var target = view.Players.Single(player => player.Seat == action.TargetSeat);
        var hostility = GetHostility(view, selfRole, target);
        if (action.Kind == LegalActionKind.Dismantlement)
        {
            var handPressure = Math.Min(target.HandCount, 5) * 4d;
            return (
                cardProfile.AiPlayValue + hostility + handPressure,
                $"盲弃置目标一张手牌；只使用公开手牌数量 {target.HandCount} 和身份敌对值，不读取目标暗牌。");
        }

        if (action.Kind == LegalActionKind.Snatch)
        {
            var handPressure = Math.Min(target.HandCount, 5) * 5d;
            return (
                cardProfile.AiPlayValue + hostility + handPressure,
                $"从距离 1 的目标盲取一张手牌；只使用公开手牌数量 {target.HandCount} 和身份敌对值，不读取目标暗牌。");
        }

        if (action.Kind == LegalActionKind.FireAttack)
        {
            var handPressure = Math.Min(target.HandCount, 5) * 5d;
            var fireFinishingBonus = target.Hp <= 1 ? 20d : 0d;
            return (
                cardProfile.AiPlayValue + hostility + handPressure + fireFinishingBonus,
                $"迫使目标展示一张手牌并尝试同花色火焰伤害；只使用公开手牌数量 {target.HandCount} 和身份敌对值，不读取双方暗牌。");
        }

        var finishingBonus = target.Hp <= 1 ? 28d : 0d;
        var pressureBonus = Math.Max(0, target.MaxHp - target.Hp) * 3d;
        var conversion = action.PlayedCardKind is { } &&
                         !IsSlashCard(card.Kind)
            ? $"；将{CardCatalog.Get(card.Kind).DisplayName}当作杀使用"
            : string.Empty;
        return (
            cardProfile.AiPlayValue + hostility + finishingBonus + pressureBonus,
            $"卡牌策略值 {cardProfile.AiPlayValue:0.#}，目标敌对值 {hostility:0.#}，低体力收益 {finishingBonus:0.#}{conversion}。身份判断只使用公开信息。");
    }

    private static bool IsSlashCard(CardKind kind) =>
        kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;

    private static bool IsRedCard(Suit suit) =>
        suit is Suit.Heart or Suit.Diamond;

    private double GetHostility(GameSnapshot view, Role selfRole, PlayerSnapshot target)
    {
        var visibleRole = target.Role;
        var suspicion = _rebelSuspicion.GetValueOrDefault(target.Seat);

        return selfRole switch
        {
            Role.Rebel => visibleRole switch
            {
                Role.Lord => 100d,
                Role.Loyalist => 65d,
                Role.Rebel => -100d,
                Role.Renegade => 20d,
                _ => 18d - suspicion * 2d
            },
            Role.Lord or Role.Loyalist => visibleRole switch
            {
                Role.Lord => -1000d,
                Role.Loyalist => -100d,
                Role.Rebel => 100d,
                Role.Renegade => 55d,
                _ => 16d + suspicion * 14d
            },
            Role.Renegade => GetRenegadeHostility(view, target, visibleRole, suspicion),
            _ => 0d
        };
    }

    private static double GetRenegadeHostility(
        GameSnapshot view,
        PlayerSnapshot target,
        Role? visibleRole,
        double suspicion)
    {
        var aliveCount = view.Players.Count(player => player.IsAlive);
        if (visibleRole == Role.Lord)
        {
            // The Renegade loses if the Lord dies before becoming the sole opponent.
            return aliveCount <= 2 ? 120d : -80d;
        }

        if (visibleRole == Role.Rebel)
        {
            return 55d;
        }

        if (visibleRole == Role.Loyalist)
        {
            return 50d;
        }

        // In the hidden-information opening, pressure the healthier unknown players
        // while mildly preferring people who acted against the Lord.
        return 28d + target.Hp * 2d + suspicion * 2d;
    }
}
