namespace CardGame.Core;

// 战意 evidence: the discarded category and the discarded entity cards are
// public; the buff split is reconstructible from the chosen category.
public sealed record ProgramZhanyiCategoryChosenEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, string Category, IReadOnlyList<int> DiscardedCardIds) : IGameEvent;
public sealed record ProgramZhanyiEquipmentPunishEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TargetSeat, int CardId, bool FromHand) : IGameEvent;

public sealed partial class GameEngine
{
    // 锦囊牌 for the hand-limit exemption: every non-equipment catalog trick.
    internal static IReadOnlyList<CardKind> ZhanyiTrickKinds { get; } = Enum.GetValues<CardKind>()
        .Where(kind => kind is not (CardKind.GeneralWeapon or CardKind.RedBloodBlade) &&
            GetProgramCardCategory(kind) == SkillProgramCardCategory.Trick)
        .Order().ToArray();

    private static string ZhanyiCategoryName(SkillProgramCardCategory category) => category switch
    {
        SkillProgramCardCategory.Basic => "基本牌",
        SkillProgramCardCategory.Trick => "锦囊牌",
        _ => "装备牌"
    };

    private IReadOnlyList<Card> ZhanyiOwnedCategoryCards(int ownerSeat, SkillProgramCardCategory category) =>
        GetHand(_players[ownerSeat]).Concat(_cardZones.CardsAt(CardLocation.Equipment(ownerSeat)))
            .Where(card => GetProgramCardCategory(card.Kind) == category)
            .OrderBy(card => card.Id).ToArray();

    // 战意 launch: the owner discards every owned hand/equipment card of one
    // category; the other two categories are empowered until the next own turn.
    private SkillProgramStepOutcome ZhanyiProgramChooseCategory(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive || _winner != Winner.None || _phase != TurnPhase.Play || owner.Seat != _currentSeat ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var options = new[] { SkillProgramCardCategory.Basic, SkillProgramCardCategory.Trick, SkillProgramCardCategory.Equipment }
            .Select(category => (Category: category, Cards: ZhanyiOwnedCategoryCards(owner.Seat, category)))
            .Where(item => item.Cards.Count > 0)
            .OrderBy(item => item.Category)
            .ToArray();
        if (options.Length == 0)
        {
            AddLog("SkillEffect", $"{owner.Name} 战意：没有可弃置的牌，技能落空。", owner.Seat);
            return SkillProgramStepOutcome.Continue;
        }
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = options.Select(item => new PromptChoice(
            new ChoiceId($"zhanyi-launch.frame-{frame.Id}.{item.Category.ToString().ToLowerInvariant()}"),
            $"弃置所有【{ZhanyiCategoryName(item.Category)}】（{item.Cards.Count} 张）。",
            item.Cards.Select(card => card.Id).ToArray(), [],
            new Dictionary<string, string>
            {
                ["program-action"] = "zhanyi-launch",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["category"] = item.Category.ToString()
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择弃置一种类别的所有牌。",
            choices.SelectMany(choice => choice.Cards).Distinct().ToArray(), [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择类别",
                "弃置一种类别的所有牌，直到你的下个回合开始，另外两种类别的牌获得战意加成。"),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveZhanyiCategoryChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Zhanyi category choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.ZhuLingZhanyiChooseCategory } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            !Enum.TryParse(selected.Parameters.GetValueOrDefault("category"), out SkillProgramCardCategory category))
            throw new InvalidOperationException("The Zhanyi category choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Zhanyi category chooser changed while suspended.");
        ClearPendingDecision();
        var owner = _players[active.OwnerSeat];
        var discarded = ZhanyiOwnedCategoryCards(active.OwnerSeat, category);
        if (!owner.IsAlive || _winner != Winner.None || discarded.Count == 0)
        {
            CancelProgramBindingAndCleanup(active, "战意的弃置类别已无牌可弃，剩余结算取消。");
            return;
        }
        var reason = new CardMoveReason($"skill-program.{active.SkillId}.launch");
        var handDiscards = discarded.Where(card => _cardZones.GetLocation(card.Id).Zone == CardZoneKind.Hand).ToArray();
        var equipmentDiscards = discarded.Except(handDiscards).ToArray();
        if (handDiscards.Length > 0)
            MoveCards(handDiscards, CardLocation.Hand(active.OwnerSeat), CardLocation.DiscardPile, reason);
        if (equipmentDiscards.Length > 0)
            MoveCards(equipmentDiscards, CardLocation.Equipment(active.OwnerSeat), CardLocation.DiscardPile, reason);
        if (category != SkillProgramCardCategory.Basic)
        {
            GrantProgramTurnCardDamageModifier(active,
                [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash], 1,
                SkillProgramDamageModifierExpiration.NextOwnerTurnStart,
                SkillProgramDamageModifierSourceScope.OwnerUsed);
            GrantProgramTurnRuleModifier(active, SkillRuleQuery.CardUseDistanceLimit,
                SkillRuleOperation.Unlimited, 0, []);
        }
        if (category != SkillProgramCardCategory.Trick)
            GrantProgramTurnHandLimitCardKindExemption(active, ZhanyiTrickKinds);
        SetProgramBooleanState(active, "zhanyi-basic", category != SkillProgramCardCategory.Basic);
        SetProgramBooleanState(active, "zhanyi-trick", category != SkillProgramCardCategory.Trick);
        SetProgramBooleanState(active, "zhanyi-equipment", category != SkillProgramCardCategory.Equipment);
        AddLog("SkillEffect",
            $"{owner.Name} 战意：弃置 {discarded.Count} 张【{ZhanyiCategoryName(category)}】，另外两种类别的牌直到下个回合开始获得加成。",
            active.OwnerSeat);
        AdvanceEventRulesAndQueueFact(new ProgramZhanyiCategoryChosenEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, category.ToString(),
            Array.AsReadOnly(discarded.Select(card => card.Id).ToArray())));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }

