namespace CardGame.Core;

/// <summary>
/// A deliberately small, inspectable heuristic AI. It receives the same filtered
/// snapshot as a human player: its own role and hand, the public Lord/dead roles,
/// public team/faction information, and only other players' hand counts.
/// </summary>
public sealed partial class SimpleAiBrain
{
    private readonly Dictionary<int, double> _rebelSuspicion = [];
    private readonly Dictionary<int, double> _nationalEnemySuspicion = [];
    private readonly DeterministicRandom _random;
    private readonly int _policyVersion;
    private readonly HashSet<int> _recastCardsThisTurn = [];
    private int _recastTurn = -1;

    public SimpleAiBrain(int seat, int seed, int policyVersion = 1)
    {
        if (policyVersion is not (1 or 2 or 3))
            throw new ArgumentOutOfRangeException(nameof(policyVersion));
        Seat = seat;
        _policyVersion = policyVersion;
        _random = new DeterministicRandom(seed == 0 ? seat + 1 : seed);
    }

    public int Seat { get; }

    public void ObserveRecast(int turnNumber, int cardId)
    {
        if (_recastTurn != turnNumber) { _recastTurn = turnNumber; _recastCardsThisTurn.Clear(); }
        _recastCardsThisTurn.Add(cardId);
    }

    public IReadOnlyDictionary<int, double> RebelSuspicion => _rebelSuspicion;

    /// <summary>
    /// Public-evidence confidence for a hidden national-war seat. Positive values
    /// mean the seat is more likely to be an enemy of this AI's faction; the
    /// bounded value is diagnostic state, not a player-visible projection.
    /// </summary>
    public IReadOnlyDictionary<int, double> NationalEnemySuspicion => _nationalEnemySuspicion;

    /// <summary>
    /// Records one public national-war attack. The caller must provide only
    /// factions that are already public to every observer; the observer's own
    /// faction is its private player-view input. No hidden target faction, hand,
    /// deck order or resolution state is accepted by this boundary.
    /// </summary>
    public void ObserveNationalAttack(
        int sourceSeat,
        int targetSeat,
        string? observerFactionId,
        string? visibleSourceFactionId,
        string? visibleTargetFactionId)
    {
        if (_policyVersion < 2 || sourceSeat == Seat || sourceSeat == targetSeat ||
            string.IsNullOrWhiteSpace(observerFactionId))
        {
            return;
        }

        if (targetSeat == Seat ||
            string.Equals(visibleTargetFactionId, observerFactionId, StringComparison.Ordinal))
        {
            AddNationalEnemySuspicion(sourceSeat, 2d);
        }
        else if (visibleSourceFactionId is null && visibleTargetFactionId is not null &&
                 !string.Equals(visibleTargetFactionId, observerFactionId, StringComparison.Ordinal))
        {
            // An unknown source attacking a publicly known enemy is weak evidence
            // that the source may be an ally. Keep it deliberately conservative in
            // a three-faction table where the third faction is still possible.
            AddNationalEnemySuspicion(sourceSeat, -0.75d);
        }

        if (string.Equals(visibleSourceFactionId, observerFactionId, StringComparison.Ordinal) &&
            visibleTargetFactionId is null)
        {
            AddNationalEnemySuspicion(targetSeat, 1.5d);
        }
    }

    private void AddNationalEnemySuspicion(int seat, double delta)
    {
        _nationalEnemySuspicion[seat] = Math.Clamp(
            _nationalEnemySuspicion.GetValueOrDefault(seat) + delta,
            -6d,
            6d);
    }

    /// <summary>
    /// Observes a public hostile action in identity mode. The action kind is
    /// intentionally not part of this first-pass model: Slash, Duel, group
    /// attacks and FireAttack all expose the same source/target role evidence.
    /// The engine supplies only the Lord seat and any role already public at
    /// the time of the action; it never supplies hidden roles or card state.
    /// </summary>
    public void ObservePublicAttack(int sourceSeat, int targetSeat, int lordSeat, Role? revealedTargetRole) =>
        ObservePublicAttack(new PublicAttackEvidence(
            PublicAttackKind.Slash,
            sourceSeat,
            targetSeat,
            lordSeat,
            revealedTargetRole));

    /// <summary>
    /// Consumes one typed public attack observation. The kind is part of the
    /// public vocabulary even though the current first-pass identity model
    /// gives all hostile actions the same role signal.
    /// </summary>
    public void ObservePublicAttack(PublicAttackEvidence evidence)
    {
        var sourceSeat = evidence.SourceSeat;
        var targetSeat = evidence.TargetSeat;
        var lordSeat = evidence.LordSeat;
        var revealedTargetRole = evidence.RevealedTargetRole;
        if (evidence.Kind == PublicAttackKind.FireAttack && _policyVersion < 3)
        {
            return;
        }
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
        if (_policyVersion >= 2)
        {
            if (targetSeat != lordSeat && revealedTargetRole is null && _rebelSuspicion.GetValueOrDefault(targetSeat) < -1d)
                _rebelSuspicion[sourceSeat] += Math.Min(1.5d, -_rebelSuspicion[targetSeat] * 0.35d);
            _rebelSuspicion[sourceSeat] = Math.Clamp(_rebelSuspicion[sourceSeat], -6d, 6d);
        }
    }

    public void ObserveSlash(int sourceSeat, int targetSeat, int lordSeat, Role? revealedTargetRole) =>
        ObservePublicAttack(new PublicAttackEvidence(
            PublicAttackKind.Slash,
            sourceSeat,
            targetSeat,
            lordSeat,
            revealedTargetRole));

    public void ObserveFireAttack(int sourceSeat, int targetSeat, int lordSeat, Role? revealedTargetRole) =>
        ObserveFireAttack(new PublicAttackEvidence(
            PublicAttackKind.FireAttack,
            sourceSeat,
            targetSeat,
            lordSeat,
            revealedTargetRole));

    public void ObserveFireAttack(PublicAttackEvidence evidence)
    {
        // FireAttack evidence is a v3 extension. Keeping v1/v2 untouched is
        // required for their existing command journals and checkpoints.
        if (evidence.Kind == PublicAttackKind.FireAttack)
        {
            ObservePublicAttack(evidence);
        }
    }

    /// <summary>
    /// Duel is a public hostile action with the same first-pass evidence signal
    /// as Slash. Keeping this adapter explicit lets the suspicion model evolve
    /// without making GameEngine reach into its private state.
    /// </summary>
    public void ObserveDuel(int sourceSeat, int targetSeat, int lordSeat, Role? revealedTargetRole) =>
        ObservePublicAttack(new PublicAttackEvidence(
            PublicAttackKind.Duel,
            sourceSeat,
            targetSeat,
            lordSeat,
            revealedTargetRole));

    /// <summary>
    /// A group attack exposes the same public hostile-action signal as Slash. The
    /// explicit adapter keeps the engine's content vocabulary separate from the
    /// suspicion model while preserving one information boundary.
    /// </summary>
    public void ObserveGroupAttack(int sourceSeat, int targetSeat, int lordSeat, Role? revealedTargetRole) =>
        ObservePublicAttack(new PublicAttackEvidence(
            PublicAttackKind.GroupAttack,
            sourceSeat,
            targetSeat,
            lordSeat,
            revealedTargetRole));

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
        if (_policyVersion >= 2) _rebelSuspicion[killerSeat.Value] = Math.Clamp(_rebelSuspicion[killerSeat.Value], -6d, 6d);
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
        // National-war snapshots intentionally have no identity Role. The
        // fallback only preserves the legacy scorer's neutral shape; targeting
        // is decided from the public/private faction fields below.
        var selfRole = self.Role ?? Role.Renegade;
        var candidates = new List<AiCandidateScore>(legalActions.Count);

        foreach (var action in legalActions)
        {
            var (score, reason) = action.Kind == LegalActionKind.RevealGeneral
                ? ScoreNationalRevealAction(view, self, action)
                : _policyVersion >= 2
                    ? ScoreTacticalAction(view, self, selfRole, action, legalActions)
                    : ScoreAction(view, self, selfRole, action);
            // A very small seeded jitter resolves exact ties without making replays unstable.
            score += _random.NextDouble() * 0.001d;
            candidates.Add(new AiCandidateScore(action, Math.Round(score, 3), reason));
        }

