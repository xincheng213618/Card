namespace CardGame.Core;

// 鬻爵 evidence: one scalar record per accepted invocation. The abolished slot,
// the card recipient and the given card id are all public; the 执笏 grant is
// rebuilt by replaying the accepted command, so the event carries no hidden ids
// beyond the given card's public hand-to-hand movement.
public sealed record ProgramYujueInvokedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, string AbolishedSlot, int TargetSeat, int GivenCardId) : IGameEvent;
public sealed record ProgramYujueSlotPaidEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, int InstructionIndex, string AbolishedSlot) : IGameEvent;
public sealed record ProgramYujueGiftPaidEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, int InstructionIndex, int TargetSeat, int GivenCardId, int MovementSequence) : IGameEvent;

// Shared game-long damage bonus: arming evidence for one seat. Any content skill
// can arm its owner through this generic event; FinalizeAttackDamageAmount adds
// the armed amount to every damage the seat deals (chain propagation included),
// so the armed state is derived from committed history on cold recovery.
public sealed record ProgramGameDamageBonusArmedEvent(long FrameId, string SkillId, string BindingId,
    string SkillInstanceId, int OwnerSeat, int Amount) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public YujuePendingState? YujuePending { get; init; }
}

// The suspension receipt of one accepted 鬻爵 invocation. AbolishedSlot is set
// once the slot prompt resolves; TargetSeat joins for the card-give prompt.
public enum YujuePendingStage { SlotPaid, GiftPaid }
public sealed record YujuePendingState(string AbolishedSlot, int? TargetSeat,
    CardConversionSource Source, string GameplayHash, int InstructionIndex,
    YujuePendingStage Stage = YujuePendingStage.SlotPaid, int? GivenCardId = null, int? GiftMovementSequence = null);

public sealed partial class GameEngine
{
    internal static readonly EquipmentSlot[] YujueSlotOrder =
        [EquipmentSlot.Weapon, EquipmentSlot.Armor, EquipmentSlot.OffensiveHorse,
         EquipmentSlot.DefensiveHorse, EquipmentSlot.Treasure];

    // Abolish preference for AI: the least combat-relevant slot first; empty
    // slots are preferred over equipped ones at the same preference rank.
    private static readonly EquipmentSlot[] YujueAiAbolishPreference =
        [EquipmentSlot.Treasure, EquipmentSlot.DefensiveHorse, EquipmentSlot.OffensiveHorse,
         EquipmentSlot.Armor, EquipmentSlot.Weapon];

    internal static string YujueSlotDisplayName(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Weapon => "武器栏",
        EquipmentSlot.Armor => "防具栏",
        EquipmentSlot.OffensiveHorse => "进攻坐骑栏",
        EquipmentSlot.DefensiveHorse => "防御坐骑栏",
        EquipmentSlot.Treasure => "宝物栏",
        _ => throw new InvalidOperationException($"Unknown equipment slot '{slot}'.")
    };

    private static string ZhihuGrantSourcePrefix => "acquired:zhihu:";

    internal bool CanRunYujueResolve(int ownerSeat)
    {
        if (_winner != Winner.None || !IsValidPlayerSeat(ownerSeat) || !_players[ownerSeat].IsAlive)
            return false;
        var owner = _players[ownerSeat];
        if (!YujueSlotOrder.Any(slot => owner.EquipmentSlotCapacity(slot) > 0))
            return false;
        return _players.Any(player => player.IsAlive && player.Seat != ownerSeat && GetHand(player).Count > 0);
    }

