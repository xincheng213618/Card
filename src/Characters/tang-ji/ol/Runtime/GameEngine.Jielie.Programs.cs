namespace CardGame.Core;

public sealed partial class GameEngine
{

    // 节烈: one before-damage operation. The first entry presents the public
    // suit choice; the typed re-entries prevent the damage, charge the hp
    // loss (suspending as a dying child when it empties the owner) and finish
    // with the marked character's random gift.
    private SkillProgramStepOutcome JielieProgramPreventAndGift(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied,
                SourceSeat: { } source, TargetSeat: { } damaged, Amount: > 0 } ||
            damaged != owner.Seat)
            return SkillProgramStepOutcome.Continue;
        var state = frame.JielieGift;
        if (state is null)
        {
            var marked = GetKanggeMarkedSeat(owner.Seat);
            if (!IsValidPlayerSeat(source) || !_players[source].IsAlive || source == owner.Seat ||
                marked is not { } markedSeat || !_players[markedSeat].IsAlive || markedSeat == source)
                return SkillProgramStepOutcome.Continue;
            var presentation = _contentRegistry!.GetSkill(active.SkillId);
            var choices = new List<PromptChoice>();
            foreach (var suit in new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond })
                choices.Add(new PromptChoice(
                    new ChoiceId($"jielie-gift.frame-{frame.Id}.suit-{suit}"),
                    $"防止此伤害并选择{suit switch { Suit.Spade => "黑桃", Suit.Heart => "红桃", Suit.Club => "梅花", _ => "方块" }}，失去 {frame.WindowContext.Amount} 点体力。",
                    [], [],
                    new Dictionary<string, string>
                    {
                        ["program-action"] = "jielie-gift",
                        ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["suit"] = suit.ToString()
                    }));
            choices.Add(new PromptChoice(
                new ChoiceId($"jielie-gift.frame-{frame.Id}.decline"),
                "不发动【节烈】。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "jielie-gift",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["decline"] = "true"
                }));
            _pendingDecision = new PendingDecision(
                DecisionKind.ProgramTrigger, active.OwnerSeat,
                $"【{presentation.Name}】{_players[source].Name} 对你即将造成 {frame.WindowContext.Amount} 点伤害，是否防止之并选择一种花色？",
                [], [source], active.OwnerSeat)
            {
                PromptId = CreatePromptId(),
                IsPrivate = false,
                TargetSeat = active.OwnerSeat,
                SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                    $"{presentation.Name} · 防伤选择", presentation.Description),
                Choices = choices.AsReadOnly()
            };
            _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
            return SkillProgramStepOutcome.AwaitChoice;
        }
        if (!state.Prevented)
        {
            PreventProgramCurrentDamage(GetActiveProgramFrame(frame.Id));
            state = state with { Prevented = true };
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { JielieGift = state });
        }
        if (!state.HpLost)
        {
            state = state with
            {
                HpLost = true,
                HpBeforePayment = owner.Hp,
                PaidHpLost = frame.WindowContext.Amount
            };
            // The paid instruction re-enters only after its native HP/dying
            // children return. Its receipt keeps the original cost paid once.
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
            {
                ReexecuteParticipantInstruction = true,
                JielieGift = state
            });
            if (new ProgramSkillHost(this).LoseHp(active.Id, active.SkillId,
                    owner.Seat, frame.WindowContext.Amount) == SkillProgramStepOutcome.Continue)
                AdvanceRuntimeProgram(active.Id);
            return SkillProgramStepOutcome.AwaitChild;
        }
        var gifted = _players[state.MarkedSeat].IsAlive
            ? GiftJielieSuitCards(active, state, frame.WindowContext.Amount) : 0;
        AddLog("SkillEffect",
            $"{owner.Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】：防止 {frame.WindowContext.Amount} 点伤害，失去 {state.PaidHpLost} 点体力，令 {_players[state.MarkedSeat].Name} 从弃牌堆随机获得 {gifted} 张{SuitName(state.Suit)}牌。",
            active.OwnerSeat, state.MarkedSeat);
        AdvanceEventRulesAndQueueFact(new ProgramJieliePreventedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), owner.Seat, source,
            frame.WindowContext.Amount, state.Suit, state.PaidHpLost, gifted, state.MarkedSeat));
        // Keep the paid receipt while native gift children still ride on this
        // before-damage program. Its completed cursor returns without reissue.
        return SkillProgramStepOutcome.Continue;
    }

    private int GiftJielieSuitCards(ProgramSkillFrame frame, JielieGiftState state, int amount)
    {
        var marked = state.MarkedSeat;
        var pool = _cardZones.CardsAt(CardLocation.DiscardPile).Where(card => card.Suit == state.Suit).ToList();
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.jielie-gift");
        var gifted = 0;
        while (gifted < amount && pool.Count > 0)
        {
            var card = pool[_random.Next(pool.Count)];
            pool.Remove(card);
            MoveCard(card, CardLocation.DiscardPile, CardLocation.Hand(marked), reason);
            gifted++;
        }
        return gifted;
    }

    [ProgramAnswer("jielie-gift")]
    private void ResolveJielieSuitChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Jielie choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.JieliePreventAndGift } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Jielie choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Jielie choice lost its chooser.");
        ClearPendingDecision();
        if (selected.Parameters.GetValueOrDefault("decline") == "true")
        {
            AddLog("SkillTriggered",
                $"{_players[active.OwnerSeat].Name} 未发动【{_contentRegistry.GetSkill(active.SkillId).Name}】。",
                active.OwnerSeat);
            AdvanceRuntimeProgram(active.Id);
            return;
        }
        if (!Enum.TryParse(selected.Parameters.GetValueOrDefault("suit"), out Suit suit) ||
            frame.WindowContext is not { SourceSeat: { } source, Amount: > 0 } ||
            GetKanggeMarkedSeat(active.OwnerSeat) is not { } marked ||
            !_players[marked].IsAlive || marked == source || !_players[source].IsAlive)
            throw new InvalidOperationException("The Jielie choice lost its damage window or marked target.");
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
        {
            ReexecuteParticipantInstruction = true,
            JielieGift = new JielieGiftState(suit, marked, false, false)
        });
        AdvanceRuntimeProgram(active.Id);
    }

    private ProgramSkillFrame? JieliePaidObserverRoot(long beforeDamageId)
    {
        var index = _resolutionStack.FindIndex(item => item.Id == beforeDamageId);
        if (index < 0 || index + 1 >= _resolutionStack.Count ||
            _resolutionStack[index] is not BeforeDamageProgramWindowFrame { Prevented: true } window ||
            _resolutionStack[index + 1] is not ProgramSkillFrame
                { JielieGift: { Prevented: true, HpLost: true } paid } frame ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied,
                ParentFrameId: var parent, SourceSeat: { } source, TargetSeat: { } target, Amount: > 0 } context ||
            parent != window.Id || target != frame.OwnerSeat || source != window.SourceSeat ||
            target != window.TargetSeat || context.Amount != window.Amount || frame.InstructionIndex < 1 ||
            paid.HpBeforePayment < 1 || paid.PaidHpLost != window.Amount ||
            paid.Suit is not (Suit.Spade or Suit.Heart or Suit.Club or Suit.Diamond) ||
            !IsValidPlayerSeat(paid.MarkedSeat) || paid.MarkedSeat == frame.OwnerSeat ||
            _contentRegistry.GetSkill(frame.SkillId).Program is not { } program || program.GameplayHash != frame.GameplayHash ||
            ProgramInstructionResolver.Default.Resolve(frame, program).GetPausedInstruction(frame.InstructionIndex).Effect.Op !=
                SkillProgramEffectOp.JieliePreventAndGift ||
            CompleteProgramEventHistory().OfType<ProgramSkillHpLostEvent>().Count(fact =>
                fact.FrameId == frame.Id && fact.SkillId == frame.SkillId && fact.TargetSeat == frame.OwnerSeat &&
                fact.Amount == paid.PaidHpLost && (long)fact.RemainingHp == (long)paid.HpBeforePayment - window.Amount) != 1)
            return null;
        return frame;
    }

    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 50)]
    private bool HasJielieGiftDying(long beforeDamageId) =>
        JieliePaidObserverRoot(beforeDamageId) is { } frame &&
        ActiveDying is { ResumesProgramSkill: true } dying && dying.ParentFrameId == frame.Id &&
        dying.VictimSeat == frame.OwnerSeat &&
        _resolutionStack.FindIndex(item => item.Id == dying.Id) is var dyingIndex && dyingIndex > 0 &&
        _resolutionStack[dyingIndex] is DyingFrame { KillerSeat: null } child &&
        child.ParentFrameId == frame.Id && _resolutionStack[dyingIndex - 1].Id == frame.Id;

    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 630, group: 10)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 720)]
    private bool IsJielieGiftProgramDying() =>
        _resolutionStack.OfType<BeforeDamageProgramWindowFrame>().Any(window => HasJielieGiftDying(window.Id));

    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 60)]
    private bool HasJielieGiftDamageObserver(long beforeDamageId) =>
        JieliePaidObserverRoot(beforeDamageId) is { } frame &&
        (HasJielieGiftDying(beforeDamageId) || DamageCursorEffectiveTop(includeNestedObservers: true)?.Id == frame.Id);


    [ProgramAiChoice(SkillProgramEffectOp.JieliePreventAndGift)]
    private PromptChoice SelectAiJielieSuitChoice(PendingDecision decision, ProgramSkillFrame frame)
    {
        var declines = decision.Choices
            .Where(choice => choice.Parameters.GetValueOrDefault("decline") == "true").ToArray();
        var amount = frame.WindowContext?.Amount ?? 0;
        var owner = _players[frame.OwnerSeat];
        // Paying the full damage value as hp loss would empty the owner's hp and
        // open a dying resolution, so the estimate declines that trade.
        if (amount >= owner.Hp && declines.Length > 0) return declines[0];
        var offers = decision.Choices
            .Where(choice => choice.Parameters.GetValueOrDefault("decline") != "true" &&
                Enum.TryParse(choice.Parameters.GetValueOrDefault("suit"), out Suit _))
            .Select(choice => (Choice: choice,
                Pool: _cardZones.CardsAt(CardLocation.DiscardPile).Count(card =>
                    card.Suit == Enum.Parse<Suit>(choice.Parameters.GetValueOrDefault("suit")!))))
            .OrderByDescending(item => item.Pool)
            .ThenBy(item => item.Choice.Id.Value, StringComparer.Ordinal)
            .ToArray();
        if (offers.Length == 0) return declines.Length > 0 ? declines[0] : decision.Choices[0];
        return offers[0].Choice;
    }
}