        var selected = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Action.CardId ?? int.MaxValue)
            .ThenBy(candidate => candidate.Action.TargetSeat ?? int.MaxValue)
            .ThenBy(candidate => string.Join(',', candidate.Action.TargetSeats))
            .ThenBy(candidate => candidate.Action.TargetCardId ?? int.MaxValue)
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
    /// Selects the smallest legal private card subset for an active-skill
    /// action. The engine passes only this AI's filtered snapshot, so the
    /// selection cannot inspect another player's hand or the hidden deck.
    /// </summary>
    public IReadOnlyList<int> ChooseActiveSkillCards(
        GameSnapshot view,
        LegalAction action)
    {
        if (action.Kind is not (LegalActionKind.UseSkill or LegalActionKind.UseEquipmentEffect))
        {
            return [];
        }

        if (action.MinCardCount < 0 || action.MaxCardCount < action.MinCardCount)
        {
            throw new InvalidOperationException("The active-skill card selection bounds are invalid.");
        }

        var self = view.Players.Single(player => player.Seat == Seat);
        var selectableCards = GetActiveSkillSelectableCards(self, action);
        if (action.Skill == SkillKind.Qiangxi)
        {
            return selectableCards
                .OrderBy(card => CardCatalog.Get(card.Kind).HandKeepValue)
                .ThenBy(card => card.Id)
                .Take(1)
                .Select(card => card.Id)
                .ToArray();
        }

        if (action.Skill == SkillKind.Luanji)
        {
            return selectableCards
                .GroupBy(card => card.Suit)
                .Where(group => group.Count() >= 2)
                .Select(group => group
                    .OrderBy(card => CardCatalog.Get(card.Kind).HandKeepValue)
                    .ThenBy(card => card.Id)
                    .Take(2)
                    .ToArray())
                .OrderBy(pair => pair.Sum(card => CardCatalog.Get(card.Kind).HandKeepValue))
                .ThenBy(pair => pair[0].Id)
                .FirstOrDefault()?
                .Select(card => card.Id)
                .ToArray() ?? [];
        }

        if (action.MinCardCount == 0)
        {
            return [];
        }

        if (action.MinCardCount > selectableCards.Count)
        {
            throw new InvalidOperationException("The active skill requires more cards than the AI owns.");
        }

        return selectableCards
            .OrderBy(card => CardCatalog.Get(card.Kind).HandKeepValue)
            .ThenBy(card => card.Id)
            .Take(action.MinCardCount)
            .Select(card => card.Id)
            .ToArray();
    }

    public PromptChoice ChooseJujianOwnerChoice(
        GameSnapshot view,
        IReadOnlyList<PromptChoice> choices)
    {
        var skip = choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "jujian-skip");
        var self = view.Players.Single(player => player.Seat == Seat);
        var selfRole = self.Role ?? Role.Renegade;
        var ownedCards = self.Hand.Concat(self.Equipment).ToDictionary(card => card.Id);
        var scored = choices
            .Where(choice => choice.Parameters.GetValueOrDefault("action") == "jujian-use" &&
                             choice.Cards.Count == 1 && choice.Targets.Count == 1 &&
                             ownedCards.ContainsKey(choice.Cards[0]))
            .Select(choice =>
            {
                var target = view.Players.Single(player => player.Seat == choice.Targets[0]);
                var support = -GetHostility(view, selfRole, target);
                var need = (target.IsFaceDown || target.IsChained ? 30d : 0d) +
                           (target.Hp < target.MaxHp ? 15d : 0d);
                var cost = CardCatalog.Get(ownedCards[choice.Cards[0]].Kind).HandKeepValue;
                return new { Choice = choice, Support = support, Score = support + need - cost * .35d };
            })
            .Where(candidate => candidate.Support > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Choice.Cards[0])
            .ThenBy(candidate => candidate.Choice.Targets[0])
            .FirstOrDefault();
        return scored?.Choice ?? skip;
    }

    public PromptChoice ChooseJujianBenefit(
        GameSnapshot view,
        IReadOnlyList<PromptChoice> choices)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        PromptChoice? Find(string action) => choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("action") == action);
        if ((self.IsFaceDown || self.IsChained) && Find("jujian-restore") is { } restore)
        {
            return restore;
        }
        if (self.Hp < self.MaxHp && self.Hp <= 2 && Find("jujian-recover") is { } recover)
        {
            return recover;
        }
        return Find("jujian-draw") ?? Find("jujian-recover") ??
               throw new InvalidOperationException("Jujian published no legal target benefit.");
    }

    private static IReadOnlyList<CardSnapshot> GetActiveSkillSelectableCards(
        PlayerSnapshot self,
        LegalAction action)
    {
        if (action.SelectableCardIds.Count == 0)
        {
            return [];
        }

        var selectable = action.SelectableCardIds.ToHashSet();
        return self.Hand
            .Concat(self.WoodenOxGrain ?? [])
            .Concat(self.Equipment)
            .Where(card => selectable.Contains(card.Id))
            .ToArray();
    }

    /// <summary>
    /// Selects the smallest legal target subset for a target-selecting active
    /// skill. Rende targets one other living character; Qingnang targets one
    /// wounded living character and may include the owner; Huichun targets its
    /// minimum number of wounded living characters. Public HP is enough to keep
    /// this deterministic without reading hidden identity or hand data.
    /// </summary>
    public IReadOnlyList<int> ChooseActiveSkillTargets(
        GameSnapshot view,
        LegalAction action)
    {
        if (action.Kind is not (LegalActionKind.UseSkill or LegalActionKind.UseEquipmentEffect) ||
            action.MinTargetCount == 0)
        {
            return [];
        }

        if (action.MinTargetCount < 0 || action.MaxTargetCount < action.MinTargetCount)
        {
            throw new InvalidOperationException("The active-skill target selection bounds are invalid.");
        }

        var selectableTargets = action.SelectableTargetSeats.Count == 0
            ? null
            : action.SelectableTargetSeats.ToHashSet();
        var candidates = view.Players
            .Where(player => player.IsAlive &&
                             (selectableTargets is null || selectableTargets.Contains(player.Seat)) &&
                             (action.Skill is SkillKind.Qingnang or SkillKind.Huichun
                                 ? player.Hp < player.MaxHp
                                 : player.Seat != Seat))
            .ToArray();
        if (action.MinTargetCount > candidates.Length)
        {
            throw new InvalidOperationException("The active skill requires more targets than are alive.");
        }

        var self = view.Players.Single(player => player.Seat == Seat);
        var selfRole = self.Role ?? Role.Renegade;
        var orderedCandidates = action.Kind == LegalActionKind.UseEquipmentEffect ||
                                action.Skill is SkillKind.Fanjian or SkillKind.Jijiang or SkillKind.Qiangxi
            ? candidates
                .OrderByDescending(player => GetHostility(view, selfRole, player))
                .ThenBy(player => player.Hp)
                .ThenBy(player => player.Seat)
            : candidates
                .OrderBy(player => player.Hp)
                .ThenByDescending(player => player.MaxHp - player.Hp)
                .ThenBy(player => player.Seat);

        return orderedCandidates
            .Take(action.MinTargetCount)
            .Select(player => player.Seat)
            .ToArray();
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
        var choice = ChooseDodgeResponse(view, attackerSeat, thoughtSequence);
        return (choice.UseDodge, choice.Thought);
    }

    /// <summary>
    /// Chooses between a visible physical Dodge, the visible Bagua armor
    /// judgment, and taking damage. The engine calls this only with a public
    /// player snapshot, so the choice never depends on the hidden draw-pile
    /// order or another player's hand.
    /// </summary>
    public (bool UseDodge, bool UseBagua, AiThoughtRecord Thought) ChooseDodgeResponse(
        GameSnapshot view,
        int attackerSeat,
        int thoughtSequence,
        bool incomingIgnoresArmor = false)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var dodgeProfile = CardCatalog.Get(CardKind.Dodge);
        var hasDodge = self.Hand.Any(card =>
            card.Kind == CardKind.Dodge ||
            self.Skill == SkillKind.Longdan && IsSlashCard(card.Kind));
        var hasBagua = !incomingIgnoresArmor &&
                       self.Equipment.Any(card => card.Kind == CardKind.BaguaFormation);
        var useDodgeScore = self.Hp <= 1
            ? dodgeProfile.AiResponseValue + 35d
            : dodgeProfile.AiResponseValue + (self.MaxHp - self.Hp) * 8d;
        var useBaguaScore = self.Hp <= 1
            ? CardCatalog.Get(CardKind.BaguaFormation).AiPlayValue + 35d
            : CardCatalog.Get(CardKind.BaguaFormation).AiPlayValue + (self.MaxHp - self.Hp) * 8d;
        var passScore = self.Hp <= 1 ? -100d : 5d;
        if (!hasDodge)
        {
            useDodgeScore = double.NegativeInfinity;
        }

        if (!hasBagua)
        {
            useBaguaScore = double.NegativeInfinity;
        }

        var useDodge = useDodgeScore >= useBaguaScore && useDodgeScore >= passScore;
        var useBagua = !useDodge && useBaguaScore >= passScore;
        var description = useDodge
            ? "打出闪"
            : useBagua
                ? "发动八卦阵"
                : "不出闪";
        var pseudoAction = new LegalAction(
            LegalActionKind.EndPlay,
            null,
            attackerSeat,
            description);

        var candidates = new List<AiCandidateScore>();
        if (hasDodge)
        {
            candidates.Add(new AiCandidateScore(
                pseudoAction with { Description = "打出闪" },
                useDodgeScore,
                "使用可见的闪或龙胆转换牌，避免这次伤害。"));
        }

        if (hasBagua)
        {
            candidates.Add(new AiCandidateScore(
                pseudoAction with { Description = "发动八卦阵" },
                useBaguaScore,
                "发动公开的八卦阵并进行红色判定；不读取隐藏牌堆顺序。"));
        }

        candidates.Add(new AiCandidateScore(
            pseudoAction with { Description = "不出闪" },
            passScore,
            "保留响应资源，接受这次伤害。"));

        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            pseudoAction.Description,
            candidates,
            $"生命值 {self.Hp}/{self.MaxHp}，决定{pseudoAction.Description}。" +
            (incomingIgnoresArmor ? "攻击者的武器无视防具，因此不考虑八卦阵。" : string.Empty));

        return (useDodge, useBagua, thought);
    }

    /// <summary>
    /// Answers Liu Bei's private Jijiang request from this seat's own cards and
    /// public identity relationship. The provider never inspects another hand.
    /// </summary>
    public (bool UseSlash, AiThoughtRecord Thought) ChooseJijiangResponse(
        GameSnapshot view,
        int ownerSeat,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var owner = view.Players.Single(player => player.Seat == ownerSeat);
        var hasSlash = view.PendingDecision is { Kind: DecisionKind.RespondSlash } prompt &&
                       prompt.Choices.Any(choice =>
                           choice.Cards.Count == 1 &&
                           choice.Parameters.GetValueOrDefault("response") == "jijiang-slash");
        var shouldHelp = self.Role == Role.Loyalist ||
                         self.Role == Role.Renegade && view.Players.Count(player => player.IsAlive) > 2;
        var slashScore = shouldHelp && hasSlash ? 85d : double.NegativeInfinity;
        const double declineScore = 10d;
        var useSlash = slashScore >= declineScore;
        var decision = useSlash ? "替主公打出杀" : "不响应激将";
        var pseudoAction = new LegalAction(LegalActionKind.EndPlay, null, ownerSeat, decision);
        var candidates = new List<AiCandidateScore>();
        if (hasSlash)
        {
            candidates.Add(new AiCandidateScore(
                pseudoAction with { Description = "替主公打出杀" },
                slashScore,
                shouldHelp ? "消耗自己的可见响应牌，帮助主公完成当前杀需求。" : "当前身份不应替主公消耗攻击牌。"));
        }
        candidates.Add(new AiCandidateScore(
            pseudoAction with { Description = "不响应激将" },
            declineScore,
            shouldHelp ? "保留自己的攻击资源。" : "拒绝帮助敌对主公。"));
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            decision,
            candidates,
            $"激将：身份为{self.Role}，主公 {owner.Seat + 1} 号位，决定{decision}。");
        return (useSlash, thought);
    }

    /// <summary>
    /// Answers Cao Cao's private Hujia request using only this seat's role, hand,
    /// public equipment and the Lord's public health. Rebels decline; loyalists
    /// protect the Lord, while the renegade does so before the final duel.
    /// </summary>
    public (bool UseDodge, bool UseBagua, AiThoughtRecord Thought) ChooseHujiaResponse(
        GameSnapshot view,
        int ownerSeat,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var owner = view.Players.Single(player => player.Seat == ownerSeat);
        var hasDodge = self.Hand.Any(card =>
            card.Kind == CardKind.Dodge ||
            self.Skill == SkillKind.Longdan && IsSlashCard(card.Kind));
        var hasBagua = self.Equipment.Any(card => card.Kind == CardKind.BaguaFormation);
        var shouldHelp = self.Role == Role.Loyalist ||
                         self.Role == Role.Renegade && view.Players.Count(player => player.IsAlive) > 2;
        var urgency = owner.Hp <= 1 ? 25d : Math.Max(0, owner.MaxHp - owner.Hp) * 5d;
        var dodgeScore = shouldHelp && hasDodge ? 90d + urgency : double.NegativeInfinity;
        var baguaScore = shouldHelp && hasBagua ? 75d + urgency : double.NegativeInfinity;
        const double declineScore = 10d;
        var useDodge = dodgeScore >= baguaScore && dodgeScore >= declineScore;
        var useBagua = !useDodge && baguaScore >= declineScore;
        var decision = useDodge ? "替主公打出闪" : useBagua ? "以八卦阵响应护驾" : "不响应护驾";
        var pseudoAction = new LegalAction(LegalActionKind.EndPlay, null, ownerSeat, decision);
        var candidates = new List<AiCandidateScore>();
        if (hasDodge)
        {
            candidates.Add(new AiCandidateScore(
                pseudoAction with { Description = "替主公打出闪" },
                dodgeScore,
                shouldHelp ? "消耗自己的可见响应牌，确定保护主公。" : "当前身份不应替主公消耗响应牌。"));
        }
        if (hasBagua)
        {
            candidates.Add(new AiCandidateScore(
                pseudoAction with { Description = "以八卦阵响应护驾" },
                baguaScore,
                shouldHelp ? "保留手牌并以公开防具尝试保护主公。" : "当前身份不应替主公承担判定。"));
        }
        candidates.Add(new AiCandidateScore(
            pseudoAction with { Description = "不响应护驾" },
            declineScore,
            shouldHelp ? "保留自己的响应资源。" : "拒绝帮助敌对主公。"));
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            decision,
            candidates,
            $"护驾：身份为{self.Role}，主公体力 {owner.Hp}/{owner.MaxHp}，决定{decision}。");
        return (useDodge, useBagua, thought);
    }

    /// <summary>
    /// Chooses whether to use the private Feedback trigger. The incoming card
    /// kind is public combat context; the AI does not inspect the engine zone or
    /// another player's hand to make this choice.
    /// </summary>
    public (bool UseFeedback, AiThoughtRecord Thought) ChooseFeedback(
        GameSnapshot view,
        int sourceSeat,
        CardKind? incomingCard,
        int thoughtSequence,
        bool takeSourceCard = false,
        string skillName = "反馈")
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var profile = incomingCard is { } cardKind ? CardCatalog.Get(cardKind) : null;
        var source = view.Players.Single(player => player.Seat == sourceSeat);
        var useScore = takeSourceCard
            ? 38d + source.HandCount * 2d + source.Equipment.Count * 8d - self.HandCount * 3d
            : (profile?.HandKeepValue ?? 0d) +
              Math.Max(0, self.MaxHp - self.HandCount) * 8d -
              self.HandCount * 4d;
        var skipScore = self.HandCount > self.MaxHp ? 52d : 8d;
        var gainDescription = takeSourceCard
            ? $"获得伤害来源的一张牌（其公开手牌数 {source.HandCount}、装备数 {source.Equipment.Count}）"
            : $"获得{profile?.DisplayName ?? "伤害牌"}";
        var gainReason = takeSourceCard
            ? $"{gainDescription}并扩大资源；只使用本座可见的生命、公开手牌数量和装备。"
            : $"获得{profile?.DisplayName ?? "伤害牌"}并扩大资源；只使用本座可见的生命和手牌数量。";
        var useAction = new LegalAction(
            LegalActionKind.Feedback,
            null,
            sourceSeat,
            $"发动【{skillName}】{gainDescription}");
        var skipAction = new LegalAction(
            LegalActionKind.SkipFeedback,
            null,
            sourceSeat,
            $"不发动【{skillName}】");
        var candidates = new[]
        {
            new AiCandidateScore(
                useAction,
                Math.Round(useScore + _random.NextDouble() * 0.001d, 3),
                gainReason),
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
            $"{skillName}触发：手牌 {self.HandCount}/{self.MaxHp}，决定{selected.Action.Description}（{selected.Score:0.###} 分）。");
        return (selected.Action.Kind == LegalActionKind.Feedback, thought);
    }

    /// <summary>
    /// Chooses whether to open the private Ganglie judgment trigger. The
    /// judgment result is deliberately not supplied here: it remains a hidden
    /// draw-pile fact until the engine commits the public judgment event.
    /// </summary>
    public (bool UseGanglie, AiThoughtRecord Thought) ChooseGanglieTrigger(
        GameSnapshot view,
        int sourceSeat,
        CardKind? incomingCard,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var source = view.Players.Single(player => player.Seat == sourceSeat);
        var useScore = 24d + Math.Max(0, self.MaxHp - self.Hp) * 6d +
                       (source.Role is Role.Rebel && (self.Role is Role.Lord or Role.Loyalist) ? 6d : 0d);
        var skipScore = self.Hp >= self.MaxHp ? 7d : 2d;
        var candidates = new[]
        {
            new AiCandidateScore(
                new LegalAction(
                    LegalActionKind.Ganglie,
                    null,
                    sourceSeat,
                    "发动【刚烈】进行判定"),
                Math.Round(useScore + _random.NextDouble() * 0.001d, 3),
                "受伤后公开判定，优先压迫伤害来源；不读取牌堆顺序或来源手牌。"),
            new AiCandidateScore(
                new LegalAction(
                    LegalActionKind.SkipGanglie,
                    null,
                    sourceSeat,
                    "不发动【刚烈】"),
                Math.Round(skipScore + _random.NextDouble() * 0.001d, 3),
                "保留当前结算，不进行额外判定。")
        };
        var selected = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Action.Kind)
            .First();
        var incomingName = incomingCard is { } cardKind
            ? CardCatalog.Get(cardKind).DisplayName
            : "技能";
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Action.Description,
            candidates,
            $"刚烈：受到{incomingName}伤害后决定{selected.Action.Description}（{selected.Score:0.###} 分）。");
        return (selected.Action.Kind == LegalActionKind.Ganglie, thought);
    }

    /// <summary>
    /// Chooses the red-judgment punishment from the exact private choices
    /// published to this seat. Card ids are read only from this seat's filtered
    /// snapshot; no opponent hand or engine zone is consulted.
    /// </summary>
    public (ChoiceId Choice, AiThoughtRecord Thought) ChooseGangliePunishment(
        GameSnapshot view,
        IReadOnlyList<PromptChoice> choices,
        int ownerSeat,
        int thoughtSequence)
    {
        if (choices.Count == 0)
        {
            throw new InvalidOperationException("AI was asked to resolve an empty Ganglie punishment prompt.");
        }

        var self = view.Players.Single(player => player.Seat == Seat);
        var handValues = self.Hand.ToDictionary(
            card => card.Id,
            card => CardCatalog.Get(card.Kind).HandKeepValue);
        var scored = choices
            .Select((choice, index) =>
            {
                var response = choice.Parameters.GetValueOrDefault("response");
                var actionKind = response == "ganglie-discard-two"
                    ? LegalActionKind.GanglieDiscardTwo
                    : LegalActionKind.GanglieLoseHp;
                var score = response == "ganglie-discard-two"
                    ? -choice.Cards.Sum(cardId => handValues.GetValueOrDefault(cardId, 0)) +
                      (self.Hp <= 1 ? 55d : self.HandCount > self.MaxHp ? 18d : 0d)
                    : self.Hp <= 1 ? -90d : 24d;
                var description = response == "ganglie-discard-two"
                    ? $"弃置 {choice.Cards.Count} 张手牌"
                    : "承受 1 点伤害";
                return (
                    Choice: choice,
                    Index: index,
                    Candidate: new AiCandidateScore(
                        new LegalAction(actionKind, null, ownerSeat, description),
                        Math.Round(score + _random.NextDouble() * 0.001d, 3),
                        response == "ganglie-discard-two"
                            ? "只按自己的私有手牌价值选择两张代价牌。"
                            : "只按自己的公开体力决定是否承受伤害。"));
            })
            .ToArray();
        var selected = scored
            .OrderByDescending(item => item.Candidate.Score)
            .ThenBy(item => item.Candidate.Action.Kind)
            .ThenBy(item => item.Index)
            .First();
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Candidate.Action.Description,
            scored.Select(item => item.Candidate).OrderByDescending(candidate => candidate.Score).ToArray(),
            $"刚烈反制：伤害来源选择{selected.Candidate.Action.Description}（{selected.Candidate.Score:0.###} 分）。");
        return (selected.Choice.Id, thought);
    }

    /// <summary>
    /// Chooses a private Guicai replacement from the exact hand-card ids in the
    /// prompt. The current judgment is public context; the AI never receives
    /// the hidden deck or another player's hand.
    /// </summary>
    public (int? CardId, AiThoughtRecord Thought) ChooseGuicaiReplacement(
        GameSnapshot view,
        int targetSeat,
        string reason,
        IReadOnlyList<int> validCardIds,
        CardKind currentJudgmentCard,
        Suit currentSuit,
        int thoughtSequence,
        int currentRank,
        int rulesVersion = 1,
        bool usesClassicGanglieJudgment = false)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var cards = validCardIds
            .Select(cardId => self.Hand.Concat(self.Equipment).Single(card => card.Id == cardId))
            .ToArray();
        if (cards.Length == 0)
        {
            throw new InvalidOperationException("AI was asked to resolve an empty Guicai card prompt.");
        }

        var isLightning = reason == JudgmentReasons.Lightning;
        var usesTacticalJudgmentScoring = rulesVersion >= 11;
        var target = view.Players.Single(player => player.Seat == targetSeat);
        var tacticalSupport = GetTacticalSupport(view, self.Role ?? Role.Renegade, target);
        var wantsSuccessfulJudgment = reason is JudgmentReasons.Lightning or JudgmentReasons.Leiji
            ? tacticalSupport < 0
            : tacticalSupport > 0;
        var currentMatches = IsSuccessfulJudgment(
            reason,
            currentSuit,
            currentRank,
            usesTacticalJudgmentScoring,
            usesClassicGanglieJudgment);
        var currentIsDesirable = usesTacticalJudgmentScoring
            ? currentMatches == wantsSuccessfulJudgment
            : currentMatches;
        var cardCandidates = cards
            .Select(card =>
            {
                var turnsSuccessful = IsSuccessfulJudgment(
                    reason,
                    card.Suit,
                    card.Rank,
                    usesTacticalJudgmentScoring,
                    usesClassicGanglieJudgment);
                var turnsDesirable = !usesTacticalJudgmentScoring
                    ? turnsSuccessful
                    : turnsSuccessful == wantsSuccessfulJudgment;
                var score = currentIsDesirable
                    ? -CardCatalog.Get(card.Kind).HandKeepValue
                    : turnsDesirable
                        ? 42d - CardCatalog.Get(card.Kind).HandKeepValue
                        : -18d - CardCatalog.Get(card.Kind).HandKeepValue;
                return new AiCandidateScore(
                    new LegalAction(
                        LegalActionKind.Guicai,
                        card.Id,
                        targetSeat,
                        $"弃置【{card.DisplayName}】替换判定牌"),
                    Math.Round(score + _random.NextDouble() * 0.001d, 3),
                    usesTacticalJudgmentScoring
                        ? turnsDesirable
                            ? "这张手牌会把公开判定改成符合当前阵营取向的结果；不读取隐藏牌堆。"
                            : "这张手牌不能把公开判定改成符合当前阵营取向的结果，保留手牌资源。"
                        : turnsSuccessful
                            ? isLightning
                                ? "用自己的黑桃 2 至 9 手牌把公开闪电判定转为命中；不读取隐藏牌堆。"
                                : "用自己的红色手牌把当前公开判定转为红色；不读取隐藏牌堆。"
                            : isLightning
                                ? "当前手牌不能把闪电转为命中，保留手牌资源。"
                                : "当前牌面不适合转为红色，保留手牌资源。");
            })
            .ToList();
        cardCandidates.Add(new AiCandidateScore(
            new LegalAction(
                LegalActionKind.SkipGuicai,
                null,
                targetSeat,
                "不发动【鬼才】"),
            currentIsDesirable ? 30d : 1d,
            usesTacticalJudgmentScoring
                ? currentIsDesirable
                    ? "当前公开判定已符合当前阵营取向，保留手牌资源。"
                    : "没有值得牺牲的手牌可把公开判定改成符合当前阵营取向的结果。"
                : currentMatches
                    ? isLightning
                        ? "当前闪电判定已经命中，只使用公开判定结果。"
                        : "当前判定已经是红色，只使用自己的公开判定结果。"
                    : isLightning
                        ? "没有值得牺牲的黑桃 2 至 9 手牌时保留资源。"
                        : "没有值得牺牲的红色手牌时保留资源."));

        var selected = cardCandidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Action.CardId ?? int.MaxValue)
            .First();
        var judgmentName = CardCatalog.Get(currentJudgmentCard).DisplayName;
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Action.Description,
            cardCandidates,
            $"鬼才：面对{judgmentName}（{currentSuit}，{reason}）决定{selected.Action.Description}（{selected.Score:0.###} 分）。");
        return (
            selected.Action.Kind == LegalActionKind.Guicai ? selected.Action.CardId : null,
            thought);
    }

    private static bool IsSuccessfulJudgment(
        string reason,
        Suit suit,
        int rank,
        bool usesSuitSpecificDelayedJudgments,
        bool usesClassicGanglieJudgment) => reason switch
        {
            JudgmentReasons.Lightning => suit == Suit.Spade && rank is >= 2 and <= 9,
            JudgmentReasons.Indulgence when usesSuitSpecificDelayedJudgments => suit == Suit.Heart,
            JudgmentReasons.SupplyShortage when usesSuitSpecificDelayedJudgments => suit == Suit.Club,
            JudgmentReasons.Luoshen => suit is Suit.Spade or Suit.Club,
            JudgmentReasons.Tieqi => suit is Suit.Heart or Suit.Diamond,
            JudgmentReasons.Leiji => suit is Suit.Spade or Suit.Club,
            JudgmentReasons.Ganglie when usesSuitSpecificDelayedJudgments && usesClassicGanglieJudgment =>
                suit != Suit.Heart,
            _ => suit is Suit.Heart or Suit.Diamond
        };

    public (int? TargetSeat, AiThoughtRecord Thought) ChooseLeijiTarget(
        GameSnapshot view,
        IReadOnlyList<int> targetSeats,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var selfRole = self.Role ?? Role.Renegade;
        var candidates = targetSeats
            .Select(targetSeat => view.Players.Single(player => player.Seat == targetSeat))
            .Where(target => target.IsAlive && target.Seat != Seat)
            .Select(target => new AiCandidateScore(
                new LegalAction(LegalActionKind.UseSkill, null, target.Seat,
                    $"对座位 {target.Seat + 1} 发动雷击"),
                Math.Round(-GetTacticalSupport(view, selfRole, target) *
                    (target.Hp <= 2 ? 44d : 30d) + _random.NextDouble() * 0.001d, 3),
                "按公开阵营关系与体力选择雷击目标；不读取隐藏牌。"))
            .ToList();
        candidates.Add(new AiCandidateScore(
            new LegalAction(LegalActionKind.UseSkill, null, null, "不发动雷击"),
            2d,
            "没有合适目标时保留可选技能。"));
        var selected = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Action.TargetSeat ?? int.MaxValue)
            .First();
        return (selected.Action.TargetSeat, new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Action.Description,
            candidates.OrderByDescending(candidate => candidate.Score).ToArray(),
            $"雷击：从 {targetSeats.Count} 个公开合法目标中选择 {selected.Action.Description}。"));
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
            let evaluated = _policyVersion >= 2
                ? GetTacticalSupport(view, self.Role ?? Role.Renegade, target) * (24 + Math.Max(0, target.MaxHp - target.HandCount) * 6) - profile.HandKeepValue * .4
                : score
            select new AiCandidateScore(
                new LegalAction(
                    LegalActionKind.YijiGift,
                    card.Id,
                    target.Seat,
                    $"将【{profile.DisplayName}】交给座位 {target.Seat + 1}"),
                Math.Round(evaluated + _random.NextDouble() * 0.001d, 3),
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
        var selfRole = self.Role ?? Role.Renegade;
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
                var value = _policyVersion >= 2 ? deficit * 20d * GetTacticalSupport(view, selfRole, target) : deficit * 20d + roleBonus;
                var score = Math.Round(value + _random.NextDouble() * 0.001d, 3);
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
        var selfRole = self.Role ?? Role.Renegade;
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
                if (_policyVersion >= 2)
                    score = (target.Hp <= 1 ? 80 : 40) * GetTacticalSupport(view, selfRole, target) - profile.HandKeepValue * .45;

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
    /// Chooses whether to satisfy Borrowed Sword with a real Slash or surrender
    /// the public weapon. The decision uses only published roles, health and the
    /// legal choices in this seat's private prompt.
    /// </summary>
    public (bool UseSlash, AiThoughtRecord Thought) ChooseBorrowedSwordResponse(
        GameSnapshot view,
        int trickSourceSeat,
        int slashTargetSeat,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var source = view.Players.Single(player => player.Seat == trickSourceSeat);
        var target = view.Players.Single(player => player.Seat == slashTargetSeat);
        var selfRole = self.Role ?? Role.Renegade;
        var canUseSlash = view.PendingDecision is { Kind: DecisionKind.RespondSlash } prompt &&
                          prompt.Choices.Any(choice =>
                              choice.Parameters.GetValueOrDefault("response") is
                                  "borrowed-sword-slash" or "jijiang-request");
        var targetSupport = GetTacticalSupport(view, selfRole, target);
        var sourceSupport = GetTacticalSupport(view, selfRole, source);
        var slashScore = canUseSlash
            ? -targetSupport * 70d + 22d + (target.Hp <= 1 ? 20d : 0d)
            : double.NegativeInfinity;
        var giveScore = sourceSupport * 42d + 8d;
        var useSlash = slashScore >= giveScore;
        var decision = useSlash ? $"对 {target.Name} 使用杀" : $"将武器交给 {source.Name}";
        var pseudoAction = new LegalAction(
            LegalActionKind.BorrowedSword,
            null,
            slashTargetSeat,
            decision);
        var candidates = new List<AiCandidateScore>();
        if (canUseSlash)
        {
            candidates.Add(new AiCandidateScore(
                pseudoAction with { Description = $"对 {target.Name} 使用杀" },
                slashScore,
                "按公开身份关系评估强制攻击目标，并保留当前装备武器。"));
        }
        candidates.Add(new AiCandidateScore(
            pseudoAction with { Description = $"将武器交给 {source.Name}" },
            giveScore,
            "放弃强制攻击，按公开身份关系评估把武器交给锦囊来源的价值。"));
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            decision,
            candidates,
            $"借刀杀人响应：目标关系 {targetSupport:0.##}，来源关系 {sourceSupport:0.##}，决定{decision}。");
        return (useSlash, thought);
    }

    /// <summary>
    /// Chooses one exact published two-card Stone Axe cost without reading any
    /// hidden engine zone. The cheapest available pair is compared with the
    /// public tactical value of forcing the Slash damage through.
    /// </summary>
    public (IReadOnlyList<int> CardIds, AiThoughtRecord Thought) ChooseStoneAxeResponse(
        GameSnapshot view,
        int targetSeat,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var target = view.Players.Single(player => player.Seat == targetSeat);
        var selfRole = self.Role ?? Role.Renegade;
        var prompt = view.PendingDecision is { Kind: DecisionKind.StoneAxe } decision
            ? decision
            : null;
        var ownedCards = self.Hand.Concat(self.Equipment).ToDictionary(card => card.Id);
        var useChoices = (prompt?.Choices ?? [])
            .Where(choice =>
                choice.Parameters.GetValueOrDefault("action") == "stone-axe-use" &&
                choice.Cards.Count == 2 &&
                choice.Cards.All(ownedCards.ContainsKey))
            .Select(choice => (
                Choice: choice,
                Cost: choice.Cards.Sum(id => CardCatalog.Get(ownedCards[id].Kind).HandKeepValue)))
            .OrderBy(candidate => candidate.Cost)
            .ThenBy(candidate => candidate.Choice.Cards[0])
            .ThenBy(candidate => candidate.Choice.Cards[1])
            .ToArray();
        var cheapest = useChoices.FirstOrDefault();
        var hasUseChoice = useChoices.Length > 0;
        var targetSupport = GetTacticalSupport(view, selfRole, target);
        var useScore = !hasUseChoice
            ? double.NegativeInfinity
            : -targetSupport * 80d +
              (target.Hp <= 1 ? 60d : target.Hp == 2 ? 20d : 0d) -
              cheapest.Cost * 0.6d;
        const double skipScore = 5d;
        var selectedIds = hasUseChoice && useScore >= skipScore
            ? cheapest.Choice.Cards.ToArray()
            : [];
        var decisionText = selectedIds.Length == 2
            ? $"弃置两张牌发动贯石斧，对 {target.Name} 造成伤害"
            : "不发动贯石斧";
        var pseudoAction = new LegalAction(
            LegalActionKind.Equip,
            null,
            targetSeat,
            decisionText);
        var candidates = new List<AiCandidateScore>();
        if (hasUseChoice)
        {
            candidates.Add(new AiCandidateScore(
                pseudoAction with { Description = $"弃置两张牌对 {target.Name} 强制造成伤害" },
                useScore,
                $"仅从当前私有提示的合法组合中选择保留价值最低的两张牌（代价 {cheapest.Cost}）。"));
        }
        candidates.Add(new AiCandidateScore(
            pseudoAction with { Description = "不发动贯石斧" },
            skipScore,
            "保留两张牌，接受此杀被闪抵消。"));
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            decisionText,
            candidates,
            $"贯石斧响应：目标关系 {targetSupport:0.##}，目标体力 {target.Hp}/{target.MaxHp}，决定{decisionText}。");
        return (selectedIds, thought);
    }

    public (ChoiceId ChoiceId, AiThoughtRecord Thought) ChooseZhuqueFanChoice(
        GameSnapshot view,
        int targetSeat,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var target = view.Players.Single(player => player.Seat == targetSeat);
        var prompt = view.PendingDecision is { Kind: DecisionKind.ZhuqueFan } decision
            ? decision
            : throw new InvalidOperationException("AI has no Zhuque Fan prompt.");
        var selfRole = self.Role ?? Role.Renegade;
        var chainScore = target.IsChained
            ? view.Players
                .Where(player => player.IsAlive && player.IsChained && player.Seat != targetSeat)
                .Sum(player => GetHostility(view, selfRole, player) +
                               (player.Hp <= 1 ? 22d : 0d))
            : 0d;
        var fireScore = 1d + chainScore;
        const double normalScore = 0d;
        var useFire = fireScore >= normalScore;
        var action = useFire ? "zhuque-fan-fire" : "zhuque-fan-normal";
        var selected = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == action);
        var candidates = prompt.Choices.Select(choice =>
        {
            var fire = choice.Parameters.GetValueOrDefault("action") == "zhuque-fan-fire";
            return new AiCandidateScore(
                new LegalAction(LegalActionKind.Equip, null, targetSeat, choice.Description),
                fire ? fireScore : normalScore,
                fire
                    ? "火杀保留直接伤害，并按公开连环状态评估可能的属性传导。"
                    : "保持普通杀，避免不利的公开连环传导。");
        }).ToArray();
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Description,
            candidates,
            $"朱雀羽扇：目标 {target.Name} 连环状态为 {(target.IsChained ? "是" : "否")}，连环净值 {chainScore:0.##}，决定{selected.Description}");
        return (selected.Id, thought);
    }

    public (ChoiceId ChoiceId, AiThoughtRecord Thought) ChooseCixiongDoubleSwordsChoice(
        GameSnapshot view,
        int sourceSeat,
        int targetSeat,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var source = view.Players.Single(player => player.Seat == sourceSeat);
        var target = view.Players.Single(player => player.Seat == targetSeat);
        var prompt = view.PendingDecision is { Kind: DecisionKind.CixiongDoubleSwords } decision
            ? decision
            : throw new InvalidOperationException("AI has no Cixiong Double Swords prompt.");
        var selfRole = self.Role ?? Role.Renegade;
        var isActivation = prompt.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("action") == "cixiong-use");
        PromptChoice selected;
        var candidates = new List<AiCandidateScore>();
        if (isActivation)
        {
            var hostility = GetHostility(view, selfRole, target);
            foreach (var choice in prompt.Choices)
            {
                var use = choice.Parameters.GetValueOrDefault("action") == "cixiong-use";
                var score = use ? hostility : 0d;
                candidates.Add(new AiCandidateScore(
                    new LegalAction(LegalActionKind.Equip, null, targetSeat, choice.Description),
                    score,
                    use
                        ? "只依据公开阵营关系决定是否让异性目标作出资源选择。"
                        : "保留装备触发，不向友方或低敌对目标施压。"));
            }
            selected = prompt.Choices
                .Select((choice, index) => new { Choice = choice, Index = index })
                .OrderByDescending(item => candidates[item.Index].Score)
                .ThenBy(item => item.Index)
                .First().Choice;
        }
        else
        {
            var sourceSupport = GetTacticalSupport(view, selfRole, source);
            var hand = self.Hand.ToDictionary(card => card.Id);
            foreach (var choice in prompt.Choices)
            {
                var discard = choice.Parameters.GetValueOrDefault("action") == "cixiong-discard";
                var cost = discard && choice.Cards.Count == 1 && hand.TryGetValue(choice.Cards[0], out var card)
                    ? CardCatalog.Get(card.Kind).HandKeepValue
                    : 0;
                var score = discard
                    ? -sourceSupport * 35d - cost * .35d
                    : sourceSupport * 45d;
                candidates.Add(new AiCandidateScore(
                    new LegalAction(
                        LegalActionKind.Equip,
                        choice.Cards.Count == 1 ? choice.Cards[0] : null,
                        sourceSeat,
                        choice.Description),
                    score,
                    discard
                        ? "只从自己的私有手牌中评估最低保留价值代价。"
                        : "依据公开阵营关系评估让杀的来源摸一张牌。"));
            }
            selected = prompt.Choices
                .Select((choice, index) => new { Choice = choice, Index = index })
                .OrderByDescending(item => candidates[item.Index].Score)
                .ThenBy(item => item.Index)
                .First().Choice;
        }

        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Description,
            candidates,
            $"雌雄双股剑：选择{selected.Description}");
        return (selected.Id, thought);
    }

    /// <summary>
    /// Chooses one exact Slash from the private Qinglong prompt. The decision
    /// uses only the filtered snapshot, the public relation to the fixed target,
    /// and this seat's own published card identities.
    /// </summary>
    public (ChoiceId ChoiceId, AiThoughtRecord Thought) ChooseQinglongCrescentBladeChoice(
        GameSnapshot view,
        int targetSeat,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var target = view.Players.Single(player => player.Seat == targetSeat);
        var selfRole = self.Role ?? Role.Renegade;
        var prompt = view.PendingDecision is { Kind: DecisionKind.QinglongCrescentBlade } decision
            ? decision
            : throw new InvalidOperationException("AI has no Qinglong Crescent Blade prompt.");
        var ownedCards = self.Hand.Concat(self.Equipment).ToDictionary(card => card.Id);
        var hostility = GetHostility(view, selfRole, target);
        var candidates = prompt.Choices.Select(choice =>
        {
            var action = choice.Parameters.GetValueOrDefault("action");
            var usesOwnSlash = action == "qinglong-slash";
            var requestsJijiang = action == "qinglong-jijiang";
            var continuesAttack = usesOwnSlash || requestsJijiang;
            var cardCost = usesOwnSlash && choice.Cards.Count == 1 && ownedCards.TryGetValue(choice.Cards[0], out var card)
                ? CardCatalog.Get(card.Kind).HandKeepValue
                : 0;
            var score = continuesAttack
                ? hostility * 1.2d + (target.Hp <= 1 ? 45d : 0d) - cardCost * .45d - (requestsJijiang ? 2d : 0d)
                : 0d;
            return new AiCandidateScore(
                new LegalAction(
                    LegalActionKind.Equip,
                    choice.Cards.Count == 1 ? choice.Cards[0] : null,
                    continuesAttack ? targetSeat : null,
                    choice.Description),
                score,
                continuesAttack
                    ? requestsJijiang
                        ? "结合公开阵营关系评估是否请求蜀势力角色提供追杀用的杀。"
                        : "从自己的私有合法候选中评估继续追杀同一目标的收益与牌值。"
                    : "保留杀牌并接受当前杀被闪抵消。");
        }).ToArray();
        var selectedIndex = candidates
            .Select((candidate, index) => new { Candidate = candidate, Index = index })
            .OrderByDescending(item => item.Candidate.Score)
            .ThenBy(item => item.Index)
            .First().Index;
        var selected = prompt.Choices[selectedIndex];
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Description,
            candidates,
            $"青龙偃月刀：目标敌对度 {hostility:0.##}，选择{selected.Description}");
        return (selected.Id, thought);
    }

    /// <summary>
    /// Chooses whether to replace Slash damage with Ice Sword discards, then
    /// selects only from the opaque hand slots and public equipment published
    /// by the private prompt.
    /// </summary>
    public (ChoiceId ChoiceId, AiThoughtRecord Thought) ChooseIceSwordChoice(
        GameSnapshot view,
        int targetSeat,
        int preventedDamageAmount,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var target = view.Players.Single(player => player.Seat == targetSeat);
        var selfRole = self.Role ?? Role.Renegade;
        var prompt = view.PendingDecision is { Kind: DecisionKind.IceSword } decision
            ? decision
            : throw new InvalidOperationException("AI has no Ice Sword prompt.");
        var support = GetTacticalSupport(view, selfRole, target);
        var availableCardCount = target.HandCount + target.Equipment.Count;
        var discardPotential = Math.Min(2, availableCardCount) * 32d;
        var lethalAdjustment = target.Hp <= preventedDamageAmount
            ? support >= 0d ? 90d : -90d
            : 0d;
        var equipment = target.Equipment.ToDictionary(card => card.Id);
        var candidates = prompt.Choices.Select(choice =>
        {
            var retainsDamage = choice.Parameters.GetValueOrDefault("action") == "ice-sword-damage";
            var publicCardValue = choice.Cards.Count == 1 &&
                                  equipment.TryGetValue(choice.Cards[0], out var publicCard)
                ? CardCatalog.Get(publicCard.Kind).HandKeepValue
                : 32;
            var score = retainsDamage
                ? -support * preventedDamageAmount * 40d
                : support * preventedDamageAmount * 45d -
                  support * discardPotential * 1.4d +
                  lethalAdjustment -
                  support * publicCardValue * .08d;
            return new AiCandidateScore(
                new LegalAction(
                    LegalActionKind.Equip,
                    choice.Cards.Count == 1 ? choice.Cards[0] : null,
                    retainsDamage ? null : targetSeat,
                    choice.Description),
                score,
                retainsDamage
                    ? "比较伤害收益、目标体力与公开关系后保留原伤害。"
                    : choice.Cards.Count == 0
                        ? "仅把暗手牌视为不透明牌位，结合目标牌量与伤害价值评估。"
                        : "公开装备可以按牌面保留价值参与寒冰剑弃牌选择。");
        }).ToArray();
        var selectedIndex = candidates
            .Select((candidate, index) => new { Candidate = candidate, Index = index })
            .OrderByDescending(item => item.Candidate.Score)
            .ThenBy(item => item.Index)
            .First().Index;
        var selected = prompt.Choices[selectedIndex];
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Description,
            candidates,
            $"寒冰剑：目标关系 {support:0.##}，待造成 {preventedDamageAmount} 点伤害，选择{selected.Description}");
        return (selected.Id, thought);
    }

    /// <summary>
    /// Chooses whether to discard one of the target's public mounts with Qilin
    /// Bow. No hidden target cards are inspected by this decision.
    /// </summary>
    public (ChoiceId ChoiceId, AiThoughtRecord Thought) ChooseQilinBowChoice(
        GameSnapshot view,
        int targetSeat,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var target = view.Players.Single(player => player.Seat == targetSeat);
        var selfRole = self.Role ?? Role.Renegade;
        var prompt = view.PendingDecision is { Kind: DecisionKind.QilinBow } decision
            ? decision
            : throw new InvalidOperationException("AI has no Qilin Bow prompt.");
        var hostility = GetHostility(view, selfRole, target);
        var publicEquipment = target.Equipment.ToDictionary(card => card.Id);
        var candidates = prompt.Choices.Select(choice =>
        {
            var use = choice.Parameters.GetValueOrDefault("action") == "qilin-bow-discard";
            var publicCardValue = use &&
                                  choice.Cards.Count == 1 &&
                                  publicEquipment.TryGetValue(choice.Cards[0], out var mount)
                ? CardCatalog.Get(mount.Kind).HandKeepValue
                : 0;
            var score = use
                ? hostility * 1.25d + publicCardValue * .2d
                : 0d;
            return new AiCandidateScore(
                new LegalAction(
                    LegalActionKind.Equip,
                    use ? choice.Cards.Single() : null,
                    use ? targetSeat : null,
                    choice.Description),
                score,
                use
                    ? "只根据公开阵营关系与目标装备区的坐骑牌面价值评估弃置收益。"
                    : "保留目标当前的公开坐骑并继续结算伤害。");
        }).ToArray();
        var selectedIndex = candidates
            .Select((candidate, index) => new { Candidate = candidate, Index = index })
            .OrderByDescending(item => item.Candidate.Score)
            .ThenBy(item => item.Index)
            .First().Index;
        var selected = prompt.Choices[selectedIndex];
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Description,
            candidates,
            $"麒麟弓：目标敌对度 {hostility:0.##}，选择{selected.Description}");
        return (selected.Id, thought);
    }

    /// <summary>
    /// Chooses a Mengjin discard from the source's filtered view. Hidden hand
    /// choices remain opaque slots; only public equipment receives card-value
    /// scoring.
    /// </summary>
    public (ChoiceId ChoiceId, AiThoughtRecord Thought) ChooseMengjinChoice(
        GameSnapshot view,
        int targetSeat,
        int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var target = view.Players.Single(player => player.Seat == targetSeat);
        var selfRole = self.Role ?? Role.Renegade;
        var prompt = view.PendingDecision is { Kind: DecisionKind.Mengjin } decision
            ? decision
            : throw new InvalidOperationException("AI has no Mengjin prompt.");
        var hostility = GetHostility(view, selfRole, target);
        var publicEquipment = target.Equipment.ToDictionary(card => card.Id);
        var candidates = prompt.Choices.Select(choice =>
        {
            var use = choice.Parameters.GetValueOrDefault("action") == "mengjin-discard";
            var publicCardValue = use && choice.Cards.Count == 1 &&
                                  publicEquipment.TryGetValue(choice.Cards[0], out var equipment)
                ? CardCatalog.Get(equipment.Kind).HandKeepValue
                : use ? 1.5d : 0d;
            var score = use ? hostility * 1.4d + publicCardValue * .2d : 0d;
            return new AiCandidateScore(
                new LegalAction(
                    LegalActionKind.Equip,
                    choice.Cards.Count == 1 ? choice.Cards[0] : null,
                    use ? targetSeat : null,
                    choice.Description),
                score,
                use
                    ? choice.Cards.Count == 1
                        ? "根据公开阵营关系与目标装备牌面价值评估猛进弃置。"
                        : "仅把目标暗手牌视为不透明牌位，不读取其牌面。"
                    : "保留目标现有牌，结束本次杀的结算。");
        }).ToArray();
        var selectedIndex = candidates
            .Select((candidate, index) => new { Candidate = candidate, Index = index })
            .OrderByDescending(item => item.Candidate.Score)
            .ThenBy(item => item.Index)
            .First().Index;
        var selected = prompt.Choices[selectedIndex];
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Description,
            candidates,
            $"猛进：目标敌对度 {hostility:0.##}，选择{selected.Description}");
        return (selected.Id, thought);
    }

    /// <summary>
    /// Chooses whether to spend one of this seat's private Nullification cards
    /// on the published trick-effect context. The method receives no engine
    /// zone, draw-pile, or other-player hand access; all strategic inputs come
    /// from the filtered snapshot and the exact legal card ids supplied by the
    /// current response prompt.
    /// </summary>
    public (int? CardId, AiThoughtRecord Thought) ChooseNullification(
        GameSnapshot view,
        CardKind effectCardKind,
        int sourceSeat,
        int? targetSeat,
        bool effectCurrentlyNullified,
        int chainDepth,
        IReadOnlyList<int> validCardIds,
        int thoughtSequence,
        IReadOnlyList<int>? targetSeats = null)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var selfRole = self.Role ?? Role.Renegade;
        var source = view.Players.Single(player => player.Seat == sourceSeat);
        var resolvedTargetSeats = targetSeats is { Count: > 0 }
            ? targetSeats
            : targetSeat is { } resolvedTarget
                ? [resolvedTarget]
                : [];
        var targets = resolvedTargetSeats
            .Select(seat => view.Players.Single(player => player.Seat == seat))
            .ToArray();
        var effectName = CardCatalog.Get(effectCardKind).DisplayName;
        var effectFavor = targets.Length == 0
            ? ScoreNullificationFavor(
                view,
                selfRole,
                source,
                target: null,
                effectCardKind: effectCardKind)
            : targets.Sum(target => ScoreNullificationFavor(view, selfRole, source, target, effectCardKind));
        var actionScore = effectCurrentlyNullified
            ? effectFavor > 0d ? 48d + effectFavor : -12d
            : effectFavor < 0d ? 54d - effectFavor : 2d;
        if (targets.Any(target => target.Seat == Seat) &&
            (_policyVersion == 1 || (effectCurrentlyNullified ? effectFavor > 0 : effectFavor < 0)))
        {
            actionScore += 38d;
        }

        var candidates = validCardIds
            .Select(cardId => self.Hand.SingleOrDefault(card => card.Id == cardId))
            .Where(card => card is not null)
            .Select(card =>
            {
                var score = Math.Round(
                    actionScore - CardCatalog.Get(card!.Kind).HandKeepValue * 0.18d +
                    _random.NextDouble() * 0.001d,
                    3);
                var description = effectCurrentlyNullified
                    ? $"使用【无懈可击】恢复【{effectName}】效果"
                    : $"使用【无懈可击】使【{effectName}】失效";
                return new AiCandidateScore(
                    new LegalAction(
                        LegalActionKind.Nullification,
                        card.Id,
                        sourceSeat,
                        description),
                    score,
                    effectCurrentlyNullified
                        ? "上一层无懈使效果暂时失效；按公开身份、目标和牌面价值判断是否恢复，不读取隐藏手牌。"
                        : "按公开身份、目标和锦囊牌面价值判断是否抵消，不读取其他玩家手牌。");
            })
            .ToList();

        var passScore = effectCurrentlyNullified
            ? effectFavor > 0d ? -35d : 18d
            : effectFavor < 0d ? -24d : 12d;
        candidates.Add(new AiCandidateScore(
            new LegalAction(
                LegalActionKind.SkipNullification,
                null,
                sourceSeat,
                "不使用【无懈可击】"),
            passScore,
            "保留自己的无懈资源；只使用当前公开的锦囊、身份、目标和生命信息。"));

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
            $"无懈响应：第 {chainDepth} 层，针对【{effectName}】决定{selected.Action.Description}（{selected.Score:0.###} 分）。");
        return (
            selected.Action.Kind == LegalActionKind.Nullification
                ? selected.Action.CardId
                : null,
            thought);
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
    /// Resolves the private, multi-step Guanxing prompt. The AI receives only
    /// the exact cards published to the skill owner and public judgment state;
    /// it never reads the engine draw pile outside that prompt.
    /// </summary>
    public (ChoiceId Choice, AiThoughtRecord Thought) ChooseGuanxing(
        GameSnapshot view,
        IReadOnlyList<PromptChoice> choices,
        int thoughtSequence)
    {
        if (choices.Count == 0)
        {
            throw new InvalidOperationException("AI was asked to resolve an empty Guanxing prompt.");
        }

        var stage = choices[0].Parameters.GetValueOrDefault("stage");
        var scored = choices.Select((choice, index) =>
        {
            var action = choice.Parameters.GetValueOrDefault("action");
            double score;
            string reason;
            if (action == "guanxing-use")
            {
                score = 80d;
                reason = "观星没有牌或体力代价，使用后只查看并排列自己的私有牌堆顶候选。";
            }
            else if (action == "guanxing-skip")
            {
                score = 0d;
                reason = "保留原牌堆顺序。";
            }
            else if (action == "guanxing-finish-top")
            {
                var selectedTop = int.Parse(
                    choice.Parameters["top-selected"],
                    System.Globalization.CultureInfo.InvariantCulture);
                var desiredTop = int.Parse(
                    choice.Parameters["desired-top-count"],
                    System.Globalization.CultureInfo.InvariantCulture);
                score = selectedTop >= desiredTop ? 90d : -90d;
                reason = selectedTop >= desiredTop
                    ? "已为公开判定与预计摸牌保留足够的牌，其余牌置底。"
                    : "牌堆顶尚未覆盖公开判定与预计摸牌位置。";
            }
            else
            {
                if (choice.Cards.Count != 1 ||
                    !Enum.TryParse<CardKind>(choice.Parameters.GetValueOrDefault("card-kind"), out var kind) ||
                    !Enum.TryParse<Suit>(choice.Parameters.GetValueOrDefault("suit"), out var suit) ||
                    !int.TryParse(
                        choice.Parameters.GetValueOrDefault("rank"),
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var rank))
                {
                    throw new InvalidOperationException("A Guanxing card choice is missing its private card metadata.");
                }

                var profile = CardCatalog.Get(kind);
                var cardValue = profile.HandKeepValue + profile.AiPlayValue * .35d;
                var purpose = choice.Parameters.GetValueOrDefault("slot-purpose");
                score = purpose switch
                {
                    $"judgment-{nameof(CardKind.Indulgence)}" => suit == Suit.Heart ? 180d : -80d,
                    $"judgment-{nameof(CardKind.SupplyShortage)}" => suit == Suit.Club ? 180d : -80d,
                    $"judgment-{nameof(CardKind.Lightning)}" => suit == Suit.Spade && rank is >= 2 and <= 9
                        ? -180d
                        : 140d,
                    "bottom" => -cardValue,
                    _ => cardValue
                };
                reason = purpose switch
                {
                    $"judgment-{nameof(CardKind.Indulgence)}" => "优先让公开的乐不思蜀获得红桃安全判定。",
                    $"judgment-{nameof(CardKind.SupplyShortage)}" => "优先让公开的兵粮寸断获得梅花安全判定。",
                    $"judgment-{nameof(CardKind.Lightning)}" => "避免把黑桃 2 至 9 放入自己的闪电判定位置。",
                    "bottom" => "牌堆底从最深处开始排列，先放较低价值牌。",
                    _ => $"预计摸牌价值 {cardValue:0.##}；只使用观星私有候选。"
                };
            }

            var cardId = choice.Cards.Count == 1 ? choice.Cards[0] : (int?)null;
            return (
                Choice: choice,
                Index: index,
                Candidate: new AiCandidateScore(
                    new LegalAction(
                        LegalActionKind.UseSkill,
                        cardId,
                        null,
                        choice.Description,
                        Skill: SkillKind.Guanxing),
                    Math.Round(score, 3),
                    reason));
        }).ToArray();
        var selected = scored
            .OrderByDescending(item => item.Candidate.Score)
            .ThenBy(item => item.Candidate.Action.CardId ?? int.MaxValue)
            .ThenBy(item => item.Index)
            .First();
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Choice.Description,
            scored.Select(item => item.Candidate).OrderByDescending(candidate => candidate.Score).ToArray(),
            $"观星{(string.IsNullOrEmpty(stage) ? "发动" : stage == "top" ? "牌堆顶排序" : "牌堆底排序")}：{selected.Choice.Description}");
        return (selected.Choice.Id, thought);
    }

    /// <summary>
    /// Chooses among the exact public-seat combinations published by Tuxi.
    /// Scores use only visible roles/teams, public hand counts and the AI's
    /// bounded suspicion model; no target card identity is inspected.
    /// </summary>
    public (ChoiceId Choice, AiThoughtRecord Thought) ChooseTuxi(
        GameSnapshot view,
        IReadOnlyList<PromptChoice> choices,
        int thoughtSequence)
    {
        if (choices.Count == 0)
        {
            throw new InvalidOperationException("AI was asked to resolve an empty Tuxi prompt.");
        }

        var self = view.Players.Single(player => player.Seat == Seat);
        var selfRole = self.Role ?? Role.Renegade;
        var scored = choices.Select(choice =>
        {
            if (choice.Parameters.GetValueOrDefault("action") == "tuxi-skip")
            {
                return (
                    Choice: choice,
                    Candidate: new AiCandidateScore(
                        new LegalAction(LegalActionKind.SkipTuxi, null, null, choice.Description),
                        30d,
                        "保留通常摸牌；不查看牌堆或任何目标暗牌。"));
            }

            var targets = choice.Targets
                .Select(seat => view.Players.Single(player => player.Seat == seat))
                .ToArray();
            var score = targets.Sum(target =>
                GetHostility(view, selfRole, target) * .45d +
                Math.Min(target.HandCount, 5) * 2d +
                14d);
            return (
                Choice: choice,
                Candidate: new AiCandidateScore(
                    new LegalAction(
                        LegalActionKind.Tuxi,
                        null,
                        targets.FirstOrDefault()?.Seat,
                        choice.Description,
                        TargetSeats: choice.Targets),
                    Math.Round(score, 3),
                    $"按 {targets.Length} 名公开目标的关系和手牌数评分；不读取牌面。"));
        }).ToArray();
        var selected = scored
            .OrderByDescending(item => item.Candidate.Score)
            .ThenBy(item => item.Choice.Id.Value, StringComparer.Ordinal)
            .First();
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Choice.Description,
            scored.Select(item => item.Candidate).OrderByDescending(candidate => candidate.Score).ToArray(),
            $"突袭：从 {choices.Count - 1} 个公开目标组合和普通摸牌中选择 {selected.Choice.Description}。");
        return (selected.Choice.Id, thought);
    }

    /// <summary>
    /// Decides whether to trade one draw for Luoyi using only the AI owner's
    /// private hand and public prompt. No deck order or opponent hand is read.
    /// </summary>
    public (ChoiceId Choice, AiThoughtRecord Thought) ChooseLuoyi(
        GameSnapshot view,
        IReadOnlyList<PromptChoice> choices,
        int thoughtSequence)
    {
        if (choices.Count != 2)
        {
            throw new InvalidOperationException("AI Luoyi requires exactly use and skip choices.");
        }

        var self = view.Players.Single(player => player.Seat == Seat);
        var attackCardCount = self.Hand.Count(card =>
            IsSlashCard(card.Kind) || card.Kind == CardKind.Duel);
        var scored = choices.Select(choice =>
        {
            var useSkill = choice.Parameters.GetValueOrDefault("action") == "luoyi-use";
            var score = useSkill
                ? attackCardCount == 0 ? 24d : 58d + Math.Min(attackCardCount, 3) * 7d
                : 45d;
            var reason = useSkill
                ? attackCardCount == 0
                    ? "当前没有可见的杀或决斗，少摸一张的即时收益较低。"
                    : $"当前私有手牌有 {attackCardCount} 张杀或决斗，可利用本回合伤害加成。"
                : "保留通常摸牌数量，不读取牌堆顺序。";
            return (
                Choice: choice,
                Candidate: new AiCandidateScore(
                    new LegalAction(
                        useSkill ? LegalActionKind.Luoyi : LegalActionKind.SkipLuoyi,
                        null,
                        null,
                        choice.Description,
                        Skill: SkillKind.Luoyi),
                    score,
                    reason));
        }).ToArray();
        var selected = scored
            .OrderByDescending(item => item.Candidate.Score)
            .ThenBy(item => item.Choice.Id.Value, StringComparer.Ordinal)
            .First();
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Choice.Description,
            scored.Select(item => item.Candidate).OrderByDescending(candidate => candidate.Score).ToArray(),
            $"裸衣：根据自己的 {attackCardCount} 张杀或决斗选择是否少摸一张牌。");
        return (selected.Choice.Id, thought);
    }

    /// <summary>
    /// Chooses one opaque ordinal slot from another player's hidden hand. Every
    /// candidate is intentionally scored identically because the filtered view
    /// contains no target-hand identity. The first slot is a stable tie-break;
    /// the choice remains fully replayable without consulting engine state.
    /// </summary>
    public (ChoiceId Choice, AiThoughtRecord Thought) ChooseTargetCardSlot(
        GameSnapshot view,
        int targetSeat,
        LegalActionKind actionKind,
        IReadOnlyList<PromptChoice> choices,
        int thoughtSequence)
    {
        if (choices.Count == 0)
        {
            throw new InvalidOperationException("AI was asked to choose from an empty hidden-hand slot list.");
        }

        var target = view.Players.Single(player => player.Seat == targetSeat);
        var effectName = actionKind == LegalActionKind.Dismantlement ? "过河拆桥" : "顺手牵羊";
        var candidates = choices
            .Select(choice => new AiCandidateScore(
                new LegalAction(actionKind, null, targetSeat, choice.Description),
                0d,
                "所有候选都是不可见的手牌位置；只按公开手牌数量选择，不读取目标牌面。"))
            .ToArray();
        var selected = choices[0];
        var thought = new AiThoughtRecord(
            thoughtSequence,
            view.TurnNumber,
            Seat,
            selected.Description,
            candidates,
            $"{effectName}：目标 {target.Name} 有 {target.HandCount} 张隐藏手牌，候选牌位信息相同，选择第一个不透明牌位。");
        return (selected.Id, thought);
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
        var selfRole = self.Role ?? Role.Renegade;
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
    /// Chooses Peach or Alcohol for any legal responder. Both the view and the
    /// private card lists are scoped to this AI seat; no other hand is inspected.
    /// </summary>
    public (bool UsePeach, int? PeachCardId, bool UseAlcohol, int? AlcoholCardId, AiThoughtRecord Thought)
        ChooseDyingResponseWithAlcohol(
        GameSnapshot view,
        int victimSeat,
        IReadOnlyList<Card> peaches,
        IReadOnlyList<Card> alcohols,
        int thoughtSequence,
        bool allowCrossSeatAlcoholRescue = false)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var victim = view.Players.Single(player => player.Seat == victimSeat);
        var selfRole = self.Role ?? Role.Renegade;
        var useScore = _policyVersion >= 2
            ? ScoreTacticalDyingResponse(view, selfRole, victim)
            : ScoreDyingResponse(selfRole, self.Seat, victim);
        var candidates = peaches
            .Select(peach => new AiCandidateScore(
                new LegalAction(
                    LegalActionKind.Peach,
                    peach.Id,
                    victimSeat,
                    peach.Kind == CardKind.Peach
                        ? $"使用桃救援 {victim.Name}"
                        : $"将{peach.DisplayName}当桃救援 {victim.Name}"),
                Math.Round(useScore + _random.NextDouble() * 0.001d, 3),
                peach.Kind == CardKind.Peach
                    ? "消耗一张自己的桃，使濒死角色回到 1 点体力。"
                    : $"将自己的一张{peach.DisplayName}当桃使用，使濒死角色回到 1 点体力。"))
            .ToList();
        var legalAlcohols = self.Seat == victimSeat || allowCrossSeatAlcoholRescue
            ? alcohols
            : [];
        candidates.AddRange(legalAlcohols.Select(alcohol => new AiCandidateScore(
            new LegalAction(
                LegalActionKind.Alcohol,
                alcohol.Id,
                victimSeat,
                self.Seat == victimSeat
                    ? $"使用酒自救 {victim.Name}"
                    : $"使用酒救援 {victim.Name}"),
            Math.Round(useScore + (_policyVersion >= 2 ? 5d : -1d) + _random.NextDouble() * 0.001d, 3),
            self.Seat == victimSeat
                ? "消耗一张自己的酒，使自己回到 1 点体力。"
                : "消耗一张自己的酒，使公开濒死角色回到 1 点体力。")));
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
            SkillKind.Ganglie => role is Role.Lord or Role.Loyalist ? 39d : 35d,
            SkillKind.Guicai => role is Role.Lord or Role.Loyalist ? 41d : 36d,
            SkillKind.Kujin => role is Role.Lord or Role.Loyalist ? 38d : 35d,
            SkillKind.Qiangxi => role is Role.Rebel or Role.Renegade ? 40d : 35d,
            SkillKind.Duanliang => role is Role.Rebel or Role.Renegade ? 41d : 36d,
            SkillKind.Zhiheng => role is Role.Lord or Role.Loyalist ? 36d : 34d,
            SkillKind.Rende => role is Role.Lord or Role.Loyalist ? 40d : 36d,
            SkillKind.Qingnang => role is Role.Lord or Role.Loyalist ? 39d : 35d,
            SkillKind.Huichun => role is Role.Lord or Role.Loyalist ? 43d : 38d,
            SkillKind.Mashu => role is Role.Rebel or Role.Renegade ? 38d : 34d,
            SkillKind.Qicai => role is Role.Rebel or Role.Renegade ? 37d : 33d,
            SkillKind.Jijiu => role is Role.Lord or Role.Loyalist ? 40d : 36d,
            SkillKind.Qixi => role is Role.Rebel or Role.Renegade ? 39d : 35d,
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
            SkillKind.Ganglie => "受伤后以公开判定逼迫伤害来源付出手牌或体力代价。",
            SkillKind.Guicai => "在公开判定生效前用自己的手牌改变结果，适合保护己方结算。",
            SkillKind.Kujin => "出牌阶段以 1 点体力换取两张牌；降至 0 点时先进入濒死救援。",
            SkillKind.Qiangxi => "出牌阶段以体力或武器牌为代价，对攻击范围内的角色造成直接伤害。",
            SkillKind.Duanliang => "可将黑色基本牌或装备牌当兵粮寸断，并把目标距离扩展到 2。",
            SkillKind.Zhiheng => "出牌阶段用低保留价值手牌换取等量新牌，稳定调整手牌质量。",
            SkillKind.Rende => "出牌阶段将手牌交给其他角色；一次交给至少两张时可回复 1 点体力。",
            SkillKind.Qingnang => "出牌阶段弃置一张手牌令受伤角色回复 1 点体力，每回合一次。",
            SkillKind.Huichun => "出牌阶段弃置两张手牌，令至少两名受伤角色各回复 1 点体力，每回合一次。",
            SkillKind.Mashu => "计算与其他角色的距离 -1，扩大杀和顺手牵羊的公开合法范围。",
            SkillKind.Qicai => "锦囊牌无距离限制，扩大公开合法目标范围。",
            SkillKind.Jijiu => "濒死窗口可将红色牌当作桃使用，扩大自己的救援牌来源。",
            SkillKind.Jijiang => "需要使用或打出杀时可请求其他蜀势力角色提供，适合共享阵营攻击资源。",
            SkillKind.Jiuyuan => "其他吴势力角色用桃救援濒死主公时额外回复一点体力，提高阵营救援效率。",
            SkillKind.Qixi => "可将黑色手牌或已装备牌当作过河拆桥，扩展对公开装备、判定区和暗手牌的控制。",
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
        if (action.Kind == LegalActionKind.Recast)
            return _recastTurn == view.TurnNumber && action.CardId is { } recastId && _recastCardsThisTurn.Contains(recastId)
                ? (-1000d, "本回合已重铸过这张实体牌，避免反复换回同一张牌而停滞。")
                : (32d, "重铸铁索换取一张未知牌；不读取牌堆顺序，优先保留更有利的连环或解链行动。");
        if (action.Kind == LegalActionKind.UseSkill)
        {
            if (action.Skill == SkillKind.Qiangxi)
            {
                var qiangxiTarget = view.Players
                    .Where(player => player.IsAlive && action.SelectableTargetSeats.Contains(player.Seat))
                    .OrderByDescending(player => GetHostility(view, selfRole, player))
                    .ThenBy(player => player.Hp)
                    .ThenBy(player => player.Seat)
                    .FirstOrDefault();
                if (qiangxiTarget is null)
                {
                    return (-100d, "攻击范围内没有强袭目标。");
                }

                var qiangxiHostility = GetHostility(view, selfRole, qiangxiTarget);
                if (qiangxiHostility <= 0)
                {
                    return (-90d, "攻击范围内没有值得支付强袭代价的敌对目标。");
                }

                var hasWeaponCost = action.SelectableCardIds.Count > 0;
                var costPenalty = hasWeaponCost
                    ? 8d
                    : self.Hp <= 1
                        ? 100d
                        : self.Hp == 2 ? 28d : 15d;
                var finishBonus = qiangxiTarget.Hp <= 1 ? 45d : 0d;
                return (
                    30d + qiangxiHostility * .45d + finishBonus - costPenalty,
                    hasWeaponCost
                        ? $"弃置自己可见的一张武器牌，对座位 {qiangxiTarget.Seat + 1} 造成 1 点伤害。"
                        : $"失去 1 点体力，对座位 {qiangxiTarget.Seat + 1} 造成 1 点伤害；只使用公开体力和合法目标。");
            }

            if (action.Skill == SkillKind.Fanjian)
            {
                var fanjianTarget = view.Players
                    .Where(player => player.IsAlive && player.Seat != Seat)
                    .OrderByDescending(player => GetHostility(view, selfRole, player))
                    .ThenBy(player => player.Hp)
                    .ThenBy(player => player.Seat)
                    .FirstOrDefault();
                if (fanjianTarget is null)
                {
                    return (-100d, "没有其他存活角色，不能发动反间。");
                }

                var fanjianHostility = GetHostility(view, selfRole, fanjianTarget);
                return fanjianHostility > 0
                    ? (18d + fanjianHostility * .4d + (fanjianTarget.Hp <= 1 ? 12d : 0d),
                        $"对公开判断中最敌对的座位 {fanjianTarget.Seat + 1} 发动反间；不读取其选择或随机手牌结果。")
                    : (-80d, "没有值得主动交牌并施压的敌对目标，保留手牌。");
            }

            if (action.Skill == SkillKind.Rende)
            {
                var recoveryBonus = self.HandCount >= 2 && self.Hp < self.MaxHp ? 12d : 0d;
                return (
                    14d + Math.Min(self.HandCount, 5) * 1.2d + recoveryBonus,
                    recoveryBonus > 0
                        ? $"发动{action.Description}，向公开低体力目标交给至少两张牌并回复 1 点；只使用自己的手牌和公开体力。"
                        : $"发动{action.Description}，向其他存活角色交给一张低保留价值手牌；目标由公开存活信息确定。 ");
            }

            if (action.Skill == SkillKind.Jijiang)
            {
                var jijiangTarget = view.Players
                    .Where(player => player.IsAlive &&
                                     action.SelectableTargetSeats.Contains(player.Seat))
                    .OrderByDescending(player => GetHostility(view, selfRole, player))
                    .ThenBy(player => player.Hp)
                    .ThenBy(player => player.Seat)
                    .FirstOrDefault();
                if (jijiangTarget is null)
                {
                    return (-100d, "没有处于刘备攻击范围内的合法目标，不能发动激将。");
                }

                var targetHostility = GetHostility(view, selfRole, jijiangTarget);
                return targetHostility > 0
                    ? (34d + targetHostility * .5d + (jijiangTarget.Hp <= 1 ? 12d : 0d),
                        $"对公开判断中最敌对的座位 {jijiangTarget.Seat + 1} 发动激将；目标与距离按刘备公开状态判断，不读取蜀将手牌。")
                    : (-80d, "攻击范围内没有值得发动激将的敌对目标。");
            }

            if (action.Skill == SkillKind.Qingnang)
            {
                var qingnangTarget = view.Players
                    .Where(player => player.IsAlive && player.Hp < player.MaxHp)
                    .OrderBy(player => player.Hp)
                    .ThenByDescending(player => player.MaxHp - player.Hp)
                    .ThenBy(player => player.Seat)
                    .FirstOrDefault();
                if (qingnangTarget is null)
                    return (-100d, "没有受伤的存活角色，不能发动青囊。 ");

                return (
                    18d + (qingnangTarget.Hp <= 1 ? 8d : 0d),
                    $"发动{action.Description}，弃置一张低保留价值手牌令公开受伤目标 {qingnangTarget.Seat + 1} 回复 1 点；不读取暗牌。 ");
            }

            if (action.Skill == SkillKind.Huichun)
            {
                var huichunTargets = view.Players
                    .Where(player => player.IsAlive && player.Hp < player.MaxHp)
                    .OrderBy(player => player.Hp)
                    .ThenByDescending(player => player.MaxHp - player.Hp)
                    .ThenBy(player => player.Seat)
                    .Take(action.MinTargetCount)
                    .ToArray();
                if (huichunTargets.Length < action.MinTargetCount)
                    return (-100d, "受伤存活角色不足，不能发动回春。 ");

                var criticalTargets = huichunTargets.Count(player => player.Hp <= 1);
                return (
                    24d + criticalTargets * 8d + Math.Min(self.HandCount, 4),
                    $"发动{action.Description}，弃置两张低保留价值手牌令 {huichunTargets.Length} 名公开受伤目标各回复 1 点；不读取暗牌。 ");
            }

            if (action.Skill == SkillKind.Zhiheng)
            {
                var selectableCards = GetActiveSkillSelectableCards(self, action);
                var discardCandidate = selectableCards
                    .OrderBy(card => CardCatalog.Get(card.Kind).HandKeepValue)
                    .ThenBy(card => card.Id)
                    .FirstOrDefault();
                var candidateName = discardCandidate is null
                    ? "没有可弃置牌"
                    : $"优先弃置【{discardCandidate.DisplayName}】";
                return (
                    10d + Math.Min(selectableCards.Count, 6) * 0.5d,
                    $"发动{action.Description}，弃置一张低保留价值牌并摸一张；{candidateName}，不读取其他角色暗牌。 ");
            }

            var handPressure = Math.Min(self.HandCount, 6) * 0.8d;
            var missingHp = Math.Max(0, self.MaxHp - self.Hp);
            return (
                24d + missingHp * 5d - handPressure,
                $"发动{action.Description}，以 1 点公开体力换取两张牌；当前体力 {self.Hp}/{self.MaxHp}，不读取暗牌。 ");
        }

        if (action.Kind == LegalActionKind.UseEquipmentEffect &&
            action.EquipmentKind == CardKind.ZhangbaSerpentSpear)
        {
            var equipmentTarget = view.Players
                .Where(player => player.IsAlive && action.SelectableTargetSeats.Contains(player.Seat))
                .OrderByDescending(player => GetHostility(view, selfRole, player))
                .ThenBy(player => player.Hp)
                .ThenBy(player => player.Seat)
                .FirstOrDefault();
            if (equipmentTarget is null)
            {
                return (-100d, "没有丈八蛇矛可攻击的合法目标。");
            }

            var equipmentHostility = GetHostility(view, selfRole, equipmentTarget);
            var cost = GetActiveSkillSelectableCards(self, action)
                .OrderBy(card => CardCatalog.Get(card.Kind).HandKeepValue)
                .ThenBy(card => card.Id)
                .Take(2)
                .Sum(card => CardCatalog.Get(card.Kind).HandKeepValue) * .18d;
            return equipmentHostility > 0
                ? (18d + equipmentHostility * .45d + (equipmentTarget.Hp <= 1 ? 35d : 0d) - cost,
                    $"发动【丈八蛇矛】对座位 {equipmentTarget.Seat + 1} 使用虚拟【杀】，支付两张最低保留价值手牌。")
                : (-100d, "合法目标中没有值得支付两张手牌攻击的敌对角色。");
        }

        if (action.Kind == LegalActionKind.UseEquipmentEffect &&
            action.EquipmentKind == CardKind.WoodenOx)
        {
            var stored = GetActiveSkillSelectableCards(self, action)
                .OrderBy(card => CardCatalog.Get(card.Kind).HandKeepValue)
                .ThenBy(card => card.Id)
                .FirstOrDefault();
            return stored is null
                ? (-100d, "没有可置于木牛流马下的手牌。")
                : (12d, $"将低保留价值的【{stored.DisplayName}】置于木牛流马下，保留为可使用的私有“粮”。");
        }

        if (action.Kind == LegalActionKind.EndPlay)
        {
            return (0d, "结束出牌是所有局面的保底动作。");
        }

        var card = self.Hand
            .Concat(self.WoodenOxGrain ?? [])
            .Concat(self.Equipment)
            .Single(candidate => candidate.Id == action.CardId);
        var playedCardKind = action.PlayedCardKind ?? card.Kind;
        var cardProfile = CardCatalog.Get(playedCardKind);

        if (action.Kind == LegalActionKind.Slash && action.TargetSeats.Count > 1)
        {
            return ScoreFangtianHalberdSlash(view, self, selfRole, action);
        }

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

        if (action.Kind == LegalActionKind.IronChain)
        {
            var targets = action.TargetSeats
                .Select(targetSeat => view.Players.Single(player => player.Seat == targetSeat))
                .ToArray();
            var chainValue = targets.Sum(target =>
            {
                var hostility = target.Seat == self.Seat
                    ? -95d
                    : GetHostility(view, selfRole, target);
                return target.IsChained ? -hostility : hostility;
            });
            var score = cardProfile.AiPlayValue + chainValue + targets.Length * 3d;
            return (
                score,
                $"切换 {targets.Length} 名公开连环角色的状态；只使用公开身份、体力和连环标记，不读取隐藏手牌。");
        }

        if (action.Kind == LegalActionKind.BorrowedSword)
        {
            if (action.TargetSeats.Count != 2)
            {
                return (-100d, "借刀杀人缺少有序的持械者与被杀目标。");
            }

            var weaponOwner = view.Players.Single(player => player.Seat == action.TargetSeats[0]);
            var slashTarget = view.Players.Single(player => player.Seat == action.TargetSeats[1]);
            var ownerHostility = GetHostility(view, selfRole, weaponOwner);
            var targetHostility = GetHostility(view, selfRole, slashTarget);
            var weaponValue = weaponOwner.Equipment
                .Where(equipment => EquipmentCatalog.Get(equipment.Kind).Slot == EquipmentSlot.Weapon)
                .Select(equipment => CardCatalog.Get(equipment.Kind).AiPlayValue)
                .DefaultIfEmpty(12)
                .Max();
            var borrowedFinishingBonus = slashTarget.Hp <= 1 ? 22d : 0d;
            return (
                cardProfile.AiPlayValue + ownerHostility * .45d + targetHostility * .65d +
                weaponValue * .35d + borrowedFinishingBonus,
                $"令持械的座位 {weaponOwner.Seat + 1} 对座位 {slashTarget.Seat + 1} 使用杀，否则取得其公开武器；只使用公开身份、体力、距离与装备。");
        }

        var target = view.Players.Single(player => player.Seat == action.TargetSeat);
        if (action.Kind == LegalActionKind.Lightning)
        {
            var riskAdjustment = self.Hp >= 4 ? 18d : -62d;
            return (
                cardProfile.AiPlayValue + riskAdjustment,
                $"对自己置入【闪电】；只按自己的公开体力 {self.Hp}/{self.MaxHp} 评估 3 点雷电风险，不读取隐藏牌堆顺序。 ");
        }

        var hostility = GetHostility(view, selfRole, target);
        if (action.Kind == LegalActionKind.Indulgence)
        {
            var existingJudgmentCount = target.Judgment.Count;
            var lowHealthBonus = target.Hp <= 1 ? 10d : 0d;
            return (
                cardProfile.AiPlayValue + hostility + lowHealthBonus,
                $"将【乐不思蜀】置入目标判定区；目标已有 {existingJudgmentCount} 张公开判定牌，只使用公开身份、体力和判定区，不读取目标暗牌。");
        }

        if (action.Kind == LegalActionKind.SupplyShortage)
        {
            var existingJudgmentCount = target.Judgment.Count;
            var handPressure = Math.Min(target.HandCount, 5) * 6d;
            var isDuanliang = action.PlayedCardKind == CardKind.SupplyShortage &&
                              card.Kind != CardKind.SupplyShortage;
            var equipmentCost = isDuanliang && self.Equipment.Any(equipment => equipment.Id == card.Id)
                ? Math.Max(8d, CardCatalog.Get(card.Kind).AiPlayValue * 0.75d)
                : 0d;
            var conversionCost = isDuanliang
                ? Math.Max(4d, CardCatalog.Get(card.Kind).HandKeepValue * 0.18d) + equipmentCost
                : 0d;
            var conversionText = isDuanliang
                ? equipmentCost > 0d
                    ? $"；以已装备的【{card.DisplayName}】发动断粮并计入公开装备机会成本"
                    : $"；将手牌【{card.DisplayName}】当作【兵粮寸断】"
                : string.Empty;
            return (
                cardProfile.AiPlayValue + hostility + handPressure - conversionCost,
                $"将【兵粮寸断】置入目标判定区；目标已有 {existingJudgmentCount} 张公开判定牌和 {target.HandCount} 张公开手牌数量，预计压制其下回合摸牌，只使用公开信息{conversionText}。");
        }

        if (action.Kind == LegalActionKind.Dismantlement)
        {
            var handPressure = Math.Min(target.HandCount, 5) * 4d;
            var publicEquipment = action.TargetCardId is { } targetCardId
                ? target.Equipment.SingleOrDefault(card => card.Id == targetCardId)
                : null;
            var publicJudgment = action.TargetCardId is { } judgmentCardId
                ? target.Judgment.SingleOrDefault(card => card.Id == judgmentCardId)
                : null;
            var publicTarget = publicEquipment ?? publicJudgment;
            var publicTargetZone = publicEquipment is not null
                ? "装备"
                : publicJudgment is not null
                    ? "判定区"
                    : null;
            var publicTargetPressure = publicTarget is null
                ? 0d
                : CardCatalog.Get(publicTarget.Kind).AiPlayValue * 0.75d + 12d;
            var equipmentCost = self.Equipment.Any(equipment => equipment.Id == card.Id)
                ? Math.Max(6d, CardCatalog.Get(card.Kind).AiPlayValue * 0.75d)
                : 0d;
            var qixiText = action.PlayedCardKind == CardKind.Dismantlement && card.Kind != CardKind.Dismantlement
                ? equipmentCost > 0d
                    ? $"；以已装备的【{card.DisplayName}】发动奇袭，计入公开装备机会成本"
                    : $"；将手牌【{card.DisplayName}】当作【过河拆桥】"
                : string.Empty;
            return (
                cardProfile.AiPlayValue + hostility + handPressure + publicTargetPressure - equipmentCost,
                publicTarget is null
                    ? $"选择目标的一张不透明牌位；只使用公开手牌数量 {target.HandCount} 和身份敌对值，不读取目标暗牌{qixiText}。"
                    : $"弃置目标公开{publicTargetZone}【{publicTarget.DisplayName}】；只读取公开{publicTargetZone}和身份敌对值，不读取目标暗牌{qixiText}。");
        }

        if (action.Kind == LegalActionKind.Snatch)
        {
            var handPressure = Math.Min(target.HandCount, 5) * 5d;
            var publicEquipment = action.TargetCardId is { } targetCardId
                ? target.Equipment.SingleOrDefault(card => card.Id == targetCardId)
                : null;
            var publicJudgment = action.TargetCardId is { } judgmentCardId
                ? target.Judgment.SingleOrDefault(card => card.Id == judgmentCardId)
                : null;
            var publicTarget = publicEquipment ?? publicJudgment;
            var publicTargetZone = publicEquipment is not null
                ? "装备"
                : publicJudgment is not null
                    ? "判定区"
                    : null;
            var publicTargetValue = publicTarget is null
                ? 0d
                : CardCatalog.Get(publicTarget.Kind).AiPlayValue * 0.9d + 15d;
            return (
                cardProfile.AiPlayValue + hostility + handPressure + publicTargetValue,
                publicTarget is null
                    ? $"从距离 1 的目标选择一张不透明牌位；只使用公开手牌数量 {target.HandCount} 和身份敌对值，不读取目标暗牌。"
                    : $"从距离 1 的目标获得公开{publicTargetZone}【{publicTarget.DisplayName}】；只读取公开{publicTargetZone}和身份敌对值，不读取目标暗牌。");
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
        var gudingBladeBonus = HasGudingBladeDamageBonus(self, action, target) ? 42d : 0d;
        var gudingBlade = gudingBladeBonus > 0d
            ? $"；古锭刀对公开为空手的目标可令伤害 +1，增加 {gudingBladeBonus:0.#} 分"
            : string.Empty;
        var zhuqueFanBonus = GetZhuqueFanConversionBonus(
            view,
            self,
            selfRole,
            action,
            card,
            target);
        var zhuqueFan = zhuqueFanBonus != 0d
            ? $"；朱雀羽扇把普通杀改为火杀，按公开连环关系调整 {zhuqueFanBonus:0.#} 分"
            : string.Empty;
        var silverLionPenalty = target.Equipment.Any(equipment => equipment.Kind == CardKind.SilverLion) &&
                                self.Equipment.All(equipment => equipment.Kind != CardKind.QinggangSword) &&
                                (self.HasAlcoholEffect || gudingBladeBonus > 0d)
            ? 36d
            : 0d;
        var silverLion = silverLionPenalty > 0d
            ? $"；白银狮子会把公开可见的多点杀伤害改为 1，扣除 {silverLionPenalty:0.#} 分"
            : string.Empty;
        return (
            cardProfile.AiPlayValue + hostility + finishingBonus + pressureBonus + gudingBladeBonus + zhuqueFanBonus - silverLionPenalty,
            $"卡牌策略值 {cardProfile.AiPlayValue:0.#}，目标敌对值 {hostility:0.#}，低体力收益 {finishingBonus:0.#}{conversion}{gudingBlade}{zhuqueFan}{silverLion}。身份判断只使用公开信息。");
    }

    private static bool HasGudingBladeDamageBonus(
        PlayerSnapshot self,
        LegalAction action,
        PlayerSnapshot target) =>
        action.Kind == LegalActionKind.Slash &&
        action.TargetSeats.Count == 1 &&
        target.HandCount == 0 &&
        self.Equipment.Any(card => card.Kind == CardKind.GudingBlade);

    private double GetZhuqueFanConversionBonus(
        GameSnapshot view,
        PlayerSnapshot self,
        Role selfRole,
        LegalAction action,
        CardSnapshot physicalCard,
        PlayerSnapshot target)
    {
        if (action.Kind != LegalActionKind.Slash ||
            action.PlayedCardKind != CardKind.FireSlash ||
            physicalCard.Kind != CardKind.Slash ||
            self.Equipment.All(card => card.Kind != CardKind.ZhuqueFan))
        {
            return 0d;
        }

        if (!target.IsChained)
        {
            return 1d;
        }

        return 1d + view.Players
            .Where(player => player.IsAlive && player.IsChained && player.Seat != target.Seat)
            .Sum(player => GetHostility(view, selfRole, player) +
                           (player.Hp <= 1 ? 22d : 0d));
    }

    private (double Score, string Reason) ScoreFangtianHalberdSlash(
        GameSnapshot view,
        PlayerSnapshot self,
        Role selfRole,
        LegalAction action)
    {
        var card = self.Hand.Concat(self.Equipment).Single(candidate => candidate.Id == action.CardId);
        var effectiveKind = action.PlayedCardKind ?? card.Kind;
        var profile = CardCatalog.Get(effectiveKind);
        var targets = action.TargetSeats
            .Select(seat => view.Players.Single(player => player.Seat == seat))
            .ToArray();
        var targetValue = targets.Sum(target =>
        {
            var hostility = GetHostility(view, selfRole, target);
            var finishingBonus = target.Hp <= 1 ? 28d : 0d;
            var pressureBonus = Math.Max(0, target.MaxHp - target.Hp) * 3d;
            return hostility + finishingBonus + pressureBonus;
        });
        var conversionCost = action.PlayedCardKind is { } && !IsSlashCard(card.Kind)
            ? Math.Max(0d, CardCatalog.Get(card.Kind).HandKeepValue - profile.HandKeepValue) * .6d
            : 0d;
        var alcoholBonus = self.HasAlcoholEffect ? 45d + (targets.Length - 1) * 20d : 0d;
        var score = profile.AiPlayValue + targetValue + targets.Length * 5d + alcoholBonus - conversionCost;
        return (
            score,
            $"发动方天画戟以最后的手牌杀依次攻击 {string.Join("、", targets.Select(target => $"座位 {target.Seat + 1}"))}；综合公开敌对、体力与濒死收益 {targetValue:0.#}，不读取目标暗牌。");
    }

    private static bool IsSlashCard(CardKind kind) =>
        kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;

    private static bool IsRedCard(Suit suit) =>
        suit is Suit.Heart or Suit.Diamond;

    private double ScoreNullificationFavor(
        GameSnapshot view,
        Role selfRole,
        PlayerSnapshot source,
        PlayerSnapshot? target,
        CardKind effectCardKind)
    {
        if (_policyVersion >= 2)
        {
            if (source.Seat == Seat) return 80;
            if (effectCardKind == CardKind.PeachGarden && target is not null)
                return target.Hp >= target.MaxHp ? 0 : target.Seat == Seat ? 95 : -GetHostility(view, selfRole, target);
            if (effectCardKind is CardKind.DrawTwo or CardKind.FiveGrains && target is not null)
                return target.Seat == Seat ? 95 : -GetHostility(view, selfRole, target);
            if (effectCardKind is CardKind.BarbarianAssault or CardKind.ArrowBarrage && target is not null)
                return target.Seat == Seat ? -95 : GetHostility(view, selfRole, target);
        }
        if (effectCardKind == CardKind.IronChain && target is not null)
        {
            var relation = target.Seat == Seat
                ? -95d
                : GetHostility(view, selfRole, target);
            return target.IsChained ? -relation : relation;
        }

        var offensiveTargeted = effectCardKind is
            CardKind.Duel or
            CardKind.Dismantlement or
            CardKind.Snatch or
            CardKind.FireAttack or
            CardKind.BorrowedSword or
            CardKind.Indulgence or
            CardKind.SupplyShortage or
            CardKind.Lightning;
        if (target is { Seat: var targetSeat } && targetSeat == Seat)
        {
            return -95d;
        }

        if (offensiveTargeted && target is not null)
        {
            return GetHostility(view, selfRole, target);
        }

        var sourceHostility = source.Seat == Seat
            ? -100d
            : GetHostility(view, selfRole, source);
        return effectCardKind switch
        {
            CardKind.PeachGarden => -sourceHostility * 0.55d,
            CardKind.DrawTwo or CardKind.FiveGrains => -sourceHostility * 0.9d,
            CardKind.BarbarianAssault or CardKind.ArrowBarrage => -sourceHostility * 1.1d,
            _ => -sourceHostility * 0.75d
        };
    }

    private double GetHostility(GameSnapshot view, Role selfRole, PlayerSnapshot target)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        if (view.ModeKind == ContentModeKind.NationalWarLite)
        {
            return GetNationalHostility(view, target);
        }

        if (self.TeamId is not null || target.TeamId is not null)
        {
            return target.Seat == Seat
                ? -1000d
                : string.Equals(self.TeamId, target.TeamId, StringComparison.Ordinal)
                    ? -100d
                    : 100d;
        }

        if (_policyVersion >= 2) return GetTacticalHostility(view, selfRole, target);
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

    private double GetNationalHostility(GameSnapshot view, PlayerSnapshot target)
    {
        if (target.Seat == Seat)
        {
            return -1000d;
        }

        var self = view.Players.Single(player => player.Seat == Seat);
        if (self.FactionId is null)
        {
            return 0d;
        }

        if (target.FactionId is null)
        {
            if (_policyVersion < 2)
            {
                return 0d;
            }

            var suspicion = _nationalEnemySuspicion.GetValueOrDefault(target.Seat);
            return Math.Clamp(suspicion * 24d, -72d, 72d);
        }

        return string.Equals(self.FactionId, target.FactionId, StringComparison.Ordinal)
            ? -100d
            : 100d;
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
