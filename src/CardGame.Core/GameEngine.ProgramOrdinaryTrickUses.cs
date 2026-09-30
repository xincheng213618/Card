namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome UseProgramAllHandCardsAsOrdinaryTrick(
        ProgramSkillFrame frame,
        string viewAsId)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.TriggerId is not null || active.OwnerSeat != frame.OwnerSeat ||
            active.SkillId != frame.SkillId ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame current || current.Id != frame.Id)
            throw new InvalidOperationException(
                "An all-hand ordinary-trick use requires the current active program activation.");
        var owner = _players[frame.OwnerSeat];
        var hand = GetHand(owner).OrderBy(card => card.Id).ToArray();
        if (hand.Length == 0 ||
            !active.SelectedCardIds.Order().SequenceEqual(hand.Select(card => card.Id)) ||
            hand.Any(card => IsTurnHandCardRestricted(owner, card)))
            throw new InvalidOperationException(
                "The all-hand ordinary-trick use no longer owns exactly its selected unrestricted hand cards.");

        var options = BuildProgramOrdinaryTrickUseOptions(owner);
        if (options.Count == 0)
            throw new InvalidOperationException("No ordinary trick is currently legal for the selected hand cards.");
        var skill = _contentRegistry!.GetSkill(active.SkillId);
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            owner.Seat,
            $"【{skill.Name}】已选择全部 {hand.Length} 张手牌，请选择要视为使用的普通锦囊及其合法目标。",
            active.SelectedCardIds,
            options.SelectMany(option => option.TargetSeats).Distinct().Order().ToArray(),
            SourceSeat: owner.Seat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            SkillPrompt = new(active.SkillId, skill.Name, $"{skill.Name} · 选择普通锦囊", skill.Description),
            Choices = options.Select(option => CreateProgramOrdinaryTrickPromptChoice(active, viewAsId, option)).ToArray()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveProgramOrdinaryTrickUseChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The ordinary-trick choice lost its program frame.");
        var effect = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("view-as-id") != effect.SourceBind)
            throw new InvalidOperationException(
                "The ordinary-trick choice does not match its suspended program instruction.");

        var owner = _players[frame.OwnerSeat];
        var hand = GetHand(owner).OrderBy(card => card.Id).ToArray();
        if (!owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, frame.SkillId, frame.SkillInstanceId) ||
            hand.Length == 0 ||
            !frame.SelectedCardIds.Order().SequenceEqual(hand.Select(card => card.Id)) ||
            hand.Any(card => IsTurnHandCardRestricted(owner, card)))
            throw new InvalidOperationException(
                "The ordinary-trick choice lost its owner, skill instance or exact all-hand cost.");
        var option = BuildProgramOrdinaryTrickUseOptions(owner)
            .SingleOrDefault(candidate => candidate.Id == selected.Id) ??
            throw new InvalidOperationException("The selected ordinary-trick use is no longer legal.");
        if (!selected.Targets.SequenceEqual(option.TargetSeats))
            throw new InvalidOperationException("The ordinary-trick targets changed while the choice was pending.");

        ClearPendingDecision();
        var representative = hand[0];
        var physicalCardIds = Array.AsReadOnly(hand.Select(card => card.Id).ToArray());
        var source = new CardConversionSource(
            frame.SkillId,
            effect.SourceBind!,
            owner.Seat,
            frame.SkillInstanceId);
        var resolutionId = BeginCardUse(
            representative,
            owner.Seat,
            option.TargetSeats,
            option.EffectiveCardKind,
            physicalCardIds: physicalCardIds,
            conversionSource: source);
        MoveCards(hand, CardLocation.Hand(owner.Seat), CardLocation.Processing, CardMoveReasons.Use);
        QueueGameEvent(new ProgramViewAsConvertedEvent(
            resolutionId,
            frame.SkillId,
            effect.SourceBind!,
            owner.Seat,
            physicalCardIds,
            option.EffectiveCardKind,
            IsUse: true,
            option.TargetSeats));
        AddLog(
            "SkillTriggered",
            $"{owner.Name} 发动【{_contentRegistry.GetSkill(frame.SkillId).Name}】，将全部 {physicalCardIds.Count} 张手牌当【{CardCatalog.Get(option.EffectiveCardKind).DisplayName}】使用。",
            owner.Seat,
            option.TargetSeats.FirstOrDefault(-1));
        BeginJizhiOrNullificationWindow(
            resolutionId,
            representative,
            owner.Seat,
            option.TargetSeats,
            option.ActionKind,
            option.TargetCardId,
            option.RequiredCardKind,
            option.EffectiveCardKind);
    }

    private IReadOnlyList<ProgramOrdinaryTrickUseOption> BuildProgramOrdinaryTrickUseOptions(CharacterState source)
    {
        var options = new List<ProgramOrdinaryTrickUseOption>();
        // A multi-card virtual card only carries a suit while every physical card
        // shares it; mixed-suit combinations are colorless and pass suit shields.
        var handSuits = GetHand(source).Select(card => card.Suit).Distinct().ToArray();
        var virtualSuit = handSuits.Length == 1 ? handSuits[0] : (Suit?)null;
        void Add(
            CardKind cardKind,
            LegalActionKind actionKind,
            string description,
            IReadOnlyList<int>? targetSeats = null,
            int? targetCardId = null,
            CardKind? requiredCardKind = null)
        {
            var targets = targetSeats?.ToArray() ?? [];
            var targetPart = targets.Length == 0 ? "none" : string.Join('-', targets);
            var cardPart = targetCardId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";
            options.Add(new ProgramOrdinaryTrickUseOption(
                new ChoiceId($"ordinary-trick.{cardKind}.targets-{targetPart}.card-{cardPart}"),
                cardKind,
                actionKind,
                Array.AsReadOnly(targets),
                targetCardId,
                requiredCardKind,
                description));
        }

        Add(CardKind.DrawTwo, LegalActionKind.DrawTwo, "当【无中生有】使用：摸两张牌");

        if (CanUseGlobalCard(source, CardKind.BarbarianAssault) ||
            CanUseGlobalCard(source, CardKind.ArrowBarrage))
        {
            var otherSeats = Enumerable.Range(1, _playerCount - 1)
                .Select(offset => _players[(source.Seat + offset) % _playerCount])
                .Where(player => player.IsAlive)
                .Select(player => player.Seat)
                .ToArray();
            var barbarianTargets = otherSeats.Where(seat =>
                    !HasCardPolicy(_players[seat], SkillProgramCardPolicyKind.ExcludeGlobalTarget,
                        CardKind.BarbarianAssault) &&
                    !IsCardTargetProhibited(_players[seat], CardKind.BarbarianAssault, virtualSuit))
                .ToArray();
            var arrowTargets = otherSeats.Where(seat =>
                    !IsCardTargetProhibited(_players[seat], CardKind.ArrowBarrage, virtualSuit))
                .ToArray();
            if (CanUseGlobalCard(source, CardKind.BarbarianAssault) && barbarianTargets.Length > 0)
                Add(CardKind.BarbarianAssault, LegalActionKind.BarbarianAssault,
                    "当【南蛮入侵】使用：其他角色依次响应【杀】", barbarianTargets,
                    requiredCardKind: CardKind.Slash);
            if (CanUseGlobalCard(source, CardKind.ArrowBarrage) && arrowTargets.Length > 0)
                Add(CardKind.ArrowBarrage, LegalActionKind.ArrowBarrage,
                    "当【万箭齐发】使用：其他角色依次响应【闪】", arrowTargets,
                    requiredCardKind: CardKind.Dodge);
        }

        var allAliveSeats = Enumerable.Range(0, _playerCount)
            .Select(offset => _players[(source.Seat + offset) % _playerCount])
            .Where(player => player.IsAlive)
            .Select(player => player.Seat)
            .ToArray();
        if (CanUseGlobalCard(source, CardKind.PeachGarden))
        {
            var peachTargets = allAliveSeats.Where(seat =>
                    !IsCardTargetProhibited(_players[seat], CardKind.PeachGarden, virtualSuit))
                .ToArray();
            if (peachTargets.Length > 0)
                Add(CardKind.PeachGarden, LegalActionKind.PeachGarden,
                    "当【桃园结义】使用：所有存活角色依次回复体力", peachTargets);
        }
        var availableCards = _cardZones.Count(CardLocation.DrawPile) + _cardZones.Count(CardLocation.DiscardPile);
        if (CanUseGlobalCard(source, CardKind.FiveGrains))
        {
            var grainTargets = allAliveSeats.Where(seat =>
                    !IsCardTargetProhibited(_players[seat], CardKind.FiveGrains, virtualSuit))
                .Take(availableCards).ToArray();
            if (grainTargets.Length > 0)
                Add(CardKind.FiveGrains, LegalActionKind.FiveGrains,
                    "当【五谷丰登】使用：所有存活角色依次选牌", grainTargets);
        }

        foreach (var target in _players.Where(player =>
                     player.IsAlive && player.Seat != source.Seat &&
                     !IsCardTargetProhibited(player, CardKind.Duel, virtualSuit) &&
                     !IsDirectedCardTargetProhibited(source.Seat, player.Seat, CardKind.Duel)))
            Add(CardKind.Duel, LegalActionKind.Duel, $"当【决斗】对 {target.Name} 使用", [target.Seat]);

        foreach (var target in _players.Where(player =>
                     player.IsAlive && player.Seat != source.Seat && HasTargetCard(player) &&
                     !IsCardTargetProhibited(player, CardKind.Dismantlement, virtualSuit) &&
                     !IsDirectedCardTargetProhibited(source.Seat, player.Seat, CardKind.Dismantlement)))
            AddProgramOrdinaryTrickTargetCardOptions(options, source, target, CardKind.Dismantlement,
                LegalActionKind.Dismantlement, "过河拆桥");

        foreach (var target in _players.Where(player =>
                     player.IsAlive && player.Seat != source.Seat &&
                     (HasCardDistanceExemption(source, player, CardKind.Snatch) ||
                      HasCardPolicy(source, SkillProgramCardPolicyKind.IgnoreUseDistance,
                          CardKind.Snatch) ||
                      GetCombatDistance(source.Seat, player.Seat) == 1) &&
                     HasTargetCard(player) && !IsCardTargetProhibited(player, CardKind.Snatch, virtualSuit) &&
                     !IsDirectedCardTargetProhibited(source.Seat, player.Seat, CardKind.Snatch)))
            AddProgramOrdinaryTrickTargetCardOptions(options, source, target, CardKind.Snatch,
                LegalActionKind.Snatch, "顺手牵羊");

        foreach (var target in _players.Where(player =>
                     player.IsAlive && GetHand(player).Count > 0 &&
                     !IsCardTargetProhibited(player, CardKind.FireAttack, virtualSuit) &&
                     !IsDirectedCardTargetProhibited(source.Seat, player.Seat, CardKind.FireAttack)))
            Add(CardKind.FireAttack, LegalActionKind.FireAttack, $"当【火攻】对 {target.Name} 使用", [target.Seat]);

        var chainTargets = _players.Where(player =>
                player.IsAlive && !IsCardTargetProhibited(player, CardKind.IronChain, virtualSuit) &&
                !IsDirectedCardTargetProhibited(source.Seat, player.Seat, CardKind.IronChain))
            .OrderBy(player => player.Seat).ToArray();
        foreach (var target in chainTargets)
            Add(CardKind.IronChain, LegalActionKind.IronChain, $"当【铁索连环】对 {target.Name} 使用", [target.Seat]);
        for (var first = 0; first < chainTargets.Length - 1; first++)
        for (var second = first + 1; second < chainTargets.Length; second++)
            Add(CardKind.IronChain, LegalActionKind.IronChain,
                $"当【铁索连环】对 {chainTargets[first].Name}、{chainTargets[second].Name} 使用",
                [chainTargets[first].Seat, chainTargets[second].Seat]);

        foreach (var weaponOwner in _players.Where(player =>
                     player.IsAlive && player.Seat != source.Seat && GetWeapon(player) is not null &&
                     !IsCardTargetProhibited(player, CardKind.BorrowedSword, virtualSuit) &&
                     !IsDirectedCardTargetProhibited(source.Seat, player.Seat, CardKind.BorrowedSword)))
        foreach (var slashTarget in _players.Where(player => IsLegalBorrowedSwordSlashTarget(weaponOwner, player)))
            Add(CardKind.BorrowedSword, LegalActionKind.BorrowedSword,
                $"当【借刀杀人】使用：令 {weaponOwner.Name} 对 {slashTarget.Name} 使用【杀】，否则获得其武器",
                [weaponOwner.Seat, slashTarget.Seat]);

        return Array.AsReadOnly(options.ToArray());
    }

    private void AddProgramOrdinaryTrickTargetCardOptions(
        ICollection<ProgramOrdinaryTrickUseOption> options,
        CharacterState source,
        CharacterState target,
        CardKind cardKind,
        LegalActionKind actionKind,
        string cardName)
    {
        void AddTarget(int? targetCardId, string suffix)
        {
            var cardPart = targetCardId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";
            options.Add(new ProgramOrdinaryTrickUseOption(
                new ChoiceId($"ordinary-trick.{cardKind}.targets-{target.Seat}.card-{cardPart}"),
                cardKind,
                actionKind,
                Array.AsReadOnly(new[] { target.Seat }),
                targetCardId,
                RequiredCardKind: null,
                $"当【{cardName}】对 {target.Name} 使用{suffix}"));
        }

        if (GetHand(target).Count > 0) AddTarget(null, "，选择其一张暗置手牌");
        foreach (var equipment in GetEquipment(target)) AddTarget(equipment.Id, $"，选择其装备【{equipment.DisplayName}】");
        foreach (var judgment in GetJudgment(target)) AddTarget(judgment.Id, $"，选择其判定区【{judgment.DisplayName}】");
    }

    private static PromptChoice CreateProgramOrdinaryTrickPromptChoice(
        ProgramSkillFrame frame,
        string viewAsId,
        ProgramOrdinaryTrickUseOption option) =>
        new(
            option.Id,
            option.Description,
            [],
            option.TargetSeats,
            new Dictionary<string, string>
            {
                ["program-action"] = "use-all-hand-as-ordinary-trick",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["view-as-id"] = viewAsId,
                ["card-kind"] = option.EffectiveCardKind.ToString(),
                ["action-kind"] = option.ActionKind.ToString(),
                ["target-card-id"] = option.TargetCardId?.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
            });

    private sealed record ProgramOrdinaryTrickUseOption(
        ChoiceId Id,
        CardKind EffectiveCardKind,
        LegalActionKind ActionKind,
        IReadOnlyList<int> TargetSeats,
        int? TargetCardId,
        CardKind? RequiredCardKind,
        string Description);
}
