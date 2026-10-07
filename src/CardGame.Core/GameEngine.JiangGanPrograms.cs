namespace CardGame.Core;

// 盗书 settlement evidence: the declared suit, whether a hand card was taken
// and the branch outcome are public; the taken card's identity stays concealed
// (it resolves through the owner's own hand) while a handed-back card and a
// revealed hand enter the event as public knowledge.
public sealed record ProgramDaoshuEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TargetSeat, Suit ChosenSuit, bool TookHandCard, bool SameSuit,
    int? GivenCardId, IReadOnlyList<int> RevealedCardIds) : IGameEvent;

// The suspended guess: the take already happened; the frame either waits for
// the give-back pick or is about to finish with the reveal fallback.
public sealed record DaoshuGuessState(int TargetSeat, Suit ChosenSuit, int TakenCardId,
    Suit TakenEffectiveSuit, bool GiveBackPending);

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DaoshuGuessState? DaoshuGuess { get; init; }
}

public sealed partial class GameEngine
{
    // 盗书 stays offerable only while some other living character still holds a
    // hand card; the activation target kind re-checks the same set.
    internal bool CanStartDaoshuGuess(CharacterState owner) =>
        _players.Any(player => player.IsAlive && player.Seat != owner.Seat && GetHand(player).Count > 0);

