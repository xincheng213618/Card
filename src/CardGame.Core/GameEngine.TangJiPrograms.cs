namespace CardGame.Core;

// 抗歌 evidence: the chosen 抗歌 target is public state (a 歌 marker on the
// target), so the event records both seats directly.
public sealed record ProgramKanggeChosenEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int ChosenSeat) : IGameEvent;
// 抗歌 gain evidence: one record per drawn card batch; the per-turn cap ledger
// derives from these committed scalars, so no drawn card ids are needed.
public sealed record ProgramKanggeGainedDrawEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int GainerSeat, int TurnNumber, int DrawnCount) : IGameEvent;
// 抗歌 rescue evidence: the once-per-round ledger derives from the round number.
public sealed record ProgramKanggeHealedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int VictimSeat, int RoundNumber) : IGameEvent;
// 抗歌 price evidence: the discarded cards are public through their movements;
// the event records only the count and the death that priced them.
public sealed record ProgramKanggeDeathPriceEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int VictimSeat, int DiscardedCardCount) : IGameEvent;
// 节烈 evidence: prevention, hp loss, chosen suit and the random gift count are
// all public facts; gifted card ids stay in their movement records.
public sealed record ProgramJieliePreventedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int DamageSourceSeat, int PreventedAmount, Suit ChosenSuit,
    int LostHp, int GiftedCount, int MarkedSeat) : IGameEvent;

// One 节烈 resolution: the chosen suit, marked seat and payment stages ride
// the owning program frame so the dying child (if the loss empties the owner's
// hp) resumes the same paid instruction and finishes the gift afterwards.
public sealed record JielieGiftState(Suit Suit, int MarkedSeat, bool Prevented, bool HpLost,
    int HpBeforePayment = 0, int PaidHpLost = 0);

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public JielieGiftState? JielieGift { get; init; }
}

public sealed partial class GameEngine
{
    // 抗歌: the 歌 marker is attributed to the owner, so two sources can never
    // confuse their targets and the public marker rides snapshots/checkpoints.
    private bool IsKanggeMarkedSeat(int seat, int ownerSeat) =>
        IsValidPlayerSeat(seat) &&
        _players[seat].MarkerSourceCounts.GetValueOrDefault((PlayerMarkerKind.Kangge, ownerSeat)) > 0;

    private int? GetKanggeMarkedSeat(int ownerSeat) => _players
        .Where(player => player.MarkerSourceCounts.GetValueOrDefault((PlayerMarkerKind.Kangge, ownerSeat)) > 0)
        .Select(player => (int?)player.Seat).FirstOrDefault();