    // 战意 equipment punish: the owner's committed equipment use is about to
    // enter the equipment zone; pick another character's card to discard.
    private SkillProgramStepOutcome ZhanyiProgramEquipmentPunish(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive || _winner != Winner.None ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            !GetProgramBooleanState(active, "zhanyi-equipment"))
            return SkillProgramStepOutcome.Continue;
        var candidates = _players.Where(player => player.IsAlive && player.Seat != active.OwnerSeat &&
            (GetHand(player).Count > 0 ||
             _cardZones.CardsAt(CardLocation.Equipment(player.Seat)).Any(card =>
                 !IsForeignEquipmentDiscardPrevented(active.OwnerSeat, card,
                     CardLocation.Equipment(player.Seat), OwnedCardMoveIntent.Discard))))
            .Select(player => player.Seat).ToArray();
        if (candidates.Length == 0)
        {
            AddLog("SkillEffect", $"{owner.Name} 战意：没有可弃牌的其他角色，装备牌的战意加成落空。", active.OwnerSeat);
            return SkillProgramStepOutcome.Continue;
        }
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = candidates.Select(seat => new PromptChoice(
            new ChoiceId($"zhanyi-punish-target.frame-{frame.Id}.seat-{seat}"),
            $"选择 {_players[seat].Name}。",
            [], [seat],
            new Dictionary<string, string>
            {
                ["program-action"] = "zhanyi-punish-target",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["target"] = seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择一名其他角色弃置其一张牌。",
            [], candidates, active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择角色", "装备牌置入你的装备区时，令一名其他角色弃置一张牌。"),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveZhanyiPunishTargetChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Zhanyi punish target choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.ZhuLingZhanyiEquipmentPunish } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("target"),
                System.Globalization.CultureInfo.InvariantCulture, out var target))
            throw new InvalidOperationException("The Zhanyi punish target choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Zhanyi punish chooser changed while suspended.");
        ClearPendingDecision();
        if (!IsValidPlayerSeat(target) || target == active.OwnerSeat || !_players[target].IsAlive ||
            _winner != Winner.None || !_players[active.OwnerSeat].IsAlive)
        {
            CancelProgramBindingAndCleanup(active, "战意的弃牌目标已失效，剩余结算取消。");
            return;
        }
        var owner = _players[active.OwnerSeat];
        var victim = _players[target];
        var handCount = GetHand(victim).Count;
        var equipment = _cardZones.CardsAt(CardLocation.Equipment(target))
            .Where(card => !IsForeignEquipmentDiscardPrevented(active.OwnerSeat, card,
                CardLocation.Equipment(target), OwnedCardMoveIntent.Discard))
            .OrderBy(card => card.Id).ToArray();
        var choices = new List<PromptChoice>();
        foreach (var card in equipment)
            choices.Add(new PromptChoice(
                new ChoiceId($"zhanyi-punish-card.frame-{frame.Id}.card-{card.Id}"),
                $"弃置 {_players[target].Name} 的【{card.DisplayName}】。",
                [card.Id], [target],
                new Dictionary<string, string>
                {
                    ["program-action"] = "zhanyi-punish-card",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["target"] = target.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["card-id"] = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
        for (var slot = 0; slot < handCount; slot++)
            choices.Add(new PromptChoice(
                new ChoiceId($"zhanyi-punish-card.frame-{frame.Id}.hand-{slot}"),
                $"弃置 {_players[target].Name} 的一张暗牌（牌位 {slot + 1}）。",
                [], [target],
                new Dictionary<string, string>
                {
                    ["program-action"] = "zhanyi-punish-card",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["target"] = target.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["hand-slot"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
        if (choices.Count == 0)
        {
            AddLog("SkillEffect", $"{owner.Name} 战意：{_players[target].Name} 没有可弃置的牌，剩余结算取消。", active.OwnerSeat);
            AdvanceRuntimeProgram(active.Id);
            return;
        }
        var presentation = _contentRegistry.GetSkill(active.SkillId);
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择 {_players[target].Name} 的一张牌弃置。",
            choices.SelectMany(choice => choice.Cards).Distinct().ToArray(), [target], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = target,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择牌", "令其弃置一张牌；暗牌按牌位盲选。"),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveZhanyiPunishCardChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Zhanyi punish card choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.ZhuLingZhanyiEquipmentPunish } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("target"),
                System.Globalization.CultureInfo.InvariantCulture, out var target))
            throw new InvalidOperationException("The Zhanyi punish card choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Zhanyi punish chooser changed while suspended.");
        ClearPendingDecision();
        var owner = _players[active.OwnerSeat];
        if (!IsValidPlayerSeat(target) || target == active.OwnerSeat || !_players[target].IsAlive ||
            _winner != Winner.None || !owner.IsAlive)
        {
            CancelProgramBindingAndCleanup(active, "战意的弃牌目标已失效，剩余结算取消。");
            return;
        }
        var reason = new CardMoveReason($"skill-program.{active.SkillId}.equipment-punish");
        Card card;
        var fromHand = selected.Parameters.ContainsKey("hand-slot");
        if (fromHand)
        {
            var hand = GetHand(_players[target]).OrderBy(item => item.Id).ToArray();
            if (!int.TryParse(selected.Parameters.GetValueOrDefault("hand-slot"),
                    System.Globalization.CultureInfo.InvariantCulture, out var slot) ||
                slot < 0 || slot >= hand.Length)
            {
                CancelProgramBindingAndCleanup(active, "战意的暗牌牌位已失效，剩余结算取消。");
                return;
            }
            card = hand[slot];
        }
        else
        {
            var cardId = selected.Parameters.TryGetValue("card-id", out var cardText) &&
                int.TryParse(cardText, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
            if (!_cardZones.CardsAt(CardLocation.Equipment(target)).Any(item => item.Id == cardId))
            {
                CancelProgramBindingAndCleanup(active, "战意的装备牌已离开原处，剩余结算取消。");
                return;
            }
            card = _cardZones.CardsAt(CardLocation.Equipment(target)).Single(item => item.Id == cardId);
        }
        MoveCards([card], _cardZones.GetLocation(card.Id), CardLocation.DiscardPile, reason);
        AddLog("SkillTriggered",
            $"{owner.Name} 发动【{_contentRegistry.GetSkill(active.SkillId).Name}】，弃置 {_players[target].Name} 的【{card.DisplayName}】。",
            active.OwnerSeat, target);
        AdvanceEventRulesAndQueueFact(new ProgramZhanyiEquipmentPunishEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, target, card.Id, fromHand));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }

    private PromptChoice SelectAiZhanyiCategoryChoice(PendingDecision decision) =>
        decision.Choices.OrderBy(choice => choice.Cards.Count)
            .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal).First();

    private PromptChoice SelectAiZhanyiPunishChoice(PendingDecision decision) =>
        decision.Choices.OrderBy(choice => choice.Targets.Count)
            .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal).First();

    private sealed partial class ProgramSkillHost : IZhuLingProgramHost
    {
        public SkillProgramStepOutcome ZhuLingZhanyiChooseCategory(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZhanyiProgramChooseCategory(frame, effect);
        public SkillProgramStepOutcome ZhuLingZhanyiEquipmentPunish(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZhanyiProgramEquipmentPunish(frame, effect);
    }
}
