namespace CardGame.Core;

// 狷狭 evidence: one scalar record per virtually used trick, the committed debt
// that promises the retaliation, and the retaliation settlement that closes each
// launch. The outstanding debt is derived from committed events only, so cold
// recovery replays the accepted commands into the same state.
public sealed record ProgramJuanxiaTrickUsedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TargetSeat, string TrickKind, int CardId, bool Paid) : IGameEvent;
public sealed record ProgramJuanxiaDebtEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TargetSeat, int TrickCount) : IGameEvent;
public sealed record ProgramJuanxiaRetaliationEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int DebtorSeat, long LaunchFrameId, int OfferedCount, int UsedCount) : IGameEvent;

// 狷狭 pending state rides the owning program frame, exactly like BijingPunish.
public enum JuanxiaStage { Choice, Card, Fire, Child, Retaliate, Slash }
public sealed record JuanxiaLaunchState(int TargetSeat, int UsedCount,
    IReadOnlyList<string> UsedKinds, JuanxiaStage Stage, string? PendingKind, int RevealedCardId = 0);
public sealed record JuanxiaRetaliationState(int DebtorSeat, int Remaining,
    IReadOnlyList<long> LaunchFrameIds, IReadOnlyList<int> LaunchCounts, JuanxiaStage Stage);

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public JuanxiaLaunchState? JuanxiaLaunch { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public JuanxiaRetaliationState? JuanxiaRetaliate { get; init; }
}

public sealed partial class GameEngine
{
    private const int MaximumJuanxiaTrickCount = 3;

    private static readonly CardKind[] JuanxiaTrickOrder =
        [CardKind.Duel, CardKind.Snatch, CardKind.Dismantlement, CardKind.FireAttack];