    // 抗歌 choice: the first turn start publicly marks one other living
    // character for the rest of the game; the 歌 marker is the public record.
    private SkillProgramStepOutcome KanggeProgramChooseTarget(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            GetKanggeMarkedSeat(owner.Seat) is not null)
            return SkillProgramStepOutcome.Continue;
        var candidates = _players.Where(player => player.IsAlive && player.Seat != owner.Seat)
            .Select(player => player.Seat).ToArray();
        if (candidates.Length == 0) return SkillProgramStepOutcome.Continue;
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = candidates.Select(seat => new PromptChoice(
            new ChoiceId($"kangge-choose.frame-{frame.Id}.seat-{seat}"),
            $"选择 {_players[seat].Name}。",
            [], [seat],
            new Dictionary<string, string>
            {
                ["program-action"] = "kangge-choose",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["chosen"] = seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择一名其他角色作为“抗歌”对象。",
            [], candidates, active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择“抗歌”对象",
                "本局中：其于其回合外获得手牌时你摸等量的牌（每回合至多三张）；每轮限一次，其进入濒死状态时你可以令其回复体力至1点；其死亡时你弃置所有牌并失去1点体力。"),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveKanggeChooseChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Kangge choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.KanggeChooseTarget } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Kangge choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("chosen"),
                System.Globalization.CultureInfo.InvariantCulture, out var chosen) ||
            !IsValidPlayerSeat(chosen) || chosen == active.OwnerSeat || !_players[chosen].IsAlive)
            throw new InvalidOperationException("The Kangge choice lost its chooser or candidate.");
        ClearPendingDecision();
        SetAttributedNatureMarker(active.OwnerSeat, chosen, PlayerMarkerKind.Kangge, 1, active.Id);
        AddLog("SkillEffect",
            $"{_players[active.OwnerSeat].Name} 的【{_contentRegistry.GetSkill(active.SkillId).Name}】：选择 {_players[chosen].Name} 作为“抗歌”对象。",
            active.OwnerSeat, chosen);
        AdvanceEventRulesAndQueueFact(new ProgramKanggeChosenEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, chosen));
        AdvanceRuntimeProgram(active.Id);
    }

    // 抗歌 gain: one candidate per observed hand-card gain of the marked
    // character outside their turn; the per-turn cap derives from committed
    // evidence, so replays reproduce it exactly.
    private SkillProgramStepOutcome KanggeProgramGainDraw(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.CardsGained, TargetSeat: { } gainer } ||
            !IsKanggeMarkedSeat(gainer, owner.Seat) || gainer == _currentSeat)
            return SkillProgramStepOutcome.Continue;
        var drawnThisTurn = CompleteProgramEventHistory().OfType<ProgramKanggeGainedDrawEvent>()
            .Where(item => item.OwnerSeat == owner.Seat && item.TurnNumber == _turnNumber)
            .Sum(item => item.DrawnCount);
        var drawn = Math.Min(1, Math.Max(0, ProgramKanggeTurnDrawCap - drawnThisTurn));
        if (drawn == 0) return SkillProgramStepOutcome.Continue;
        DrawProgramCards(active.Id, owner.Seat, drawn, null, null,
            SkillProgramCardSetVisibility.Private,
            new($"skill-program.{active.SkillId}.kangge-gain-draw"));
        AddLog("SkillEffect",
            $"{owner.Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】:{_players[gainer].Name} 于回合外获得手牌，{owner.Name} 摸 {drawn} 张牌。",
            active.OwnerSeat, gainer);
        AdvanceEventRulesAndQueueFact(new ProgramKanggeGainedDrawEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), owner.Seat, gainer, _turnNumber, drawn));
        return SkillProgramStepOutcome.Continue;
    }

    private const int ProgramKanggeTurnDrawCap = 3;

    private bool KanggeHealUsedThisRound(int ownerSeat) =>
        CompleteProgramEventHistory().OfType<ProgramKanggeHealedEvent>()
            .Any(item => item.OwnerSeat == ownerSeat && item.RoundNumber == _roundNumber);

    // 抗歌 rescue: the trigger fires for every dying character; the operation
    // filters to the marked victim and presents the voluntary recovery itself.
    private SkillProgramStepOutcome KanggeProgramHealVictim(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.DyingEntering, TargetSeat: { } victim } ||
            victim == owner.Seat || !IsKanggeMarkedSeat(victim, owner.Seat) ||
            _players[victim].Hp > 0 || KanggeHealUsedThisRound(owner.Seat))
            return SkillProgramStepOutcome.Continue;
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = new[]
        {
            new PromptChoice(
                new ChoiceId($"kangge-heal.frame-{frame.Id}.heal"),
                $"令 {_players[victim].Name} 回复体力至1点。",
                [], [victim],
                new Dictionary<string, string>
                {
                    ["program-action"] = "kangge-heal",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }),
            new PromptChoice(
                new ChoiceId($"kangge-heal.frame-{frame.Id}.decline"),
                "不发动【抗歌】。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "kangge-heal",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["decline"] = "true"
                })
        };
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】是否令 {_players[victim].Name} 回复体力至1点？（每轮限一次）",
            [], [victim], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 濒死回复", presentation.Description),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveKanggeHealChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Kangge heal choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.KanggeHealVictim } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Kangge heal choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Kangge heal choice lost its chooser.");
        ClearPendingDecision();
        if (selected.Parameters.GetValueOrDefault("decline") == "true")
        {
            AddLog("SkillTriggered",
                $"{owner.Name} 未发动【{_contentRegistry.GetSkill(active.SkillId).Name}】的濒死回复。",
                active.OwnerSeat);
            AdvanceRuntimeProgram(active.Id);
            return;
        }
        if (frame.WindowContext is not { TargetSeat: { } victim } ||
            !IsKanggeMarkedSeat(victim, owner.Seat) || owner.Seat == victim ||
            KanggeHealUsedThisRound(owner.Seat) || _players[victim].Hp > 0)
        {
            // The victim left the dying window between prompt and answer; there
            // is nothing left to rescue, so the trigger settles without effect.
            AdvanceRuntimeProgram(active.Id);
            return;
        }
        RecoverOtherDyingVictim(GetActiveProgramFrame(frame.Id), 1);
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame resumed || resumed.Id != frame.Id) return;
        AddLog("SkillEffect",
            $"{owner.Name} 的【{_contentRegistry.GetSkill(active.SkillId).Name}】：令 {_players[victim].Name} 回复体力至1点。",
            active.OwnerSeat, victim);
        AdvanceEventRulesAndQueueFact(new ProgramKanggeHealedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), owner.Seat, victim, _roundNumber));
        AdvanceRuntimeProgram(active.Id);
    }

    // 抗歌 price: when the marked character dies, the owner discards every card
    // in their hand, equipment and judgment zones and loses 1 hp.
    private SkillProgramStepOutcome KanggeProgramDeathPrice(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.CharacterDied, TargetSeat: { } victim } ||
            !IsKanggeMarkedSeat(victim, owner.Seat))
            return SkillProgramStepOutcome.Continue;
        var hand = GetHand(owner).ToArray();
        var equipment = GetEquipment(owner).ToArray();
        var judgment = _cardZones.CardsAt(CardLocation.Judgment(owner.Seat)).ToArray();
        var reason = new CardMoveReason($"skill-program.{active.SkillId}.kangge-death-price");
        foreach (var card in hand) MoveCard(card, CardLocation.Hand(owner.Seat), CardLocation.DiscardPile, reason);
        foreach (var card in equipment) MoveCard(card, _cardZones.GetLocation(card.Id), CardLocation.DiscardPile, reason);
        foreach (var card in judgment) MoveCard(card, CardLocation.Judgment(owner.Seat), CardLocation.DiscardPile, reason);
        var discarded = hand.Length + equipment.Length + judgment.Length;
        AddLog("SkillEffect",
            $"{owner.Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】:{_players[victim].Name} 死亡，{owner.Name} 弃置所有牌（{discarded} 张）并失去1点体力。",
            active.OwnerSeat, victim);
        var before = owner.Hp;
        owner.Hp = Math.Max(0, before - 1);
        RecordHpChange(active.Id, null, owner.Seat, before, owner.Hp, HpChangeKind.Loss);
        AdvanceEventRulesAndQueueFact(new ProgramSkillHpLostEvent(active.Id, active.SkillId, owner.Seat,
            Math.Min(before, 1), owner.Hp));
        AdvanceEventRulesAndQueueFact(new ProgramKanggeDeathPriceEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), owner.Seat, victim, discarded));
        if (owner.Hp == 0)
        {
            BeginProgramSkillDying(active.Id, owner);
            return SkillProgramStepOutcome.AwaitChild;
        }
        return SkillProgramStepOutcome.Continue;
    }

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
                PaidHpLost = Math.Min(owner.Hp, frame.WindowContext.Amount)
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

    private static string SuitName(Suit suit) => suit switch
    {
        Suit.Spade => "黑桃", Suit.Heart => "红桃", Suit.Club => "梅花", _ => "方块"
    };

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
            paid.HpBeforePayment < 1 || paid.PaidHpLost != Math.Min(paid.HpBeforePayment, window.Amount) ||
            paid.Suit is not (Suit.Spade or Suit.Heart or Suit.Club or Suit.Diamond) ||
            !IsValidPlayerSeat(paid.MarkedSeat) || paid.MarkedSeat == frame.OwnerSeat ||
            _contentRegistry.GetSkill(frame.SkillId).Program is not { } program || program.GameplayHash != frame.GameplayHash ||
            ProgramInstructionResolver.Default.Resolve(frame, program).GetPausedInstruction(frame.InstructionIndex).Effect.Op !=
                SkillProgramEffectOp.JieliePreventAndGift ||
            CompleteProgramEventHistory().OfType<ProgramSkillHpLostEvent>().Count(fact =>
                fact.FrameId == frame.Id && fact.SkillId == frame.SkillId && fact.TargetSeat == frame.OwnerSeat &&
                fact.Amount == paid.PaidHpLost && fact.RemainingHp == Math.Max(0, paid.HpBeforePayment - window.Amount)) != 1)
            return null;
        return frame;
    }

    private bool HasJielieGiftDying(long beforeDamageId) =>
        JieliePaidObserverRoot(beforeDamageId) is { } frame &&
        ActiveDying is { ResumesProgramSkill: true } dying && dying.ParentFrameId == frame.Id &&
        dying.VictimSeat == frame.OwnerSeat &&
        _resolutionStack.FindIndex(item => item.Id == dying.Id) is var dyingIndex && dyingIndex > 0 &&
        _resolutionStack[dyingIndex] is DyingFrame { KillerSeat: null } child &&
        child.ParentFrameId == frame.Id && _resolutionStack[dyingIndex - 1].Id == frame.Id;

    private bool IsJielieGiftProgramDying() =>
        _resolutionStack.OfType<BeforeDamageProgramWindowFrame>().Any(window => HasJielieGiftDying(window.Id));

    private bool HasJielieGiftDamageObserver(long beforeDamageId) =>
        JieliePaidObserverRoot(beforeDamageId) is { } frame &&
        (HasJielieGiftDying(beforeDamageId) || DamageCursorEffectiveTop(includeNestedObservers: true)?.Id == frame.Id);

    private PromptChoice SelectAiKanggeChooseChoice(PendingDecision decision) =>
        decision.Choices
            .OrderByDescending(choice => choice.Targets.Select(seat => _players[seat].Hp).FirstOrDefault())
            .ThenBy(choice => choice.Targets.FirstOrDefault())
            .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal)
            .First();

    private PromptChoice SelectAiKanggeHealChoice(PendingDecision decision) =>
        decision.Choices.First(choice => choice.Parameters.GetValueOrDefault("decline") != "true");

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

    private sealed partial class ProgramSkillHost : ITangJiProgramHost
    {
        public SkillProgramStepOutcome KanggeChooseTarget(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.KanggeProgramChooseTarget(frame, effect);
        public SkillProgramStepOutcome KanggeGainDraw(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.KanggeProgramGainDraw(frame, effect);
        public SkillProgramStepOutcome KanggeHealVictim(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.KanggeProgramHealVictim(frame, effect);
        public SkillProgramStepOutcome KanggeDeathPrice(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.KanggeProgramDeathPrice(frame, effect);
        public SkillProgramStepOutcome JieliePreventAndGift(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.JielieProgramPreventAndGift(frame, effect);
    }
}
