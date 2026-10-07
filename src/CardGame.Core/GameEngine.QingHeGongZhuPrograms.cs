namespace CardGame.Core;

internal interface IQingHeGongZhuProgramHost
{
    SkillProgramStepOutcome ChangjiEndingDamageChoice(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZengouNullifyDodge(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 长姬 evidence: the committed ending-phase choice is public; the draw count,
// the discarded cards and the damage evidence ride one scalar record.
public sealed record ProgramChangjiEndingEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int EndingSeat, string Branch, IReadOnlyList<int> DiscardedCardIds, int DrawCount) : IGameEvent;

// 谮构 evidence: the paid cost, the nullified dodge and the gained entity are
// public once they move; one record carries the whole activation.
public sealed record ProgramZengouNullifiedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int DodgerSeat, string Cost, int? PaidCardId, int? GainedCardId) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ChangjiDiscardState? ChangjiDiscard { get; init; }
}

// One prompt per discarded card; the receipt rides the owning program frame.
public sealed record ChangjiDiscardState(int EndingSeat, int OwnerSeat, int Remaining,
    IReadOnlyList<int> DiscardedCardIds);

public sealed partial class GameEngine
{
    // 长姬: at any character's ending phase, if the owner dealt damage this turn
    // she may have that character draw two cards, and if she took damage this
    // turn she may have that character discard two cards.
    private SkillProgramStepOutcome ChangjiProgramEndingDamageChoice(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding, TargetSeat: { } endingSeat } ||
            !_players[endingSeat].IsAlive)
            return SkillProgramStepOutcome.Continue;
        var damages = EventsSinceLastBoundary(item => item is TurnStartedEvent).OfType<DamageAppliedEvent>()
            .Where(item => item.Amount > 0).ToArray();
        var dealt = damages.Any(item => !item.SourceLess && item.SourceSeat == owner.Seat);
        var taken = damages.Any(item => item.TargetSeat == owner.Seat);
        if (!dealt && !taken) return SkillProgramStepOutcome.Continue;
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = new List<PromptChoice>();
        if (dealt)
            choices.Add(new PromptChoice(
                new ChoiceId($"changji-ending.frame-{frame.Id}.draw"),
                $"令 {_players[endingSeat].Name} 摸两张牌。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "changji-ending-choice",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["branch"] = "draw"
                }));
        if (taken)
            choices.Add(new PromptChoice(
                new ChoiceId($"changji-ending.frame-{frame.Id}.discard"),
                $"令 {_players[endingSeat].Name} 弃置两张牌。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "changji-ending-choice",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["branch"] = "discard"
                }));
        choices.Add(new PromptChoice(
            new ChoiceId($"changji-ending.frame-{frame.Id}.skip"), "不发动【长姬】。", [], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "changji-ending-choice",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["branch"] = "skip"
            }));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择是否发动。",
            [], [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = endingSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 结束阶段",
                dealt && taken ? "你本回合造成过伤害，也受到过伤害，请选择一项。" :
                dealt ? "你本回合造成过伤害。" : "你本回合受到过伤害。"),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveChangjiEndingChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Changji ending choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.ChangjiEndingDamageChoice } ||
            selected.Parameters.GetValueOrDefault("program-action") != "changji-ending-choice" ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Changji ending choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Changji ending chooser changed while suspended.");
        ClearPendingDecision();
        var endingSeat = active.WindowContext!.TargetSeat!.Value;
        var branch = selected.Parameters.GetValueOrDefault("branch");
        if (branch == "draw")
        {
            DrawCards(_players[endingSeat], 2, true, new($"skill-program.{active.SkillId}.changji-draw"));
            AddLog("SkillEffect",
                $"{_players[active.OwnerSeat].Name} 发动【长姬】：令 {_players[endingSeat].Name} 摸两张牌。",
                active.OwnerSeat, endingSeat);
            AdvanceEventRulesAndQueueFact(new ProgramChangjiEndingEvent(active.Id, active.SkillId,
                GetProgramBindingId(active), active.OwnerSeat, endingSeat, "draw", [], 2));
            if (!TryBeginCardsMovedProgramWindow())
                AdvanceRuntimeProgram(active.Id);
            return;
        }
        if (branch == "discard")
        {
            var hand = GetHand(_players[endingSeat]);
            if (hand.Count == 0)
            {
                AddLog("SkillEffect", $"{_players[endingSeat].Name} 没有手牌，长姬弃牌落空。", active.OwnerSeat, endingSeat);
                AdvanceRuntimeProgram(active.Id);
                return;
            }
            ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with
            {
                ChangjiDiscard = new ChangjiDiscardState(endingSeat, active.OwnerSeat,
                    Math.Min(2, hand.Count), [])
            });
            PresentChangjiDiscardPrompt(GetActiveProgramFrame(active.Id));
            return;
        }
        AdvanceRuntimeProgram(active.Id);
    }

    private void PresentChangjiDiscardPrompt(ProgramSkillFrame frame)
    {
        var state = frame.ChangjiDiscard ?? throw new InvalidOperationException("The Changji discard lost its state.");
        var active = GetActiveProgramFrame(frame.Id);
        var hand = GetHand(_players[state.EndingSeat]).OrderBy(card => card.Id).ToArray();
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = hand.Select(card => new PromptChoice(
            new ChoiceId($"changji-discard.frame-{frame.Id}.card-{card.Id}"),
            $"弃置【{card.DisplayName}】。",
            [card.Id], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "changji-discard",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["card-id"] = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, state.EndingSeat,
            $"【{presentation.Name}】请弃置 {state.Remaining} 张手牌。",
            hand.Select(card => card.Id).ToArray(), [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            TargetSeat = state.EndingSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 弃牌",
                $"{_players[state.OwnerSeat].Name} 本回合受到过伤害，你弃置 {state.Remaining} 张手牌。"),
            Choices = choices.AsReadOnly()
        };
        _status = _players[state.EndingSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveChangjiDiscardChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Changji discard choice lost its program frame.");
        var state = frame.ChangjiDiscard ?? throw new InvalidOperationException("The Changji discard lost its state.");
        if (!int.TryParse(selected.Parameters.GetValueOrDefault("card-id"),
                System.Globalization.CultureInfo.InvariantCulture, out var cardId) ||
            _cardZones.GetLocation(cardId) != CardLocation.Hand(state.EndingSeat))
            throw new InvalidOperationException("The Changji discard card is no longer in hand.");
        ClearPendingDecision();
        var active = GetActiveProgramFrame(frame.Id);
        MoveCards([_cardZones.CardsAt(CardLocation.Hand(state.EndingSeat)).Single(card => card.Id == cardId)],
            CardLocation.Hand(state.EndingSeat), CardLocation.DiscardPile,
            new($"skill-program.{active.SkillId}.changji-discard"));
        var discarded = state.DiscardedCardIds.Append(cardId).ToArray();
        if (state.Remaining > 1)
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
            {
                ChangjiDiscard = state with
                {
                    Remaining = state.Remaining - 1,
                    DiscardedCardIds = Array.AsReadOnly(discarded)
                }
            });
            PresentChangjiDiscardPrompt(GetActiveProgramFrame(frame.Id));
            return;
        }
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { ChangjiDiscard = null });
        AddLog("SkillEffect",
            $"{_players[active.OwnerSeat].Name} 发动【长姬】：{_players[state.EndingSeat].Name} 弃置 {discarded.Length} 张手牌。",
            active.OwnerSeat, state.EndingSeat);
        AdvanceEventRulesAndQueueFact(new ProgramChangjiEndingEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, state.EndingSeat, "discard",
            Array.AsReadOnly(discarded), 0));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }

    // 谮构: when a character within the owner's attack range uses a Dodge that
    // fully resolved against a single-target attack, the owner may discard one
    // non-basic card or lose 1 HP to nullify that Dodge and gain its entity.
    private SkillProgramStepOutcome ZengouProgramNullifyDodge(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            frame.WindowContext is not { CardUse: { } cardUse, TargetSeat: { } dodgerSeat } ||
            dodgerSeat == active.OwnerSeat || !_players[dodgerSeat].IsAlive ||
            !IsWithinAttackRange(active.OwnerSeat, dodgerSeat))
            return SkillProgramStepOutcome.Continue;
        var attack = _resolutionStack.OfType<CardUseFrame>()
            .SingleOrDefault(item => item.Id == cardUse.ParentCardUseFrameId);
        if (attack is null || attack.TargetSeats.Count != 1 ||
            (attack.Enhancements & CurrentCardEnhancement.Uncancelable) != 0)
            return SkillProgramStepOutcome.Continue;
        var dodge = LatestSettledDodgeEntity();
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = new List<PromptChoice>();
        foreach (var card in NonBasicOwnedCards(active.OwnerSeat))
        {
            if (dodge is null) break;
            choices.Add(new PromptChoice(
                new ChoiceId($"zengou-cost.frame-{frame.Id}.card-{card.Id}"),
                $"弃置【{card.DisplayName}】。",
                [card.Id], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "zengou-cost",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["cost"] = "discard",
                    ["card-id"] = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
        }
        if (owner.Hp > 0)
            choices.Add(new PromptChoice(
                new ChoiceId($"zengou-cost.frame-{frame.Id}.lose-hp"),
                "失去1点体力。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "zengou-cost",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["cost"] = "loseHp"
                }));
        if (choices.Count == 0) return SkillProgramStepOutcome.Continue;
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择支付方式，令此【闪】无效并获得之。",
            [], [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = dodgerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 令【闪】无效",
                $"{_players[dodgerSeat].Name} 使用的【闪】结算完成，你可以支付代价令其无效并获得之。"),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveZengouCostChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Zengou cost choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.ZengouNullifyDodge } ||
            selected.Parameters.GetValueOrDefault("program-action") != "zengou-cost" ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Zengou cost choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Zengou cost chooser changed while suspended.");
        ClearPendingDecision();
        if (active.WindowContext is not { CardUse: { } cardUse, TargetSeat: { } dodgerSeat } ||
            _winner != Winner.None || !_players[active.OwnerSeat].IsAlive ||
            !_players[dodgerSeat].IsAlive || !IsWithinAttackRange(active.OwnerSeat, dodgerSeat))
        {
            CancelProgramBindingAndCleanup(active, "谮构的目标已失效，剩余结算取消。");
            return;
        }
        var attack = _resolutionStack.OfType<CardUseFrame>()
            .SingleOrDefault(item => item.Id == cardUse.ParentCardUseFrameId);
        var dodge = LatestSettledDodgeEntity();
        if (attack is null || attack.TargetSeats.Count != 1 ||
            (attack.Enhancements & CurrentCardEnhancement.Uncancelable) != 0 || dodge is null)
        {
            CancelProgramBindingAndCleanup(active, "谮构的【闪】结算已变化，剩余结算取消。");
            return;
        }
        var owner = _players[active.OwnerSeat];
        var cost = selected.Parameters.GetValueOrDefault("cost");
        int? paidCardId = null;
        if (cost == "discard")
        {
            if (!int.TryParse(selected.Parameters.GetValueOrDefault("card-id"),
                    System.Globalization.CultureInfo.InvariantCulture, out var parsedCardId) ||
                NonBasicOwnedCards(active.OwnerSeat).All(card => card.Id != parsedCardId))
                throw new InvalidOperationException("The Zengou cost card is no longer available.");
            paidCardId = parsedCardId;
            var paidCard = NonBasicOwnedCards(active.OwnerSeat).Single(card => card.Id == parsedCardId);
            MoveCards([paidCard], _cardZones.GetLocation(paidCard.Id), CardLocation.DiscardPile,
                new($"skill-program.{active.SkillId}.zengou-cost"));
        }
        else if (cost == "loseHp")
        {
            var before = owner.Hp;
            owner.Hp = Math.Max(0, before - 1);
            RecordHpChange(active.Id, null, active.OwnerSeat, before, owner.Hp, HpChangeKind.Loss);
            AdvanceEventRulesAndQueueFact(new ProgramSkillHpLostEvent(active.Id, active.SkillId,
                active.OwnerSeat, before - owner.Hp, owner.Hp));
        }
        else
        {
            throw new InvalidOperationException($"The Zengou cost '{cost}' is unsupported.");
        }
        ReplaceRuntimeFrame(attack.Id, attack with
        {
            Enhancements = attack.Enhancements | CurrentCardEnhancement.Uncancelable,
            EnhancementOwnerSeat = active.OwnerSeat
        });
        MoveCard(dodge, CardLocation.DiscardPile, CardLocation.Hand(active.OwnerSeat),
            new($"skill-program.{active.SkillId}.zengou-gain"));
        AddLog("SkillTriggered",
            $"{owner.Name} 发动【{_contentRegistry.GetSkill(active.SkillId).Name}】：" +
            (cost == "discard" ? $"弃置一张非基本牌" : "失去1点体力") +
            $"，令 {_players[dodgerSeat].Name} 使用的【闪】无效，并获得之。",
            active.OwnerSeat, dodgerSeat);
        AdvanceEventRulesAndQueueFact(new ProgramZengouNullifiedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, dodgerSeat, cost, paidCardId, dodge.Id));
        if (owner.Hp == 0)
        {
            BeginProgramSkillDying(active.Id, owner);
            return;
        }
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }

    private Card? LatestSettledDodgeEntity()
    {
        foreach (var movement in _cardMovements.AsEnumerable().Reverse())
        {
            if (movement.To != CardLocation.DiscardPile ||
                movement.Reason.Value != CardMoveReasons.ResponseFinished.Value)
                continue;
            var card = _cardZones.CardsAt(CardLocation.DiscardPile)
                .FirstOrDefault(item => item.Id == movement.CardId);
            if (card is null) continue;
            return GetAdvancedCard(card.Id).Kind == CardKind.Dodge ? card : null;
        }
        return null;
    }

    private IReadOnlyList<Card> NonBasicOwnedCards(int ownerSeat) =>
        _cardZones.CardsAt(CardLocation.Hand(ownerSeat))
            .Concat(_cardZones.CardsAt(CardLocation.Equipment(ownerSeat)))
            .Where(card => CardCatalog.Get(card.Kind).CategoryName != "基本牌")
            .ToArray();

    private PromptChoice SelectAiChangjiEndingChoice(PendingDecision decision)
    {
        if (decision.Choices[0].Parameters.GetValueOrDefault("program-action") == "changji-discard")
            return decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();
        var draw = decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("branch") == "draw");
        var discard = decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("branch") == "discard");
        var skip = decision.Choices.First(choice => choice.Parameters.GetValueOrDefault("branch") == "skip");
        return decision.TargetSeat == decision.PlayerSeat ? draw ?? skip : discard ?? skip;
    }

    private PromptChoice SelectAiZengouCostChoice(PendingDecision decision)
    {
        var discard = decision.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("cost") == "discard");
        return discard ?? decision.Choices.First();
    }

    private sealed partial class ProgramSkillHost : IQingHeGongZhuProgramHost
    {
        public SkillProgramStepOutcome ChangjiEndingDamageChoice(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ChangjiProgramEndingDamageChoice(frame, effect);
        public SkillProgramStepOutcome ZengouNullifyDodge(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZengouProgramNullifyDodge(frame, effect);
    }
}