    // 狷狭 launch: at the owner's ending phase the owner picks one other
    // character and then declares up to three differently named single-target
    // ordinary tricks against them, one at a time.
    private SkillProgramStepOutcome JuanxiaProgramDeclareTricks(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SelectedTargetSeats is not [var target] || target == active.OwnerSeat ||
            !IsValidPlayerSeat(target) || !_players[target].IsAlive || _winner != Winner.None)
            return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
        {
            JuanxiaLaunch = new JuanxiaLaunchState(target, 0, [], JuanxiaStage.Choice, null)
        });
        return PresentJuanxiaTrickPrompt(GetActiveProgramFrame(frame.Id));
    }

    private bool IsJuanxiaTrickCandidate(int ownerSeat, int targetSeat, CardKind kind, IReadOnlyList<string> usedKinds)
    {
        if (usedKinds.Contains(kind.ToString(), StringComparer.Ordinal)) return false;
        var owner = _players[ownerSeat];
        var target = _players[targetSeat];
        if (!owner.IsAlive || !target.IsAlive || !IsJuanxiaSingleTargetTrick(kind)) return false;
        if (IsCardTargetProhibited(target, kind) || IsDirectedCardTargetProhibited(ownerSeat, targetSeat, kind))
            return false;
        return kind switch
        {
            CardKind.Duel => true,
            CardKind.Dismantlement => HasTargetCard(target),
            CardKind.Snatch => HasTargetCard(target) &&
                (GetCombatDistance(ownerSeat, targetSeat) == 1 ||
                 HasCardDistanceExemption(owner, target, CardKind.Snatch) ||
                 HasCardPolicy(owner, SkillProgramCardPolicyKind.IgnoreUseDistance, CardKind.Snatch)),
            CardKind.FireAttack => GetHand(target).Count > 0,
            _ => false
        };
    }

    // 仅指定唯一目标的普通锦囊牌：the card's targeting is exactly one other
    // character, so instant single-target tricks qualify and every other
    // ordinary trick (self/none/one-or-two/multi-target) stays outside.
    private static bool IsJuanxiaSingleTargetTrick(CardKind kind) => kind is
        CardKind.Duel or CardKind.Snatch or CardKind.Dismantlement or CardKind.FireAttack;

    private SkillProgramStepOutcome PresentJuanxiaTrickPrompt(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var state = active.JuanxiaLaunch ??
            throw new InvalidOperationException("The Juanxia launch lost its frame state.");
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            !_players[state.TargetSeat].IsAlive || state.UsedCount >= MaximumJuanxiaTrickCount)
        {
            FinishJuanxiaLaunch(active);
            return SkillProgramStepOutcome.AwaitChild;
        }
        var candidates = JuanxiaTrickOrder.Where(kind =>
            IsJuanxiaTrickCandidate(active.OwnerSeat, state.TargetSeat, kind, state.UsedKinds)).ToArray();
        if (candidates.Length == 0)
        {
            FinishJuanxiaLaunch(active);
            return SkillProgramStepOutcome.AwaitChild;
        }
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = candidates.Select(kind => new PromptChoice(
                new ChoiceId($"juanxia-trick.frame-{frame.Id}.{kind}"),
                $"视为对 {_players[state.TargetSeat].Name} 使用【{CardCatalog.Get(kind).DisplayName}】。",
                [], [state.TargetSeat],
                new Dictionary<string, string>
                {
                    ["program-action"] = "juanxia-trick",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["trick"] = kind.ToString()
                }))
            .Append(new PromptChoice(
                new ChoiceId($"juanxia-trick.frame-{frame.Id}.stop"),
                "停止发动。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "juanxia-trick",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["trick"] = "stop"
                }))
            .ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择视为对 {_players[state.TargetSeat].Name} 使用的普通锦囊，或停止。",
            [], [state.TargetSeat], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择普通锦囊",
                $"至多 {MaximumJuanxiaTrickCount} 张牌名各不相同、仅指定唯一目标的普通锦囊牌依次视为使用。"),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void FinishJuanxiaLaunch(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var state = active.JuanxiaLaunch;
        if (state is null) return;
        if (state.UsedCount > 0 && _players[state.TargetSeat].IsAlive)
        {
            AdvanceEventRulesAndQueueFact(new ProgramJuanxiaDebtEvent(active.Id, active.SkillId,
                GetProgramBindingId(active), active.OwnerSeat, state.TargetSeat, state.UsedCount));
            AddLog("SkillEffect",
                $"{_players[active.OwnerSeat].Name} 狷狭：{_players[state.TargetSeat].Name} 的下一个结束阶段开始时，其可以视为对 {_players[active.OwnerSeat].Name} 使用 {state.UsedCount} 张【杀】。",
                active.OwnerSeat, state.TargetSeat);
        }
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { JuanxiaLaunch = null });
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }

    private void ResolveJuanxiaTrickChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Juanxia trick choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.JuanxiaDeclareTricks } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Juanxia trick choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat || active.JuanxiaLaunch is not { } state ||
            state.Stage != JuanxiaStage.Choice)
            throw new InvalidOperationException("The Juanxia trick chooser changed while suspended.");
        ClearPendingDecision();
        var trickParameter = selected.Parameters.GetValueOrDefault("trick");
        if (trickParameter == "stop")
        {
            FinishJuanxiaLaunch(active);
            return;
        }
        if (!Enum.TryParse<CardKind>(trickParameter, out var kind) ||
            !IsJuanxiaSingleTargetTrick(kind) ||
            !IsJuanxiaTrickCandidate(active.OwnerSeat, state.TargetSeat, kind, state.UsedKinds))
            throw new InvalidOperationException("The selected Juanxia trick is no longer legal.");
        switch (kind)
        {
            case CardKind.Duel:
                BeginJuanxiaVirtualDuel(active, state with
                {
                    UsedCount = state.UsedCount + 1,
                    UsedKinds = AppendJuanxiaKind(state.UsedKinds, kind),
                    Stage = JuanxiaStage.Child
                });
                return;
            case CardKind.Snatch or CardKind.Dismantlement:
                ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with
                {
                    JuanxiaLaunch = state with { PendingKind = kind.ToString(), Stage = JuanxiaStage.Card }
                });
                PresentJuanxiaTargetCardPrompt(GetActiveProgramFrame(active.Id), kind);
                return;
            case CardKind.FireAttack:
                BeginJuanxiaFireAttackReveal(active, state);
                return;
            default:
                throw new InvalidOperationException($"The Juanxia trick kind '{kind}' is unsupported.");
        }
    }

    private static IReadOnlyList<string> AppendJuanxiaKind(IReadOnlyList<string> kinds, CardKind kind) =>
        Array.AsReadOnly(kinds.Append(kind.ToString()).ToArray());

    // 决斗 resolves through the shared cardless attack keyed by the owning
    // frame; the completion returns here through the program resume dispatch.
    private void BeginJuanxiaVirtualDuel(ProgramSkillFrame frame, JuanxiaLaunchState state)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (ActiveCardAttack is not null || ActiveDuel is not null)
            throw new InvalidOperationException("A Juanxia Duel cannot overwrite a pending card resolution.");
        ReplaceRuntimeTop(active with { JuanxiaLaunch = state });
        var attack = new CardAttackHandle(this, active.Id, active.OwnerSeat, state.TargetSeat,
            card: null, damageAmount: 1, playedCardKind: CardKind.Duel, programSkillFrameId: active.Id);
        ActiveCardAttack = attack;
        ActiveDuel = new DuelHandle(this, attack);
        AddLog("SkillTriggered",
            $"{_players[active.OwnerSeat].Name} 发动【{_contentRegistry!.GetSkill(active.SkillId).Name}】，视为对 {_players[state.TargetSeat].Name} 使用【决斗】。",
            active.OwnerSeat, state.TargetSeat);
        AdvanceEventRulesAndQueueFact(new ProgramJuanxiaTrickUsedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, state.TargetSeat, CardKind.Duel.ToString(), 0, false));
        BeginDuelResponse(ActiveDuel);
    }

    private void PresentJuanxiaTargetCardPrompt(ProgramSkillFrame frame, CardKind kind)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var state = active.JuanxiaLaunch ??
            throw new InvalidOperationException("The Juanxia card choice lost its frame state.");
        var target = _players[state.TargetSeat];
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var cardName = CardCatalog.Get(kind).DisplayName;
        var choices = new List<PromptChoice>();
        void Add(Card? card, string description, string parameter)
        {
            choices.Add(new PromptChoice(
                new ChoiceId($"juanxia-card.frame-{frame.Id}.{kind}.{parameter}"),
                description,
                card is null ? [] : [card.Id], [target.Seat],
                new Dictionary<string, string>
                {
                    ["program-action"] = "juanxia-target-card",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["trick"] = kind.ToString(),
                    ["card-id"] = card?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    ["card-ref"] = parameter
                }));
        }
        if (GetHand(target).Count > 0)
            Add(null, $"随机获得 {target.Name} 的一张手牌。", "hand");
        foreach (var equipment in GetEquipment(target).OrderBy(card => card.Id))
            Add(equipment, $"选择 {target.Name} 装备区的【{equipment.DisplayName}】。", $"card-{equipment.Id}");
        foreach (var judgment in GetJudgment(target).OrderBy(card => card.Id))
            Add(judgment, $"选择 {target.Name} 判定区的【{judgment.DisplayName}】。", $"card-{judgment.Id}");
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择【{cardName}】的目标牌。",
            choices.SelectMany(choice => choice.Cards).Distinct().Order().ToArray(),
            [target.Seat], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择目标牌", $"视为对 {target.Name} 使用【{cardName}】。"),
            Choices = choices.AsReadOnly()
        };
        _status = _players[active.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveJuanxiaTargetCardChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Juanxia card choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.JuanxiaDeclareTricks } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Juanxia card choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat || active.JuanxiaLaunch is not { } state ||
            state.Stage != JuanxiaStage.Card ||
            !Enum.TryParse<CardKind>(state.PendingKind, out var kind) ||
            kind is not (CardKind.Snatch or CardKind.Dismantlement))
            throw new InvalidOperationException("The Juanxia card chooser changed while suspended.");
        ClearPendingDecision();
        var owner = _players[active.OwnerSeat];
        var target = _players[state.TargetSeat];
        var cardReference = selected.Parameters.GetValueOrDefault("card-ref");
        Card card;
        CardLocation from;
        if (cardReference == "hand")
        {
            var hand = GetHand(target).ToArray();
            if (hand.Length == 0)
            {
                CancelJuanxiaTrick(active, state, kind, "狷狭：目标已没有手牌，这张牌的结算取消。");
                return;
            }
            card = hand[_random.Next(hand.Length)];
            from = CardLocation.Hand(target.Seat);
        }
        else
        {
            if (!int.TryParse(selected.Parameters.GetValueOrDefault("card-id"),
                    System.Globalization.CultureInfo.InvariantCulture, out var cardId))
                throw new InvalidOperationException("The Juanxia card choice lost its selected card.");
            card = _cardZones.CardsAt(_cardZones.GetLocation(cardId)).Single(candidate => candidate.Id == cardId);
            from = _cardZones.GetLocation(cardId);
            if (from.OwnerSeat != target.Seat || from.Zone is not (CardZoneKind.Equipment or CardZoneKind.Judgment))
            {
                CancelJuanxiaTrick(active, state, kind, "狷狭：目标牌已离开原区域，这张牌的结算取消。");
                return;
            }
        }
        var destination = kind == CardKind.Snatch ? CardLocation.Hand(owner.Seat) : CardLocation.DiscardPile;
        MoveCard(card, from, destination, new($"skill-program.{active.SkillId}.{kind switch
        {
            CardKind.Snatch => "snatch",
            _ => "dismantle"
        }}"));
        AddLog("SkillTriggered",
            $"{owner.Name} 发动【{_contentRegistry!.GetSkill(active.SkillId).Name}】，视为对 {target.Name} 使用【{CardCatalog.Get(kind).DisplayName}】。",
            active.OwnerSeat, target.Seat);
        AdvanceEventRulesAndQueueFact(new ProgramJuanxiaTrickUsedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, target.Seat, kind.ToString(), card.Id, false));
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with
        {
            JuanxiaLaunch = state with
            {
                UsedCount = state.UsedCount + 1,
                UsedKinds = AppendJuanxiaKind(state.UsedKinds, kind),
                Stage = JuanxiaStage.Child,
                PendingKind = null
            }
        });
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }

    // 火攻: the target's random hand card is shown publicly, then the owner may
    // discard one same-color hand or equipment card to deal one fire damage.
    private void BeginJuanxiaFireAttackReveal(ProgramSkillFrame frame, JuanxiaLaunchState state)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var target = _players[state.TargetSeat];
        var hand = GetHand(target).ToArray();
        if (hand.Length == 0)
        {
            CancelJuanxiaTrick(active, state, CardKind.FireAttack, "狷狭：目标已没有手牌，这张牌的结算取消。");
            return;
        }
        var revealed = hand[_random.Next(hand.Length)];
        AddLog("CardEffect",
            $"狷狭：{target.Name} 的手牌【{revealed.DisplayName}】被展示（视为使用【火攻】）。",
            active.OwnerSeat, target.Seat);
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with
        {
            JuanxiaLaunch = state with
            {
                PendingKind = CardKind.FireAttack.ToString(),
                RevealedCardId = revealed.Id,
                Stage = JuanxiaStage.Fire
            }
        });
        PresentJuanxiaFirePaymentPrompt(GetActiveProgramFrame(active.Id), revealed);
    }

    private void PresentJuanxiaFirePaymentPrompt(ProgramSkillFrame frame, Card revealed)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var state = active.JuanxiaLaunch ??
            throw new InvalidOperationException("The Juanxia fire payment lost its frame state.");
        var owner = _players[active.OwnerSeat];
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var revealedIsRed = IsRedSuit(revealed.Suit);
        var costs = GetHand(owner).Concat(GetEquipment(owner))
            .Where(card => IsRedSuit(card.Suit) == revealedIsRed)
            .OrderBy(card => card.Id).ToArray();
        var choices = costs.Select(card => new PromptChoice(
                new ChoiceId($"juanxia-fire.frame-{frame.Id}.card-{card.Id}"),
                $"弃置【{card.DisplayName}】，对 {_players[state.TargetSeat].Name} 造成1点火焰伤害。",
                [card.Id], [state.TargetSeat],
                new Dictionary<string, string>
                {
                    ["program-action"] = "juanxia-fire-payment",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["card-id"] = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }))
            .Append(new PromptChoice(
                new ChoiceId($"juanxia-fire.frame-{frame.Id}.decline"),
                "放弃伤害。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "juanxia-fire-payment",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["card-id"] = string.Empty
                }))
            .ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】火攻：展示的是【{revealed.DisplayName}】，请选择弃置一张同颜色牌或放弃伤害。",
            costs.Select(card => card.Id).ToArray(), [state.TargetSeat], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 火攻", "弃置一张与展示牌颜色相同的手牌或装备牌，令其受到1点火焰伤害。"),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveJuanxiaFirePaymentChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Juanxia fire payment lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.JuanxiaDeclareTricks } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Juanxia fire payment does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat || active.JuanxiaLaunch is not { } state ||
            state.Stage != JuanxiaStage.Fire || state.RevealedCardId == 0)
            throw new InvalidOperationException("The Juanxia fire payment changed while suspended.");
        ClearPendingDecision();
        var owner = _players[active.OwnerSeat];
        var revealedLocation = _cardZones.GetLocation(state.RevealedCardId);
        var revealedStillShown = revealedLocation.OwnerSeat == state.TargetSeat &&
            revealedLocation.Zone == CardZoneKind.Hand;
        var cardIdParameter = selected.Parameters.GetValueOrDefault("card-id");
        var paid = !string.IsNullOrEmpty(cardIdParameter) && revealedStillShown;
        if (paid)
        {
            if (!int.TryParse(cardIdParameter, System.Globalization.CultureInfo.InvariantCulture, out var costId))
                throw new InvalidOperationException("The Juanxia fire payment lost its selected cost.");
            var costFrom = _cardZones.GetLocation(costId);
            var cost = _cardZones.CardsAt(costFrom).SingleOrDefault(card => card.Id == costId);
            var revealedCard = _cardZones.CardsAt(revealedLocation).SingleOrDefault(card => card.Id == state.RevealedCardId) ??
                throw new InvalidOperationException("The Juanxia fire reveal left its shown hand card.");
            if (cost is null || costFrom.OwnerSeat != active.OwnerSeat ||
                costFrom.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                IsRedSuit(cost.Suit) != IsRedSuit(revealedCard.Suit))
                throw new InvalidOperationException("The Juanxia fire payment is no longer a legal same-color card.");
            MoveCard(cost, costFrom, CardLocation.DiscardPile, CardMoveReasons.FireAttackDiscard);
        }
        AddLog("SkillTriggered",
            $"{owner.Name} 发动【{_contentRegistry!.GetSkill(active.SkillId).Name}】，视为对 {_players[state.TargetSeat].Name} 使用【火攻】{(paid ? "，造成1点火焰伤害。" : "，未造成伤害。")}",
            active.OwnerSeat, state.TargetSeat);
        AdvanceEventRulesAndQueueFact(new ProgramJuanxiaTrickUsedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, state.TargetSeat, CardKind.FireAttack.ToString(),
            state.RevealedCardId, paid));
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with
        {
            JuanxiaLaunch = state with
            {
                UsedCount = state.UsedCount + 1,
                UsedKinds = AppendJuanxiaKind(state.UsedKinds, CardKind.FireAttack),
                Stage = JuanxiaStage.Child,
                PendingKind = null,
                RevealedCardId = 0
            }
        });
        if (!paid)
        {
            PresentJuanxiaTrickPrompt(GetActiveProgramFrame(active.Id));
            return;
        }
        BeginProgramSkillDamage(GetActiveProgramFrame(active.Id), state.TargetSeat, 1, nature: DamageNature.Fire);
    }

    private void CancelJuanxiaTrick(ProgramSkillFrame frame, JuanxiaLaunchState state,
        CardKind kind, string reason)
    {
        var active = GetActiveProgramFrame(frame.Id);
        AddLog("SkillEffect", reason, active.OwnerSeat, state.TargetSeat);
        PresentJuanxiaTrickPrompt(GetActiveProgramFrame(active.Id));
    }

    // 狷狭 retaliation: at the debtor's ending phase the outstanding committed
    // debts (if any) let the debtor view as using that many Slashes against the
    // owner. The settlement is recorded once per launch.
    private SkillProgramStepOutcome JuanxiaProgramRetaliation(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (frame.WindowContext is not { SourceSeat: { } turnOwner } ||
            !IsValidPlayerSeat(turnOwner) || turnOwner == active.OwnerSeat ||
            !_players[turnOwner].IsAlive || _winner != Winner.None ||
            !HasRuntimeSkillInstance(_players[active.OwnerSeat], active.SkillId, active.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var debts = OutstandingJuanxiaDebts(active.OwnerSeat, turnOwner).ToArray();
        if (debts.Length == 0) return SkillProgramStepOutcome.Continue;
        var total = debts.Sum(debt => debt.Count);
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
        {
            JuanxiaRetaliate = new JuanxiaRetaliationState(turnOwner, total,
                Array.AsReadOnly(debts.Select(debt => debt.LaunchFrameId).ToArray()),
                Array.AsReadOnly(debts.Select(debt => debt.Count).ToArray()), JuanxiaStage.Retaliate)
        });
        return PresentJuanxiaRetaliationPrompt(GetActiveProgramFrame(frame.Id));
    }

    private IReadOnlyList<(long LaunchFrameId, int Count)> OutstandingJuanxiaDebts(int ownerSeat, int debtorSeat)
    {
        var settled = CompleteProgramEventHistory().OfType<ProgramJuanxiaRetaliationEvent>()
            .Select(e => e.LaunchFrameId).ToHashSet();
        return CompleteProgramEventHistory().OfType<ProgramJuanxiaDebtEvent>()
            .Where(debt => debt.OwnerSeat == ownerSeat && debt.TargetSeat == debtorSeat &&
                !settled.Contains(debt.FrameId))
            .Select(debt => (debt.FrameId, debt.TrickCount))
            .ToArray();
    }

    private SkillProgramStepOutcome PresentJuanxiaRetaliationPrompt(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var state = active.JuanxiaRetaliate ??
            throw new InvalidOperationException("The Juanxia retaliation lost its frame state.");
        var owner = _players[active.OwnerSeat];
        var debtor = _players[state.DebtorSeat];
        if (_winner != Winner.None || !owner.IsAlive || !debtor.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId))
        {
            FinishJuanxiaRetaliation(active);
            return SkillProgramStepOutcome.AwaitChild;
        }
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = new[]
        {
            new PromptChoice(
                new ChoiceId($"juanxia-retaliate.frame-{frame.Id}.use"),
                $"视为对 {owner.Name} 使用 {state.Remaining} 张【杀】。",
                [], [active.OwnerSeat],
                new Dictionary<string, string>
                {
                    ["program-action"] = "juanxia-retaliate",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["answer"] = "use"
                }),
            new PromptChoice(
                new ChoiceId($"juanxia-retaliate.frame-{frame.Id}.decline"),
                "不使用【杀】。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "juanxia-retaliate",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["answer"] = "decline"
                })
        };
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, state.DebtorSeat,
            $"【{presentation.Name}】你可以视为对 {owner.Name} 使用 {state.Remaining} 张【杀】。",
            [], [active.OwnerSeat], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = state.DebtorSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 报复", $"{owner.Name} 曾对你发动狷狭，你可以视为对其使用等量张【杀】。"),
            Choices = choices.AsReadOnly()
        };
        _status = debtor.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveJuanxiaRetaliateChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Juanxia retaliation choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.JuanxiaRetaliation } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Juanxia retaliation choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            active.JuanxiaRetaliate is not { } state || state.Stage != JuanxiaStage.Retaliate ||
            decision.PlayerSeat != state.DebtorSeat)
            throw new InvalidOperationException("The Juanxia retaliation chooser changed while suspended.");
        ClearPendingDecision();
        if (selected.Parameters.GetValueOrDefault("answer") != "use")
        {
            FinishJuanxiaRetaliation(active);
            return;
        }
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with
        {
            JuanxiaRetaliate = state with { Stage = JuanxiaStage.Slash }
        });
        BeginJuanxiaRetaliationSlash(GetActiveProgramFrame(active.Id));
    }

    private bool CanUseJuanxiaRetaliationSlash(int actorSeat, int targetSeat) =>
        _players[actorSeat].IsAlive && _players[targetSeat].IsAlive && actorSeat != targetSeat &&
        !HasTurnCardTargetRestriction(actorSeat, SkillProgramCardTargetRestriction.SelfOnly) &&
        !IsCardUseForbidden(actorSeat, CardKind.Slash, CardActionType.Use) &&
        !IsDirectedCardTargetProhibited(actorSeat, targetSeat, CardKind.Slash) &&
        !IsCardTargetProhibited(_players[targetSeat], CardKind.Slash, Suit.None, null) &&
        !HasBeneficiarySuitShield(actorSeat, targetSeat, Suit.None) &&
        !IsSlashProhibited(_players[targetSeat]) &&
        CanSpendSlashUse(_players[actorSeat], _players[targetSeat], ignoresCount: true);

    private void BeginJuanxiaRetaliationSlash(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var state = active.JuanxiaRetaliate ??
            throw new InvalidOperationException("The Juanxia retaliation lost its frame state.");
        var debtorSeat = state.DebtorSeat;
        var ownerSeat = active.OwnerSeat;
        if (state.Remaining <= 0 || _winner != Winner.None || !_players[debtorSeat].IsAlive ||
            !_players[ownerSeat].IsAlive || !CanUseJuanxiaRetaliationSlash(debtorSeat, ownerSeat))
        {
            FinishJuanxiaRetaliation(active);
            return;
        }
        if (ActiveCardAttack is not null || ActiveDuel is not null)
            throw new InvalidOperationException("A Juanxia Slash cannot overwrite a pending card resolution.");
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with
        {
            JuanxiaRetaliate = state with { Remaining = state.Remaining - 1 }
        });
        var debtor = _players[debtorSeat];
        var owner = _players[ownerSeat];
        var resolutionId = ++_resolutionSequence;
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence,
            _resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action?.ActionId,
            CardActionType.Use, debtorSeat, debtorSeat, null, null, null, CardKind.Slash,
            [ownerSeat], [], [], effectiveSuit: Suit.None, effectiveRank: 0));
        PushRuntimeFrame(new CardUseFrame(resolutionId, debtorSeat, 0, CardKind.Slash, [ownerSeat],
            PhysicalCardIds: []) { Action = action });
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(resolutionId, 0, CardKind.Slash, debtorSeat));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(resolutionId, [ownerSeat]));
        var attack = new CardAttackHandle(this, resolutionId, debtorSeat, ownerSeat, card: null,
            damageAmount: debtor.HasAlcoholEffect ? 2 : 1, playedCardKind: CardKind.Slash,
            ignoresArmor: HasCardArmorBypass(debtor, owner, CardKind.Slash),
            programSkillCardUseFrameId: active.Id);
        CaptureProgramAlcoholConsumption(resolutionId, debtor);
        debtor.HasAlcoholEffect = false;
        ActiveCardAttack = attack;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Slash, debtorSeat, ownerSeat));
        TryMarkProgramUseCommitted(resolutionId);
        AddLog("CardUsed", $"{debtor.Name} 狷狭：视为对 {owner.Name} 使用【杀】。", debtorSeat, ownerSeat);
        if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted,
                action.TargetSeats, ProgramCardContinuation.CommittedSlash))
            BeginSlashTargetResolution(attack);
    }

    private void FinishJuanxiaRetaliation(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var state = active.JuanxiaRetaliate;
        if (state is null) return;
        var offeredTotal = state.LaunchCounts.Sum();
        var usedTotal = Math.Max(0, offeredTotal - Math.Max(0, state.Remaining));
        var remainingUsed = usedTotal;
        for (var index = 0; index < state.LaunchFrameIds.Count; index++)
        {
            var offered = state.LaunchCounts[index];
            var used = Math.Min(offered, Math.Max(0, remainingUsed));
            remainingUsed -= used;
            AdvanceEventRulesAndQueueFact(new ProgramJuanxiaRetaliationEvent(active.Id, active.SkillId,
                GetProgramBindingId(active), active.OwnerSeat, state.DebtorSeat,
                state.LaunchFrameIds[index], offered, used));
        }
        if (offeredTotal > 0)
            AddLog("SkillEffect",
                $"狷狭报复结算：{_players[state.DebtorSeat].Name} 视为对 {_players[active.OwnerSeat].Name} 使用了 {usedTotal}/{offeredTotal} 张【杀】。",
                state.DebtorSeat, active.OwnerSeat);
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { JuanxiaRetaliate = null });
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }

    // Program resume hook: a returned child (duel, program damage or a closed
    // cards-moved window) hands control back to the paused Juanxia instruction.
    private bool ResumeJuanxia(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != frameId)
            return false;
        if (frame.JuanxiaRetaliate is { Stage: JuanxiaStage.Slash } retaliation)
        {
            if (retaliation.Remaining > 0)
                BeginJuanxiaRetaliationSlash(GetActiveProgramFrame(frameId));
            else
                FinishJuanxiaRetaliation(GetActiveProgramFrame(frameId));
            return true;
        }
        if (frame.JuanxiaLaunch is not { } launch) return false;
        switch (launch.Stage)
        {
            case JuanxiaStage.Child:
                PresentJuanxiaTrickPrompt(GetActiveProgramFrame(frameId));
                return true;
            case JuanxiaStage.Card when Enum.TryParse<CardKind>(launch.PendingKind, out var kind) &&
                kind is CardKind.Snatch or CardKind.Dismantlement:
                PresentJuanxiaTargetCardPrompt(GetActiveProgramFrame(frameId), kind);
                return true;
            case JuanxiaStage.Fire when launch.RevealedCardId > 0:
                PresentJuanxiaFirePaymentPrompt(GetActiveProgramFrame(frameId),
                    _cardZones.CardsAt(_cardZones.GetLocation(launch.RevealedCardId))
                        .Single(card => card.Id == launch.RevealedCardId));
                return true;
            default:
                return false;
        }
    }

    private PromptChoice SelectAiJuanxiaChoice(PendingDecision decision)
    {
        // The prompts order benefit first and decline/stop last, so the first
        // choice is always the aggressive option.
        return decision.Choices[0];
    }

    private sealed partial class ProgramSkillHost : IYangYiProgramHost
    {
        public SkillProgramStepOutcome JuanxiaDeclareTricks(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.JuanxiaProgramDeclareTricks(frame, effect);
        public SkillProgramStepOutcome JuanxiaRetaliation(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.JuanxiaProgramRetaliation(frame, effect);
    }
}