    // 鬻爵 resolve: the fresh invocation prompts the slot choice; the pending
    // state routes the resume through the target prompt and the card give.
    private SkillProgramStepOutcome YujueProgramResolve(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (active.YujuePending is { } pending)
        {
            if (pending.Stage == YujuePendingStage.GiftPaid)
            {
                CompleteYujueInvocation(active, pending, given: true, pending.TargetSeat, pending.GivenCardId);
                return SkillProgramStepOutcome.AwaitChild;
            }
            if (pending.TargetSeat is { } target)
                return PresentYujueGivePrompt(active, pending with { TargetSeat = target });
            PresentYujueTargetPrompt(active, pending);
            return SkillProgramStepOutcome.AwaitChoice;
        }
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            !CanRunYujueResolve(active.OwnerSeat))
            return SkillProgramStepOutcome.Continue;
        return PresentYujueSlotPrompt(active);
    }

    private SkillProgramStepOutcome PresentYujueSlotPrompt(ProgramSkillFrame active)
    {
        var owner = _players[active.OwnerSeat];
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var slots = YujueSlotOrder.Where(slot => owner.EquipmentSlotCapacity(slot) > 0).ToArray();
        var choices = slots.Select(slot => new PromptChoice(
                new ChoiceId($"yujue-slot.frame-{active.Id}.{slot}"),
                $"废除你的{YujueSlotDisplayName(slot)}。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "yujue-slot",
                    ["frame-id"] = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["slot"] = slot.ToString()
                }))
            .ToList<PromptChoice>();
        choices.Add(new PromptChoice(
            new ChoiceId($"yujue-decline.frame-{active.Id}"),
            "不发动【鬻爵】。",
            [], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "yujue-decline",
                ["frame-id"] = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】你可以废除一个装备栏，然后令一名有手牌的其他角色交给你一张手牌，其获得“执笏”直到你的下回合开始。",
            [], [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择要废除的装备栏", presentation.Description),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    // Prompt router: every 鬻爵 prompt rides one op, so the shared program
    // dispatch hands each accepted choice back through this single entry.
    private void ResolveYujueProgramChoice(PromptChoice selected)
    {
        switch (selected.Parameters.GetValueOrDefault("program-action"))
        {
            case "yujue-slot" or "yujue-decline": ResolveYujueSlotChoice(selected); return;
            case "yujue-target": ResolveYujueTargetChoice(selected); return;
            case "yujue-give": ResolveYujueGiveChoice(selected); return;
            default: throw new InvalidOperationException("The Yujue choice lost its action parameter.");
        }
    }

    private void ResolveYujueSlotChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Yujue choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.YujueResolve } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Yujue choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        ClearPendingDecision();
        if (selected.Parameters.GetValueOrDefault("program-action") == "yujue-decline")
        {
            var activation = _contentRegistry.GetSkill(active.SkillId).Program!.Activations
                .Single(entry => entry.Id == active.ActivationId);
            var key = (active.OwnerSeat, active.SkillId, activation.UsageGroup);
            if (_programUses.GetValueOrDefault(key) <= 0 || _programPhaseUses.GetValueOrDefault(key) <= 0)
                throw new InvalidOperationException("An unpaid Yujue cancellation lost its exact activation usage.");
            _programUses[key]--;
            _programPhaseUses[key]--;
            AdvanceRuntimeProgram(active.Id);
            return;
        }
        if (!System.Enum.TryParse<EquipmentSlot>(selected.Parameters.GetValueOrDefault("slot"), out var slot) ||
            !YujueSlotOrder.Contains(slot) ||
            _players[active.OwnerSeat].EquipmentSlotCapacity(slot) <= 0)
            throw new InvalidOperationException("The Yujue slot is no longer available.");
        if (!CanRunYujueResolve(active.OwnerSeat))
            throw new InvalidOperationException("The Yujue invocation lost its prerequisites.");
        var source = new CardConversionSource(active.SkillId, GetProgramBindingId(active), active.OwnerSeat, active.SkillInstanceId);
        var pending = new YujuePendingState(slot.ToString(), null, source, active.GameplayHash, active.InstructionIndex);
        AbolishYujueSlot(GetActiveProgramFrame(frame.Id), slot);
        ResolveTuxingAfterSlotAbolish(GetActiveProgramFrame(frame.Id));
        AdvanceEventRulesAndQueueFact(new ProgramYujueSlotPaidEvent(active.Id, source,
            active.GameplayHash, active.InstructionIndex, slot.ToString()));
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { YujuePending = pending, ReexecuteParticipantInstruction = true });
        AdvanceRuntimeProgram(active.Id);
    }

    // The slot payment: capacity zero plus the public capacity-changed event;
    // any equipped card moves to the discard pile inside SetEquipmentSlotCapacity.
    private void AbolishYujueSlot(ProgramSkillFrame frame, EquipmentSlot slot)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (frame.TriggerId is not null || _phase != TurnPhase.Play || _currentSeat != owner.Seat ||
            !owner.IsAlive || owner.EquipmentSlotCapacity(slot) <= 0)
            throw new InvalidOperationException("The Yujue slot payment lost its own Play activation.");
        SetEquipmentSlotCapacity(owner, slot, 0);
        AddLog("SkillTriggered",
            $"{owner.Name} 发动【{_contentRegistry!.GetSkill(active.SkillId).Name}】，废除自己的{YujueSlotDisplayName(slot)}。",
            active.OwnerSeat);
    }

    private void PresentYujueTargetPrompt(ProgramSkillFrame frame, YujuePendingState pending)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        var candidates = _players.Where(player => player.IsAlive && player.Seat != active.OwnerSeat &&
            GetHand(player).Count > 0).Select(player => player.Seat).OrderBy(seat => seat).ToArray();
        if (candidates.Length == 0)
        {
            // The recipient pool collapsed (only possible through a foreign
            // trigger inside the cards-moved window); the invocation ends and
            // the abolished slot stays spent.
            CompleteYujueInvocation(active, pending, given: false, targetSeat: null, givenCardId: null);
            return;
        }
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = candidates.Select(seat => new PromptChoice(
                new ChoiceId($"yujue-target.frame-{active.Id}.seat-{seat}"),
                $"令 {_players[seat].Name} 交给你一张手牌。",
                [], [seat],
                new Dictionary<string, string>
                {
                    ["program-action"] = "yujue-target",
                    ["frame-id"] = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["target-seat"] = seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }))
            .ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择一名有手牌的其他角色。",
            [], candidates, active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择交牌角色", presentation.Description),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveYujueTargetChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Yujue target choice lost its program frame.");
        var pending = frame.YujuePending ??
            throw new InvalidOperationException("The Yujue target choice lost its pending state.");
        if (!int.TryParse(selected.Parameters.GetValueOrDefault("target-seat"),
                System.Globalization.CultureInfo.InvariantCulture, out var targetSeat) ||
            !IsValidPlayerSeat(targetSeat) || targetSeat == frame.OwnerSeat ||
            !_players[targetSeat].IsAlive || GetHand(_players[targetSeat]).Count == 0)
            throw new InvalidOperationException("The Yujue recipient is no longer legal.");
        ClearPendingDecision();
        var active = GetActiveProgramFrame(frame.Id);
        var advanced = pending with { TargetSeat = targetSeat };
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { YujuePending = advanced });
        PresentYujueGivePrompt(GetActiveProgramFrame(frame.Id), advanced);
    }

    private SkillProgramStepOutcome PresentYujueGivePrompt(ProgramSkillFrame frame, YujuePendingState pending)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var targetSeat = pending.TargetSeat ??
            throw new InvalidOperationException("The Yujue give prompt lost its recipient.");
        var target = _players[targetSeat];
        if (!target.IsAlive || GetHand(target).Count == 0)
        {
            CompleteYujueInvocation(active, pending, given: false, targetSeat, null);
            return SkillProgramStepOutcome.AwaitChild;
        }
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var hand = GetHand(target).OrderBy(card => card.Id).ToArray();
        var choices = hand.Select(card => new PromptChoice(
                new ChoiceId($"yujue-give.frame-{active.Id}.card-{card.Id}"),
                $"交给 {_players[active.OwnerSeat].Name}【{card.DisplayName}】。",
                [card.Id], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "yujue-give",
                    ["frame-id"] = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["card-id"] = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }))
            .ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, targetSeat,
            $"【{presentation.Name}】请交给 {_players[active.OwnerSeat].Name} 一张手牌。",
            hand.Select(card => card.Id).ToArray(), [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            TargetSeat = targetSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 交牌",
                $"{_players[active.OwnerSeat].Name} 令你交给其一张手牌，你获得“执笏”直到其下回合开始。"),
            Choices = choices.AsReadOnly()
        };
        _status = target.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveYujueGiveChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Yujue give choice lost its program frame.");
        var pending = frame.YujuePending ??
            throw new InvalidOperationException("The Yujue give choice lost its pending state.");
        var targetSeat = pending.TargetSeat ??
            throw new InvalidOperationException("The Yujue give choice lost its recipient.");
        if (!int.TryParse(selected.Parameters.GetValueOrDefault("card-id"),
                System.Globalization.CultureInfo.InvariantCulture, out var cardId) ||
            _cardZones.GetLocation(cardId) != CardLocation.Hand(targetSeat))
            throw new InvalidOperationException("The Yujue given card is no longer in the recipient hand.");
        ClearPendingDecision();
        var active = GetActiveProgramFrame(frame.Id);
        MoveCard(_cardZones.CardsAt(CardLocation.Hand(targetSeat)).Single(card => card.Id == cardId),
            CardLocation.Hand(targetSeat), CardLocation.Hand(active.OwnerSeat),
            new($"skill-program.{active.SkillId}.yujue-give"));
        AddLog("CardGained",
            $"{_players[targetSeat].Name} 交给 {_players[active.OwnerSeat].Name} 一张手牌。",
            targetSeat, active.OwnerSeat);
        var movementSequence = _cardMovements.Last().Sequence;
        AdvanceEventRulesAndQueueFact(new ProgramYujueGiftPaidEvent(active.Id, pending.Source,
            active.GameplayHash, active.InstructionIndex, targetSeat, cardId, movementSequence));
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
        {
            YujuePending = pending with { Stage = YujuePendingStage.GiftPaid, GivenCardId = cardId,
                GiftMovementSequence = movementSequence },
            ReexecuteParticipantInstruction = true
        });
        AdvanceRuntimeProgram(active.Id);
    }

    // Finalize: grant 执笏 to the recipient until the owner's next turn starts,
    // queue the evidence and continue (or open the cards-moved window first).
    private void CompleteYujueInvocation(ProgramSkillFrame frame, YujuePendingState pending,
        bool given, int? targetSeat, int? givenCardId)
    {
        var active = GetActiveProgramFrame(frame.Id);
        AdvanceEventRulesAndQueueFact(new ProgramYujueInvokedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, pending.AbolishedSlot,
            targetSeat ?? -1, givenCardId ?? -1));
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { YujuePending = null, ReexecuteParticipantInstruction = false });
        if (given && targetSeat is { } seat)
        {
            AcquireRuntimeSkills(_players[seat], $"zhihu:{active.OwnerSeat}:{active.Id}", ["ol:zhihu"]);
            AddLog("SkillTriggered",
                $"{_players[seat].Name} 获得“执笏”，直到 {_players[active.OwnerSeat].Name} 的下回合开始。",
                active.OwnerSeat, seat);
        }
        AddLog("SkillTriggered",
            given
                ? $"{_players[active.OwnerSeat].Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】结算完成：废除{YujueSlotDisplayName(System.Enum.Parse<EquipmentSlot>(pending.AbolishedSlot))}，获得一张手牌。"
                : $"{_players[active.OwnerSeat].Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】结算完成：废除{YujueSlotDisplayName(System.Enum.Parse<EquipmentSlot>(pending.AbolishedSlot))}。",
            active.OwnerSeat, targetSeat);
        AdvanceRuntimeProgram(active.Id);
    }

    private void AssertYujuePending(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (frame.YujuePending is null && paused.Op != SkillProgramEffectOp.YujueResolve) return;
        var history = CompleteProgramEventHistory().ToArray();
        var payments = history.OfType<ProgramYujueSlotPaidEvent>().Where(fact => fact.FrameId == frame.Id).ToArray();
        var completed = history.OfType<ProgramYujueInvokedEvent>().Where(fact => fact.FrameId == frame.Id).ToArray();
        var gifts = history.OfType<ProgramYujueGiftPaidEvent>().Where(fact => fact.FrameId == frame.Id).ToArray();
        if (frame.YujuePending is not { } pending)
        {
            if (payments.Length != 0 && completed.Length != 1)
                throw new InvalidOperationException("A paid Yujue invocation lost its frame-owned continuation receipt.");
            return;
        }
        var source = new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
        if (paused.Op != SkillProgramEffectOp.YujueResolve || frame.TriggerId is not null ||
            frame.InstructionIndex != 1 || frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 ||
            pending.Source != source || pending.GameplayHash != frame.GameplayHash ||
            pending.InstructionIndex != frame.InstructionIndex ||
            !System.Enum.TryParse<EquipmentSlot>(pending.AbolishedSlot, out var slot) || !YujueSlotOrder.Contains(slot) ||
            pending.Stage is not (YujuePendingStage.SlotPaid or YujuePendingStage.GiftPaid) ||
            payments is not [var paid] || paid.Source != source || paid.GameplayHash != frame.GameplayHash ||
            paid.InstructionIndex != frame.InstructionIndex || paid.AbolishedSlot != pending.AbolishedSlot || completed.Length != 0 ||
            pending.TargetSeat is { } target && (!IsValidPlayerSeat(target) || target == frame.OwnerSeat))
            throw new InvalidOperationException("A Yujue receipt lost its exact activation, source instance or single paid slot.");
        if (pending.Stage == YujuePendingStage.SlotPaid)
        {
            if (pending.GivenCardId is not null || pending.GiftMovementSequence is not null || gifts.Length != 0)
                throw new InvalidOperationException("An unpaid Yujue gift contains a completed card invoice.");
        }
        else if (pending.TargetSeat is not { } giver || pending.GivenCardId is not { } card ||
                 pending.GiftMovementSequence is not { } sequence || gifts is not [var gift] ||
                 gift.Source != source || gift.GameplayHash != frame.GameplayHash ||
                 gift.InstructionIndex != frame.InstructionIndex || gift.TargetSeat != giver ||
                 gift.GivenCardId != card || gift.MovementSequence != sequence ||
                 _cardMovements.Count(move => move.Sequence == sequence && move.CardId == card && move.From == CardLocation.Hand(giver) &&
                     move.To == CardLocation.Hand(frame.OwnerSeat) &&
                     move.Reason.Value == $"skill-program.{frame.SkillId}.yujue-give") != 1)
            throw new InvalidOperationException("A paid Yujue gift lost its exact once-only hand movement.");
    }

    // 图兴: the locked companion resolves inside the abolish flow. Clause one
    // fires on every abolished slot; clause two fires once, when the last slot
    // goes (minus four maximum HP, then the game-long damage bonus arms).
    private void ResolveTuxingAfterSlotAbolish(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (!EnabledContentSkillIds(owner).Contains("ol:tuxing", StringComparer.Ordinal))
            return;
        var tuxingPresentation = _contentRegistry!.GetSkill("ol:tuxing");
        ChangeProgramMaximumHp(GetActiveProgramFrame(frame.Id), 1, "ol:tuxing");
        AddLog("SkillTriggered",
            $"{owner.Name} 的【{tuxingPresentation.Name}】生效：增加1点体力上限并回复1点体力。",
            active.OwnerSeat);
        new ProgramSkillHost(this).Recover(active.Id, active.OwnerSeat, active.OwnerSeat, 1, null, null);
        if (!IsTuxingDamageArmPending(active.OwnerSeat))
            return;
        AddLog("SkillTriggered",
            $"{owner.Name} 的【{tuxingPresentation.Name}】生效：所有装备栏均已废除，减少4点体力上限，本局游戏接下来造成的伤害+1。",
            active.OwnerSeat);
        ChangeProgramMaximumHp(GetActiveProgramFrame(frame.Id), -4, "ol:tuxing");
        ArmProgramGameDamageBonus(GetActiveProgramFrame(frame.Id), "ol:tuxing", active.OwnerSeat, 1);
    }

    // 图兴 catch-up: the locked turn-start sweep arms the game-long damage
    // bonus when every slot stands abolished and the bonus has not armed yet;
    // the abolish-time resolution already armed it in the ordinary flow, so
    // this only covers slots abolished outside that flow.
    private SkillProgramStepOutcome TuxingProgramArmGameDamage(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            !IsTuxingDamageArmPending(active.OwnerSeat))
            return SkillProgramStepOutcome.Continue;
        AddLog("SkillTriggered",
            $"{owner.Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】生效：所有装备栏均已废除，本局游戏接下来造成的伤害+1。",
            active.OwnerSeat);
        ArmProgramGameDamageBonus(active, active.SkillId, active.OwnerSeat, 1);
        return SkillProgramStepOutcome.Continue;
    }

    private bool IsTuxingDamageArmPending(int ownerSeat)
    {
        var owner = _players[ownerSeat];
        if (!YujueSlotOrder.All(slot => owner.EquipmentSlotCapacity(slot) == 0))
            return false;
        return !CompleteProgramEventHistory().OfType<ProgramGameDamageBonusArmedEvent>()
            .Any(fact => fact.OwnerSeat == ownerSeat);
    }

    // Shared arming: emits the committed evidence the damage pipeline reads.
    // The evidence attributes to the skill whose clause arms the bonus, so the
    // damage-time log names the right skill.
    private void ArmProgramGameDamageBonus(ProgramSkillFrame frame, string skillId, int ownerSeat, int amount)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var bindingId = skillId == active.SkillId ? GetProgramBindingId(active) :
            _contentRegistry.GetSkill(skillId).Program!.Triggers.Single(trigger =>
                trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.TuxingArmGameDamage)).Id;
        AdvanceEventRulesAndQueueFact(new ProgramGameDamageBonusArmedEvent(active.Id, skillId,
            bindingId, GetRuntimeSkillInstanceId(_players[ownerSeat], skillId), ownerSeat, amount));
    }

    // Shared consumption: every damage the armed seat deals carries the armed
    // amount, whatever the card kind and including chain propagation — the OL
    // text has no non-transferred-damage restriction.
    private IReadOnlyList<(CardUseEffectSource Source, int Amount)> GetArmedGameDamageBonuses(
        IDamageAttempt attack)
    {
        if (attack.IsSourceLess || !IsValidPlayerSeat(attack.SourceSeat) || !_players[attack.SourceSeat].IsAlive)
            return [];
        return CompleteProgramEventHistory().OfType<ProgramGameDamageBonusArmedEvent>()
            .Where(fact => fact.OwnerSeat == attack.SourceSeat)
            .Select(fact => (new CardUseEffectSource(fact.SkillId, fact.BindingId,
                fact.OwnerSeat, fact.SkillInstanceId), fact.Amount))
            .ToArray();
    }

    // 执笏 expiry: grants sourced "acquired:zhihu:{grantorSeat}:…" are removed
    // when the grantor's next turn begins; the grant lives through every other
    // character's turn in between.
    private SkillProgramStepOutcome ZhihuProgramExpire(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var holder = _players[active.OwnerSeat];
        if (_winner != Winner.None || !holder.IsAlive)
            return SkillProgramStepOutcome.Continue;
        foreach (var grant in holder.SkillGrants.Grants
                     .Where(grant => grant.IsEnabled &&
                         grant.SourceId.StartsWith(ZhihuGrantSourcePrefix, StringComparison.Ordinal))
                     .ToArray())
        {
            var segment = grant.SourceId[ZhihuGrantSourcePrefix.Length..];
            var separator = segment.IndexOf(':');
            if (separator > 0 &&
                int.TryParse(segment[..separator], System.Globalization.CultureInfo.InvariantCulture, out var grantor) &&
                grantor == _currentSeat)
            {
                holder.SkillGrants.RemoveGrant(grant.GrantId);
                AddLog("SkillEffect",
                    $"{holder.Name} 的“执笏”随 {_players[grantor].Name} 的回合开始而失效。",
                    holder.Seat);
            }
        }
        return SkillProgramStepOutcome.Continue;
    }

    // 鬻爵 slot prompt: keep combat-relevant slots; prefer an empty low-value
    // slot, otherwise sacrifice the lowest-value equipped slot.
    private PromptChoice SelectAiYujueSlotChoice(PendingDecision decision)
    {
        var owner = _players[decision.PlayerSeat];
        var ranked = YujueAiAbolishPreference
            .Where(slot => owner.EquipmentSlotCapacity(slot) > 0)
            .Select((slot, index) => (Slot: slot, Index: index,
                Equipped: GetEquipment(owner).Any(card => EquipmentCatalog.Get(card.Kind).Slot == slot)))
            .OrderBy(entry => entry.Equipped ? 1 : 0)
            .ThenBy(entry => entry.Index)
            .First();
        return decision.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("slot") == ranked.Slot.ToString());
    }

    // 鬻爵 recipient: score every candidate through the shared target scoring;
    // the recipient both pays one card and gains the damage-draw engine, so the
    // benefit hint mirrors a two-card draw to the target.
    private PromptChoice SelectAiYujueTargetChoice(PendingDecision decision)
    {
        var view = CreateSnapshot(decision.PlayerSeat);
        var brain = _aiBrains[decision.PlayerSeat];
        return decision.Choices
            .Select(choice =>
            {
                var targetSeat = int.Parse(choice.Parameters.GetValueOrDefault("target-seat", "-1"),
                    System.Globalization.CultureInfo.InvariantCulture);
                var hint = new SkillProgramAiHint(0, 0, 0, 2, 0, 0, false, false);
                return (Choice: choice, Score: brain.ScoreProgramTarget(view, targetSeat, hint));
            })
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Choice.Id.Value, StringComparer.Ordinal)
            .First().Choice;
    }

    // 执笏 give: the recipient surrenders its least valuable hand entity in
    // stable id order.
    private PromptChoice SelectAiYujueGiveChoice(PendingDecision decision) =>
        decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();

    private PromptChoice SelectAiYujueChoice(PendingDecision decision)
    {
        var first = decision.Choices[0].Parameters.GetValueOrDefault("program-action");
        if (first == "yujue-give") return SelectAiYujueGiveChoice(decision);
        if (first == "yujue-target") return SelectAiYujueTargetChoice(decision);
        if (first == "yujue-slot") return SelectAiYujueSlotChoice(decision);
        // Decline only survives when every slot choice is unwanted: the AI
        // always spends an empty slot first, so the decline is a fallback.
        return decision.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "yujue-decline");
    }

    private sealed partial class ProgramSkillHost : ILiuHongProgramHost
    {
        public SkillProgramStepOutcome YujueResolve(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.YujueProgramResolve(frame, effect);
        public SkillProgramStepOutcome ZhihuExpire(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZhihuProgramExpire(frame, effect);
        public SkillProgramStepOutcome TuxingArmGameDamage(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.TuxingProgramArmGameDamage(frame, effect);
    }
}