    // 盗书 entry: the activation already selected one other character with hand
    // cards; the suit declaration is the first suspended prompt.
    private SkillProgramStepOutcome DaoshuProgramGuessAndTake(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SelectedTargetSeats is not [var target] || target == active.OwnerSeat ||
            !_players[target].IsAlive || GetHand(_players[target]).Count == 0)
        {
            CancelProgramBindingAndCleanup(active, "盗书的目标已失效或没有手牌，技能结算取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        if (!_players[active.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[active.OwnerSeat], active.SkillId, active.SkillInstanceId))
        {
            CancelProgramBindingAndCleanup(active, "盗书的技能拥有者已失效，技能结算取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        PresentDaoshuSuitPrompt(active);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PresentDaoshuSuitPrompt(ProgramSkillFrame frame)
    {
        var presentation = _contentRegistry!.GetSkill(frame.SkillId);
        var suits = new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond };
        var choices = suits.Select(suit => new PromptChoice(
            new ChoiceId($"daoshu-suit.frame-{frame.Id}.{suit}"),
            $"选择{GetSuitDisplayName(suit)}。",
            [], [frame.SelectedTargetSeats is [var target] ? target : frame.OwnerSeat],
            new Dictionary<string, string>
            {
                ["program-action"] = "daoshu-suit",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["suit"] = suit.ToString()
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, frame.OwnerSeat,
            $"【{presentation.Name}】请选择一种花色。",
            [], [], frame.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = frame.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(frame.SkillId, presentation.Name,
                $"{presentation.Name} · 选择花色",
                "选择一种花色并获得该角色的一张手牌：花色相同，你对其造成1点伤害且此技能视为未发动过；花色不同，你交给其一张其他花色的手牌（若没有需展示所有手牌）。"),
            Choices = Array.AsReadOnly(choices)
        };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveDaoshuSuitChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Daoshu suit choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.DaoshuGuessAndTake } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
            frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Daoshu suit choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SelectedTargetSeats is not [var target] ||
            !System.Enum.TryParse<Suit>(selected.Parameters.GetValueOrDefault("suit"), out var chosenSuit))
            throw new InvalidOperationException("The Daoshu suit choice lost its target or suit.");
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != frame.OwnerSeat)
            throw new InvalidOperationException("The Daoshu guesser changed while suspended.");
        var owner = _players[frame.OwnerSeat];
        if (!owner.IsAlive || !HasRuntimeSkillInstance(owner, frame.SkillId, frame.SkillInstanceId))
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(active, "盗书的技能拥有者已失效，剩余结算取消。");
            return;
        }
        var targetState = _players[target];
        if (!targetState.IsAlive || GetHand(targetState).Count == 0)
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(active, "盗书的目标已失效或没有手牌，剩余结算取消。");
            return;
        }
        ClearPendingDecision();
        // The take rides the established blind-hand privacy shape: the concealed
        // entity hops through the processing zone and only seats stay public.
        var hand = GetHand(targetState);
        var taken = hand[_random.Next(hand.Count)];
        MoveCard(taken, CardLocation.Hand(target), CardLocation.Processing,
            new($"skill-program.{frame.SkillId}.daoshu-take"));
        MoveCard(taken, CardLocation.Processing, CardLocation.Hand(owner.Seat),
            new($"skill-program.{frame.SkillId}.daoshu-take"));
        var takenSuit = GetProgramEffectiveSuit(owner, taken);
        AddLog("SkillEffect",
            $"{owner.Name} 发动【盗书】，选择{GetSuitDisplayName(chosenSuit)}，获得 {targetState.Name} 的一张手牌。",
            owner.Seat, target);
        if (takenSuit == chosenSuit)
        {
            // Correct guess: the per-turn activation is refunded so the skill may
            // fire again this phase, then the damage settles through the program
            // damage pipeline which resumes and completes this frame afterwards.
            var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
            var usageKey = (frame.OwnerSeat, frame.SkillId, plan.Activation?.UsageGroup ?? frame.ActivationId);
            if (_programUses.TryGetValue(usageKey, out var used) && used > 0)
                _programUses[usageKey] = used - 1;
            AddLog("SkillEffect",
                $"{owner.Name} 的【盗书】猜中花色，对 {targetState.Name} 造成1点伤害，此技能视为未发动过。",
                owner.Seat, target);
            AdvanceEventRulesAndQueueFact(new ProgramDaoshuEvent(active.Id, active.SkillId,
                GetProgramBindingId(active), owner.Seat, target, chosenSuit, true, true, null,
                Array.Empty<int>()));
            BeginProgramSkillDamage(GetActiveProgramFrame(frame.Id), target, 1);
            return;
        }
        // Wrong guess: one hand card of a suit other than the taken card's suit
        // goes back; without one the whole hand is shown instead.
        var eligible = GetHand(owner)
            .Where(card => card.Id != taken.Id && GetProgramEffectiveSuit(owner, card) != takenSuit)
            .ToArray();
        if (eligible.Length == 0)
        {
            var handCards = GetHand(owner).ToArray();
            SetProgramCardSet(frame.Id, "daoshu-reveal",
                handCards.Select(card => card.Id).ToArray(), SkillProgramCardSetVisibility.Private,
                handCards.Select(card => _cardZones.GetLocation(card.Id)).ToArray());
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { DaoshuGuess = null });
            RevealProgramBoundCards(GetActiveProgramFrame(frame.Id), "daoshu-reveal");
            AddLog("SkillEffect",
                $"{owner.Name} 的【盗书】未猜中花色，没有其他花色的手牌，展示了所有手牌。",
                owner.Seat, target);
            AdvanceEventRulesAndQueueFact(new ProgramDaoshuEvent(active.Id, active.SkillId,
                GetProgramBindingId(active), owner.Seat, target, chosenSuit, true, false, null,
                Array.AsReadOnly(handCards.Select(card => card.Id).ToArray())));
            if (!TryBeginCardsMovedProgramWindow())
                AdvanceRuntimeProgram(active.Id);
            return;
        }
        ReplaceRuntimeTop(active with
        {
            DaoshuGuess = new DaoshuGuessState(target, chosenSuit, taken.Id, takenSuit, true)
        });
        PresentDaoshuGiveBackPrompt(GetActiveProgramFrame(frame.Id), eligible);
    }

    private void PresentDaoshuGiveBackPrompt(ProgramSkillFrame frame, Card[] eligible)
    {
        var state = frame.DaoshuGuess ?? throw new InvalidOperationException("The Daoshu give-back lost its guess state.");
        var presentation = _contentRegistry!.GetSkill(frame.SkillId);
        var owner = _players[frame.OwnerSeat];
        var choices = eligible.Select(card => new PromptChoice(
            new ChoiceId($"daoshu-give.frame-{frame.Id}.card-{card.Id}"),
            $"交给 {_players[state.TargetSeat].Name} 【{card.DisplayName}】。",
            [card.Id], [state.TargetSeat],
            new Dictionary<string, string>
            {
                ["program-action"] = "daoshu-give-back",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["card-id"] = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, frame.OwnerSeat,
            $"【{presentation.Name}】请交给 {_players[state.TargetSeat].Name} 一张其他花色的手牌。",
            eligible.Select(card => card.Id).ToArray(), [state.TargetSeat], frame.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = frame.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(frame.SkillId, presentation.Name,
                $"{presentation.Name} · 交还手牌",
                "交给该角色一张与其所获牌花色不同的手牌，否则需展示所有手牌。"),
            Choices = Array.AsReadOnly(choices)
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveDaoshuGiveBackChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Daoshu give-back choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.DaoshuGuessAndTake } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
            frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Daoshu give-back choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        var state = active.DaoshuGuess is { GiveBackPending: true } guess ? guess :
            throw new InvalidOperationException("The Daoshu give-back choice lost its guess state.");
        if (!int.TryParse(selected.Parameters.GetValueOrDefault("card-id"),
                System.Globalization.CultureInfo.InvariantCulture, out var cardId))
            throw new InvalidOperationException("The Daoshu give-back choice carries no card.");
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != frame.OwnerSeat)
            throw new InvalidOperationException("The Daoshu giver changed while suspended.");
        var owner = _players[frame.OwnerSeat];
        var target = _players[state.TargetSeat];
        if (!owner.IsAlive || !target.IsAlive ||
            !HasRuntimeSkillInstance(owner, frame.SkillId, frame.SkillInstanceId) ||
            _cardZones.GetLocation(cardId) != CardLocation.Hand(owner.Seat))
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(active, "盗书的参与者或所选手牌已失效，剩余结算取消。");
            return;
        }
        var card = _cardZones.CardsAt(CardLocation.Hand(owner.Seat)).Single(item => item.Id == cardId);
        if (GetProgramEffectiveSuit(owner, card) == state.TakenEffectiveSuit)
            throw new InvalidOperationException("The Daoshu give-back card must differ from the taken card's suit.");
        ClearPendingDecision();
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { DaoshuGuess = null });
        MoveCard(card, CardLocation.Hand(owner.Seat), CardLocation.Hand(state.TargetSeat),
            new($"skill-program.{frame.SkillId}.daoshu-give"));
        AddLog("SkillEffect",
            $"{owner.Name} 的【盗书】未猜中花色，交给 {target.Name} 【{card.DisplayName}】。",
            owner.Seat, state.TargetSeat);
        AdvanceEventRulesAndQueueFact(new ProgramDaoshuEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), owner.Seat, state.TargetSeat, state.ChosenSuit, true, false,
            card.Id, Array.Empty<int>()));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }

    private PromptChoice SelectAiDaoshuChoice(PendingDecision decision) =>
        decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();

    private sealed partial class ProgramSkillHost : IJiangGanProgramHost
    {
        public SkillProgramStepOutcome DaoshuGuessAndTake(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.DaoshuProgramGuessAndTake(frame, effect);
    }
}
