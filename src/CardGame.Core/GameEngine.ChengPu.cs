namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string LihuoSkillId = "classic:lihuo";
    private const string ChunlaoSkillId = "classic:chunlao";
    private ChunlaoResolution? _pendingChunlao;

    private bool UsesFormalLihuo =>
        HasClassicGeneralPackage(new Version(1, 91, 0));

    private bool HasLihuo(PlayerRuntime owner) =>
        UsesFormalLihuo && HasRuntimeSkill(owner, LihuoSkillId);

    private bool UsesFormalChunlao =>
        HasClassicGeneralPackage(new Version(1, 92, 0));

    private bool HasChunlao(PlayerRuntime owner) =>
        UsesFormalChunlao && HasRuntimeSkill(owner, ChunlaoSkillId);

    private IReadOnlyList<Card> GetChunlaoCards(PlayerRuntime owner) =>
        _cardZones.CardsAt(CardLocation.Chunlao(owner.Seat));

    private CardConversionSource CreateLihuoConversionSource(PlayerRuntime owner) =>
        new(
            LihuoSkillId,
            "ordinary-slash-to-fire-slash",
            owner.Seat,
            $"seat-{owner.Seat}:{LihuoSkillId}");

    private CardConversionSource CreateChunlaoConversionSource(PlayerRuntime owner) =>
        new(
            ChunlaoSkillId,
            "chun-as-alcohol",
            owner.Seat,
            $"seat-{owner.Seat}:{ChunlaoSkillId}");

    private bool TryBeginChunlaoChoice(PlayerRuntime owner)
    {
        if (!UsesFormalChunlao ||
            _chunlaoResolvedThisTurn ||
            !owner.IsAlive ||
            !HasChunlao(owner) ||
            GetChunlaoCards(owner).Count > 0)
        {
            return false;
        }

        var slashes = GetHand(owner)
            .Where(card => IsSlashCard(card.Kind))
            .OrderBy(card => card.Id)
            .ToArray();
        if (slashes.Length == 0)
        {
            return false;
        }

        if (!owner.IsHuman)
        {
            var (cardIds, thought) = _aiBrains[owner.Seat].ChooseChunlaoStorage(
                CreateSnapshot(owner.Seat),
                slashes,
                ++_thoughtSequence);
            AddThought(thought);
            var selected = cardIds.Select(id => slashes.Single(card => card.Id == id)).ToArray();
            if (selected.Length > 0)
            {
                StoreChunlaoCards(owner, selected);
            }
            else
            {
                AddLog("SkillSkipped", $"{owner.Name} 未发动【醇醪】。", owner.Seat);
            }
            _chunlaoResolvedThisTurn = true;
            return false;
        }

        _pendingChunlao = new ChunlaoResolution(owner.Seat);
        BeginChunlaoSelectionPrompt(_pendingChunlao);
        return true;
    }

    private void BeginChunlaoSelectionPrompt(ChunlaoResolution pending)
    {
        var owner = _players[pending.OwnerSeat];
        var selected = pending.SelectedCardIds.ToHashSet();
        var remaining = GetHand(owner)
            .Where(card => IsSlashCard(card.Kind) && !selected.Contains(card.Id))
            .OrderBy(card => card.Id)
            .ToArray();
        var choices = remaining.Select(card => new PromptChoice(
            new ChoiceId($"chunlao.select.card-{card.Id}"),
            $"选择【{card.DisplayName}】作为“醇”（已选 {selected.Count} 张）。",
            [card.Id],
            [],
            new Dictionary<string, string> { ["action"] = "chunlao-select" }))
            .ToList();
        if (selected.Count == 0)
        {
            choices.Add(new PromptChoice(
                new ChoiceId("chunlao.skip"),
                "不发动【醇醪】。",
                [],
                [],
                new Dictionary<string, string> { ["action"] = "chunlao-skip" }));
        }
        else
        {
            choices.Add(new PromptChoice(
                new ChoiceId("chunlao.finish"),
                $"将已选的 {selected.Count} 张【杀】置于武将牌上，称为“醇”。",
                [],
                [],
                new Dictionary<string, string> { ["action"] = "chunlao-finish" }));
        }

        _pendingDecision = new PendingDecision(
            DecisionKind.Chunlao,
            owner.Seat,
            selected.Count == 0
                ? $"{owner.Name} 的结束阶段：是否发动【醇醪】，选择至少一张【杀】作为“醇”？"
                : $"【醇醪】已选 {selected.Count} 张【杀】；可继续选择或完成。",
            remaining.Select(card => card.Id).ToArray(),
            [])
        {
            PromptId = CreatePromptId(),
            Choices = choices
        };
        _status = EngineStatus.AwaitingHumanCardSelection;
    }

    private CommandResult SubmitChunlaoPromptAnswer(PromptChoice selected)
    {
        if (_pendingChunlao is null ||
            _pendingDecision is not { Kind: DecisionKind.Chunlao } ||
            !selected.Parameters.TryGetValue("action", out var action))
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的醇醪结束阶段窗口。");
        }

        return action switch
        {
            "chunlao-select" when selected.Cards.Count == 1 && selected.Targets.Count == 0 =>
                Accept(() => HumanChunlaoCore(
                    selected.Cards[0], finish: false, skip: false,
                    _options.AdvanceAfterHumanCommands)),
            "chunlao-finish" when selected.Cards.Count == 0 && selected.Targets.Count == 0 =>
                Accept(() => HumanChunlaoCore(
                    cardId: null, finish: true, skip: false,
                    _options.AdvanceAfterHumanCommands)),
            "chunlao-skip" when selected.Cards.Count == 0 && selected.Targets.Count == 0 =>
                Accept(() => HumanChunlaoCore(
                    cardId: null, finish: false, skip: true,
                    _options.AdvanceAfterHumanCommands)),
            _ => Reject(CommandErrorCode.InvalidChoice, "醇醪选择不符合当前结束阶段窗口。")
        };
    }

    private EngineRunResult HumanChunlaoCore(
        int? cardId,
        bool finish,
        bool skip,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Chunlao);
        ResolveChunlaoChoice(cardId, finish, skip);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private void ResolveChunlaoChoice(int? cardId, bool finish, bool skip)
    {
        var pending = _pendingChunlao ??
            throw new InvalidOperationException("There is no Chunlao selection to resolve.");
        var owner = _players[pending.OwnerSeat];
        if (!owner.IsAlive || !HasChunlao(owner) || GetChunlaoCards(owner).Count > 0)
        {
            throw new InvalidOperationException("Chunlao is no longer legal for its owner.");
        }

        if (skip)
        {
            if (pending.SelectedCardIds.Count != 0 || cardId is not null || finish)
            {
                throw new InvalidOperationException("Chunlao can only be skipped before selecting a Slash.");
            }
            _pendingChunlao = null;
            ClearPendingDecision();
            _chunlaoResolvedThisTurn = true;
            AddLog("SkillSkipped", $"{owner.Name} 未发动【醇醪】。", owner.Seat);
            EndTurn();
            return;
        }

        if (cardId is { } selectedCardId)
        {
            if (finish || pending.SelectedCardIds.Contains(selectedCardId) ||
                GetHand(owner).SingleOrDefault(card => card.Id == selectedCardId) is not { } card ||
                !IsSlashCard(card.Kind))
            {
                throw new InvalidOperationException("The selected Chunlao card is not an unselected Slash in hand.");
            }
            pending.SelectedCardIds.Add(selectedCardId);
            ClearPendingDecision();
            BeginChunlaoSelectionPrompt(pending);
            return;
        }

        if (!finish || pending.SelectedCardIds.Count == 0)
        {
            throw new InvalidOperationException("Chunlao must select at least one Slash before finishing.");
        }

        var cards = pending.SelectedCardIds.Select(id =>
            GetHand(owner).SingleOrDefault(card => card.Id == id) ??
            throw new InvalidOperationException("A selected Chunlao Slash left its owner's hand."))
            .ToArray();
        if (cards.Any(card => !IsSlashCard(card.Kind)))
        {
            throw new InvalidOperationException("Only Slash cards may become Chun.");
        }
        _pendingChunlao = null;
        ClearPendingDecision();
        StoreChunlaoCards(owner, cards);
        _chunlaoResolvedThisTurn = true;
        EndTurn();
    }

    private void StoreChunlaoCards(PlayerRuntime owner, IReadOnlyList<Card> cards)
    {
        if (cards.Count == 0 || GetChunlaoCards(owner).Count != 0 ||
            cards.Any(card => !IsSlashCard(card.Kind) ||
                              _cardZones.GetLocation(card.Id) != CardLocation.Hand(owner.Seat)))
        {
            throw new InvalidOperationException("Chunlao requires at least one Slash from an empty Chun pile.");
        }
        MoveCards(cards, CardLocation.Hand(owner.Seat), CardLocation.Chunlao(owner.Seat), CardMoveReasons.ChunlaoStore);
        var ids = Array.AsReadOnly(cards.Select(card => card.Id).ToArray());
        QueueGameEvent(new ChunlaoStoredEvent(owner.Seat, ids));
        AddLog("SkillTriggered", $"{owner.Name} 发动【醇醪】，将 {cards.Count} 张【杀】置于武将牌上作为“醇”。", owner.Seat);
    }

    private Card[] GetAvailableChunlaoCards(PlayerRuntime owner, DyingResolution dying) =>
        HasChunlao(owner) && owner.IsAlive && !dying.ChunlaoUsedOwnerSeats.Contains(owner.Seat)
            ? GetChunlaoCards(owner).ToArray()
            : [];

    private void ResolveChunlaoRescue(
        PlayerRuntime owner,
        PlayerRuntime victim,
        DyingResolution dying,
        int? requestedCardId)
    {
        var chun = GetAvailableChunlaoCards(owner, dying).FirstOrDefault(card =>
            !requestedCardId.HasValue || card.Id == requestedCardId.Value) ??
            throw new InvalidOperationException("The requested Chun card is not available for this dying occurrence.");
        if (victim.Hp > 0 || !victim.IsAlive || !dying.ChunlaoUsedOwnerSeats.Add(owner.Seat))
        {
            throw new InvalidOperationException("Chunlao cannot resolve in the current dying occurrence.");
        }

        var resolutionId = BeginCardUse(
            chun,
            victim.Seat,
            [victim.Seat],
            playedCardKind: CardKind.Alcohol,
            conversionSource: CreateChunlaoConversionSource(owner));
        MoveCard(chun, CardLocation.Chunlao(owner.Seat), CardLocation.Processing, CardMoveReasons.ChunlaoRescue);
        SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);
        var recoveryFrameId = BeginRecovery(resolutionId, victim.Seat, victim.Seat, 1);
        try
        {
            victim.Hp = Math.Min(victim.MaxHp, victim.Hp + 1);
            QueueGameEvent(new RecoveryAppliedEvent(victim.Seat, victim.Seat, 1, victim.Hp));
        }
        finally
        {
            PopResolutionFrame(recoveryFrameId, ResolutionFrameKind.Recovery);
        }
        MoveCard(chun, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.ChunlaoRescue);
        FinishCardUse(resolutionId, chun, CardKind.Alcohol);
        QueueGameEvent(new ChunlaoRescueEvent(
            dying.FrameId,
            owner.Seat,
            victim.Seat,
            chun.Id,
            1,
            victim.Hp));
        AddLog(
            "SkillTriggered",
            $"{owner.Name} 发动【醇醪】，移去一张“醇”，令 {victim.Name} 视为使用【酒】并回复1点体力。",
            owner.Seat,
            victim.Seat);
    }

    private void AssertChengPuInvariant()
    {
        if (_pendingChunlao is { } pending &&
            (!UsesFormalChunlao ||
             _pendingDecision is not { Kind: DecisionKind.Chunlao, PlayerSeat: var seat } ||
             seat != pending.OwnerSeat ||
             pending.SelectedCardIds.Count != pending.SelectedCardIds.Distinct().Count() ||
             pending.SelectedCardIds.Any(id =>
                 _cardZones.GetLocation(id) != CardLocation.Hand(pending.OwnerSeat))))
        {
            throw new InvalidOperationException("A Chunlao prompt lost its owner, exact selection or hand-card state.");
        }
    }

    private IReadOnlyList<SlashUseVariant> GetSlashUseVariants(
        PlayerRuntime actor,
        CardKind baseEffectiveKind)
    {
        var variants = new List<SlashUseVariant>
        {
            new(baseEffectiveKind, CardKindModifierSkill: null, UsesZhuqueFan: false)
        };
        if (baseEffectiveKind != CardKind.Slash)
        {
            return variants;
        }

        if (HasLihuo(actor))
        {
            variants.Add(new SlashUseVariant(
                CardKind.FireSlash,
                SkillKind.Lihuo,
                UsesZhuqueFan: false));
        }
        if (HasZhuqueFan(actor))
        {
            variants.Add(new SlashUseVariant(
                CardKind.FireSlash,
                CardKindModifierSkill: null,
                UsesZhuqueFan: true));
        }
        return variants;
    }

    private void AddLihuoSlashActions(
        ICollection<LegalAction> actions,
        PlayerRuntime actor,
        Card physicalCard,
        IReadOnlyList<PlayerRuntime> legalTargets,
        string slashName,
        CardKind? playedCardKind,
        CardConversionSource? conversionSource = null,
        SkillKind? cardKindModifierSkill = null)
    {
        var effectiveKind = playedCardKind ?? physicalCard.Kind;
        if (effectiveKind != CardKind.FireSlash || !HasLihuo(actor))
        {
            return;
        }

        var usesFangtian = UsesFormalFangtianHalberd &&
                           GetHand(actor).Count == 1 &&
                           GetHand(actor)[0].Id == physicalCard.Id &&
                           GetEquipment(actor).Any(card => card.Kind == CardKind.FangtianHalberd);
        var existingMaximum = usesFangtian
            ? actor.TianyiWonThisTurn ? 4 : 3
            : actor.TianyiWonThisTurn ? 2 : 1;
        var targetCount = existingMaximum + 1;
        if (legalTargets.Count < targetCount)
        {
            return;
        }

        var orderedTargets = legalTargets
            .OrderBy(player => (player.Seat - actor.Seat + _playerCount) % _playerCount)
            .ToArray();
        AddCombinations(0, []);

        void AddCombinations(int startIndex, IReadOnlyList<PlayerRuntime> selected)
        {
            if (selected.Count == targetCount)
            {
                var targetSeats = Array.AsReadOnly(selected.Select(target => target.Seat).ToArray());
                var combinedEffects = new List<string>();
                if (usesFangtian) combinedEffects.Add("方天画戟");
                if (actor.TianyiWonThisTurn) combinedEffects.Add("天义");
                combinedEffects.Add("疠火");
                actions.Add(new LegalAction(
                    LegalActionKind.Slash,
                    physicalCard.Id,
                    targetSeats[0],
                    DescribeConversion(
                        conversionSource,
                        $"发动【{string.Join("】【", combinedEffects)}】，以【{slashName}】指定 {string.Join("、", selected.Select(target => target.Name))}"),
                    PlayedCardKind: playedCardKind,
                    TargetSeats: targetSeats)
                {
                    ConversionSource = conversionSource,
                    CardKindModifierSkill = cardKindModifierSkill,
                    TargetCountModifierSkill = SkillKind.Lihuo
                });
                return;
            }

            for (var index = startIndex;
                 index <= orderedTargets.Length - (targetCount - selected.Count);
                 index++)
            {
                AddCombinations(index + 1, [.. selected, orderedTargets[index]]);
            }
        }
    }

    private bool TryApplyLihuoPenaltyAfterCardUse(AttackResolution attack)
    {
        if (!attack.IsLihuoConversion ||
            !attack.LihuoCausedDamage ||
            attack.LihuoPenaltyApplied ||
            _status == EngineStatus.Completed)
        {
            return false;
        }

        attack.MarkLihuoPenaltyApplied();
        var owner = _players[attack.CardUserSeat];
        if (!owner.IsAlive)
        {
            return false;
        }

        owner.Hp = Math.Max(0, owner.Hp - 1);
        QueueGameEvent(new SkillHpLostEvent(
            attack.ResolutionId,
            owner.Seat,
            SkillKind.Lihuo,
            1,
            owner.Hp));
        AddLog(
            "SkillCost",
            $"{owner.Name} 以【疠火】转化的【火杀】造成过伤害，结算结束后失去1点体力。",
            owner.Seat);
        if (owner.Hp > 0)
        {
            return false;
        }

        BeginLihuoDying(attack, owner);
        return true;
    }

    private void BeginLihuoDying(AttackResolution attack, PlayerRuntime owner)
    {
        if (_pendingDying is not null)
        {
            throw new InvalidOperationException("Lihuo cannot open a second dying continuation.");
        }

        var responderSeats = Array.AsReadOnly(BuildDyingResponderSeats(owner.Seat).ToArray());
        var frameId = ++_resolutionSequence;
        _resolutionStack.Add(new DyingFrame(
            frameId,
            attack.ResolutionId,
            owner.Seat,
            KillerSeat: null,
            responderSeats,
            ResponderIndex: 0));
        _pendingDying = new DyingResolution(
            frameId,
            damageFrameId: null,
            attack,
            owner.Seat,
            killerSeat: null,
            responderSeats,
            attack.ResolutionId,
            DyingContinuation.Lihuo);
        QueueGameEvent(new PlayerDyingEvent(frameId, owner.Seat, KillerSeat: null));
        _status = EngineStatus.Running;
        if (!TryResolveBuqu(_pendingDying))
        {
            ExposeHumanDyingPrompt();
        }
    }

    private void CompleteLihuoAfterDying(DyingResolution dying)
    {
        var attack = dying.Attack ??
            throw new InvalidOperationException("A Lihuo dying continuation lost its Slash use.");
        CompleteAttackAfterCardResolution(attack);
    }

    private sealed record SlashUseVariant(
        CardKind EffectiveKind,
        SkillKind? CardKindModifierSkill,
        bool UsesZhuqueFan);

    private sealed class ChunlaoResolution(int ownerSeat)
    {
        public int OwnerSeat { get; } = ownerSeat;
        public List<int> SelectedCardIds { get; } = [];
    }
}
