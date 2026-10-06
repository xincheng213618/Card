namespace CardGame.Core;

public sealed record ProgramBijingMarkedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, IReadOnlyList<int> CardIds) : IGameEvent;
public sealed record ProgramBijingRecastEvent(long FrameId, string SkillId,
    int OwnerSeat, IReadOnlyList<int> DiscardedCardIds, int DrawnCount) : IGameEvent;
public sealed record ProgramBijingPunishEvent(long FrameId, string SkillId,
    int OwnerSeat, int TurnOwnerSeat, IReadOnlyList<int> LostCardIds, IReadOnlyList<int> DiscardedCardIds) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public BijingPunishState? BijingPunish { get; init; }
}

// One prompt per discarded card; the receipt rides the owning program frame.
public sealed record BijingPunishState(int TurnOwnerSeat, int OwnerSeat,
    IReadOnlyList<int> LostCardIds, int Remaining, IReadOnlyList<int> DiscardedCardIds);

public sealed partial class GameEngine
{
    // 闭境 marks survive across turns until the owner's next preparation phase
    // recasts them; replay reconstructs the map from the accepted commands.
    private readonly Dictionary<int, int> _bijingMarkedCardIds = [];

    // 图南 branch prompt: the selected counterpart sees only the legal ways to
    // use the revealed card — as itself (slash/peach/equipment) or as a Slash.
    private SkillProgramStepOutcome TunanProgramUseRevealedCard(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SelectedTargetSeats is not [var user] || user == active.OwnerSeat)
            throw new InvalidOperationException("Tunan lost its selected counterpart.");
        var binding = active.CardSetBindings.SingleOrDefault(item => item.Name == effect.SourceBind!);
        var card = ReadTunanRevealedCard(binding);
        if (!_players[user].IsAlive || card is null)
        {
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id),
                card is null ? "图南亮出的牌已离开处理区，剩余结算取消。" : "图南的用牌角色已失效，剩余结算取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var canUse = card.Kind is CardKind.Slash or CardKind.Peach ||
            EquipmentCatalog.IsEquipment(card.Kind);
        var slashVictims = _players.Where(player => player.IsAlive && player.Seat != user &&
            GetSeatDistance(user, player.Seat) <= GetAttackRange(user)).Select(player => player.Seat).ToArray();
        if (!canUse && slashVictims.Length == 0)
        {
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id), "图南没有可行的使用方式，剩余结算取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var presentation = _contentRegistry!.GetSkill(frame.SkillId);
        var choices = new List<PromptChoice>();
        if (canUse)
            choices.Add(new PromptChoice(
                new ChoiceId($"tunan-branch.frame-{frame.Id}.use"),
                $"使用【{card.DisplayName}】（无距离限制）。",
                [card.Id], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "tunan-branch",
                    ["branch"] = "use",
                    ["source-bind"] = effect.SourceBind!
                }));
        if (slashVictims.Length > 0)
            choices.Add(new PromptChoice(
                new ChoiceId($"tunan-branch.frame-{frame.Id}.slash"),
                $"将【{card.DisplayName}】当【杀】使用（正常距离）。",
                [card.Id], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "tunan-branch",
                    ["branch"] = "slash",
                    ["source-bind"] = effect.SourceBind!
                }));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, user,
            $"【{presentation.Name}】请选择使用亮出之牌的方式。",
            [card.Id], [], frame.WindowContext?.SourceSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = user,
            SkillPrompt = new SkillPromptPresentation(frame.SkillId, presentation.Name,
                $"{presentation.Name} · 选择使用方式", "将亮出的牌按所选方式使用。"),
            Choices = choices.AsReadOnly()
        };
        _status = _players[user].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveTunanBranchChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Tunan branch choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.TunanUseRevealedCard } effect ||
            selected.Parameters.GetValueOrDefault("source-bind") != effect.SourceBind)
            throw new InvalidOperationException("The Tunan branch choice does not match the suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SelectedTargetSeats is not [var user])
            throw new InvalidOperationException("Tunan lost its selected counterpart.");
        var card = ReadTunanRevealedCard(active.CardSetBindings
            .SingleOrDefault(item => item.Name == effect.SourceBind!)) ??
            throw new InvalidOperationException("The Tunan revealed card left the processing zone.");
        ClearPendingDecision();
        var branch = selected.Parameters.GetValueOrDefault("branch");
        if (branch == "use" && card.Kind == CardKind.Peach)
        {
            ExecuteTunanPeachUse(frame, card, user);
            return;
        }
        if (branch == "use" && EquipmentCatalog.IsEquipment(card.Kind))
        {
            ExecuteTunanEquipmentUse(frame, card, user);
            return;
        }
        // Both slash shapes pick their victim in a follow-up prompt; the real
        // use ignores distance, the converted Slash keeps the normal distance.
        var unlimited = branch == "use";
        var victims = _players.Where(player => player.IsAlive && player.Seat != user &&
            (unlimited || GetSeatDistance(user, player.Seat) <= GetAttackRange(user)))
            .Select(player => player.Seat).ToArray();
        if (victims.Length == 0)
        {
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id), "图南的杀没有合法目标，剩余结算取消。");
            return;
        }
        var presentation = _contentRegistry.GetSkill(frame.SkillId);
        var choices = victims.Select(victim => new PromptChoice(
            new ChoiceId($"tunan-target.frame-{frame.Id}.seat-{victim}"),
            $"对 {_players[victim].Name} 使用【杀】。",
            [], [victim],
            new Dictionary<string, string>
            {
                ["program-action"] = "tunan-target",
                ["branch"] = branch ?? string.Empty,
                ["source-bind"] = effect.SourceBind!,
                ["victim"] = victim.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, user,
            $"【{presentation.Name}】请选择【杀】的目标。",
            [], victims, frame.WindowContext?.SourceSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = user,
            SkillPrompt = new SkillPromptPresentation(frame.SkillId, presentation.Name,
                $"{presentation.Name} · 选择目标", unlimited ? "此【杀】无距离限制。" : "此【杀】按正常距离规则。"),
            Choices = choices.AsReadOnly()
        };
        _status = _players[user].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveTunanTargetChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Tunan target choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.TunanUseRevealedCard } effect ||
            selected.Parameters.GetValueOrDefault("source-bind") != effect.SourceBind)
            throw new InvalidOperationException("The Tunan target choice does not match the suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SelectedTargetSeats is not [var user] ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("victim"),
                System.Globalization.CultureInfo.InvariantCulture, out var victim))
            throw new InvalidOperationException("Tunan lost its selected counterpart or victim.");
        var card = ReadTunanRevealedCard(active.CardSetBindings
            .SingleOrDefault(item => item.Name == effect.SourceBind!)) ??
            throw new InvalidOperationException("The Tunan revealed card left the processing zone.");
        ClearPendingDecision();
        var asSlash = selected.Parameters.GetValueOrDefault("branch") == "slash";
        var resolutionId = BeginCardUse(card, user, [victim],
            playedCardKind: asSlash ? CardKind.Slash : null,
            conversionSource: asSlash
                ? new CardConversionSource(active.SkillId, GetProgramBindingId(active), user, active.SkillInstanceId)
                : null);
        SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);
        var action = _resolutionStack.OfType<CardUseFrame>().Last(use => use.Id == resolutionId).Action ??
            throw new InvalidOperationException("The Tunan slash lost its action context.");
        var attack = new CardAttackHandle(this, resolutionId, user, victim,
            card: asSlash ? null : card, damageAmount: 1, playedCardKind: CardKind.Slash,
            physicalCards: asSlash ? null : [card],
            programSkillCardUseFrameId: frame.Id);
        ActiveCardAttack = attack;
        AddLog("CardUsed", $"{_players[user].Name} 对 {_players[victim].Name} 使用【杀】。", user, victim);
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(asSlash ? 0 : card.Id, CardKind.Slash, user, victim));
        if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted,
                [victim], ProgramCardContinuation.CommittedSlash))
            BeginSlashTargetResolution(attack);
    }

    private void ExecuteTunanPeachUse(ProgramSkillFrame frame, Card card, int user)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var resolutionId = BeginCardUse(card, user, [user]);
        SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);
        BeginSimpleCardUse(resolutionId, new(card.Id, SimpleCardUseEffect.Recovery, 1, []));
    }

    private void ExecuteTunanEquipmentUse(ProgramSkillFrame frame, Card card, int user)
    {
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
        {
            PendingMovementContinuation = new ProgramMovementContinuation(frame.OwnerSeat, 0, null)
        });
        var resolutionId = BeginCardUse(card, user, []);
        if (!TryBeginEquipmentTargetPrograms(_players[user], card, resolutionId))
            CompleteEquipmentUse(_players[user], card, resolutionId);
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame)
        {
            if (!TryBeginCardsMovedProgramWindow())
                ReturnRuntimeProgramMovement(frame.Id);
        }
    }

    private Card? ReadTunanRevealedCard(ProgramSkillCardSetBinding? binding)
    {
        if (binding is null || binding.CardIds.Count != 1) return null;
        var cardId = binding.CardIds[0];
        return _cardZones.CardsAt(CardLocation.Processing).SingleOrDefault(card => card.Id == cardId);
    }

    private PromptChoice SelectAiTunanChoice(PendingDecision decision)
    {
        if (decision.Choices[0].Parameters.GetValueOrDefault("program-action") == "tunan-branch")
            return decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("branch") == "use") ??
                decision.Choices[0];
        return decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();
    }

    // 闭境 mark: the chosen hand cards stay registered until their recast.
    private SkillProgramStepOutcome BijingProgramMarkHandCards(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var binding = active.CardSetBindings.SingleOrDefault(item => item.Name == effect.SourceBind!);
        if (binding is null)
        {
            CancelProgramBindingAndCleanup(active, "闭境的选牌绑定未生成，标记结束。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        foreach (var cardId in binding.CardIds)
            if (_cardZones.GetLocation(cardId) == CardLocation.Hand(active.OwnerSeat))
                _bijingMarkedCardIds[cardId] = active.OwnerSeat;
        if (binding.CardIds.Count > 0)
            AdvanceEventRulesAndQueueFact(new ProgramBijingMarkedEvent(active.Id, active.SkillId,
                GetProgramBindingId(active), active.OwnerSeat, binding.CardIds));
        AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.Continue;
    }

    // 闭境 recast: marked hand cards are discarded and replaced one-for-one.
    private SkillProgramStepOutcome BijingProgramRecastMarkedCards(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive) return SkillProgramStepOutcome.Continue;
        var marked = GetHand(owner).Where(card => _bijingMarkedCardIds.TryGetValue(card.Id, out var seat) &&
            seat == active.OwnerSeat).ToArray();
        foreach (var card in marked)
            _bijingMarkedCardIds.Remove(card.Id);
        if (marked.Length == 0) return SkillProgramStepOutcome.Continue;
        var reason = new CardMoveReason($"skill-program.{active.SkillId}.bijing-recast");
        MoveCards(marked, CardLocation.Hand(active.OwnerSeat), CardLocation.DiscardPile, reason);
        DrawCards(owner, marked.Length, true, new($"skill-program.{active.SkillId}.bijing-recast-draw"));
        AdvanceEventRulesAndQueueFact(new ProgramBijingRecastEvent(active.Id, active.SkillId,
            active.OwnerSeat, Array.AsReadOnly(marked.Select(card => card.Id).ToArray()), marked.Length));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    // 闭境 punish: the turn owner discards two when the owner lost a marked
    // card during this turn; the movement log is the per-turn loss record.
    private SkillProgramStepOutcome BijingProgramPunishDiscardPhase(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var ownerSeat = active.OwnerSeat;
        if (frame.WindowContext is not { SourceSeat: { } turnOwner } ||
            turnOwner == ownerSeat || !_players[turnOwner].IsAlive || !_players[ownerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[ownerSeat], active.SkillId, active.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var lost = _cardMovements.Where(movement => movement.TurnNumber == _turnNumber &&
                movement.From == CardLocation.Hand(ownerSeat) &&
                _bijingMarkedCardIds.TryGetValue(movement.CardId, out var seat) && seat == ownerSeat)
            .Select(movement => movement.CardId).Distinct().ToArray();
        if (lost.Length == 0) return SkillProgramStepOutcome.Continue;
        var turnOwnerHand = GetHand(_players[turnOwner]);
        if (turnOwnerHand.Count == 0)
        {
            AdvanceEventRulesAndQueueFact(new ProgramBijingPunishEvent(active.Id, active.SkillId,
                ownerSeat, turnOwner, lost, []));
            return SkillProgramStepOutcome.Continue;
        }
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with
        {
            BijingPunish = new BijingPunishState(turnOwner, ownerSeat, lost,
                Math.Min(2, turnOwnerHand.Count), [])
        });
        PresentBijingPunishCardPrompt(GetActiveProgramFrame(active.Id));
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PresentBijingPunishCardPrompt(ProgramSkillFrame frame)
    {
        var state = frame.BijingPunish ?? throw new InvalidOperationException("The Bijing punish lost its state.");
        var active = GetActiveProgramFrame(frame.Id);
        var hand = GetHand(_players[state.TurnOwnerSeat]).OrderBy(card => card.Id).ToArray();
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = hand.Select(card => new PromptChoice(
            new ChoiceId($"bijing-punish.frame-{frame.Id}.card-{card.Id}"),
            $"弃置【{card.DisplayName}】。",
            [card.Id], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "bijing-punish-discard",
                ["card-id"] = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, state.TurnOwnerSeat,
            $"【{presentation.Name}】请弃置 {state.Remaining} 张手牌。",
            hand.Select(card => card.Id).ToArray(), [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            TargetSeat = state.TurnOwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 弃牌",
                $"{_players[state.OwnerSeat].Name} 本回合失去过“闭境”牌，你弃置 {state.Remaining} 张手牌。"),
            Choices = choices.AsReadOnly()
        };
        _status = _players[state.TurnOwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveBijingPunishChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Bijing punish choice lost its program frame.");
        var state = frame.BijingPunish ?? throw new InvalidOperationException("The Bijing punish lost its state.");
        if (!int.TryParse(selected.Parameters.GetValueOrDefault("card-id"),
                System.Globalization.CultureInfo.InvariantCulture, out var cardId) ||
            _cardZones.GetLocation(cardId) != CardLocation.Hand(state.TurnOwnerSeat))
            throw new InvalidOperationException("The Bijing punish card is no longer in hand.");
        ClearPendingDecision();
        var active = GetActiveProgramFrame(frame.Id);
        MoveCards([_cardZones.CardsAt(CardLocation.Hand(state.TurnOwnerSeat)).Single(card => card.Id == cardId)],
            CardLocation.Hand(state.TurnOwnerSeat), CardLocation.DiscardPile,
            new($"skill-program.{active.SkillId}.bijing-punish"));
        var discarded = state.DiscardedCardIds.Append(cardId).ToArray();
        if (state.Remaining > 1)
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
            {
                BijingPunish = state with
                {
                    Remaining = state.Remaining - 1,
                    DiscardedCardIds = Array.AsReadOnly(discarded)
                }
            });
            PresentBijingPunishCardPrompt(GetActiveProgramFrame(frame.Id));
            return;
        }
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { BijingPunish = null });
        AdvanceEventRulesAndQueueFact(new ProgramBijingPunishEvent(active.Id, active.SkillId,
            state.OwnerSeat, state.TurnOwnerSeat, state.LostCardIds, discarded));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }

    private sealed partial class ProgramSkillHost : ILvKaiProgramHost
    {
        public SkillProgramStepOutcome TunanUseRevealedCard(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.TunanProgramUseRevealedCard(frame, effect);
        public SkillProgramStepOutcome BijingMarkHandCards(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.BijingProgramMarkHandCards(frame, effect);
        public SkillProgramStepOutcome BijingRecastMarkedCards(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.BijingProgramRecastMarkedCards(frame, effect);
        public SkillProgramStepOutcome BijingPunishDiscardPhase(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.BijingProgramPunishDiscardPhase(frame, effect);
    }
}
