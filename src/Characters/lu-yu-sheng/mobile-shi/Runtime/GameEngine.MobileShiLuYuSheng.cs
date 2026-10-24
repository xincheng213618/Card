using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShiLysRunweiSkill = "mobile:shi-runwei", ShiLysShuanghuaiSkill = "mobile:shi-shuanghuai";

    private static CardConversionSource ShiLysSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);

    // 润微/霜怀 tracking: the turn-stamped fields reset lazily with the turn, while the
    // Shuang Huai last-target memory persists for the whole game.
    private sealed class MobileShiLuYuShengTrack
    {
        internal int TurnStamp = -1;
        internal readonly HashSet<int> GainedSeats = [];
        internal readonly List<int> MarkedCards = [];
        internal int DebtRequired;
        internal int DebtProgress;
        internal bool Refunded;
    }

    private readonly Dictionary<int, MobileShiLuYuShengTrack> _mobileShiLuYuSheng = new();
    private readonly Dictionary<int, int> _mobileShiLuYuShengLastGuardVictim = new();

    private MobileShiLuYuShengTrack MobileShiLuYuShengTrackAt(int seat)
    {
        if (!_mobileShiLuYuSheng.TryGetValue(seat, out var track))
            _mobileShiLuYuSheng[seat] = track = new MobileShiLuYuShengTrack();
        if (track.TurnStamp != _turnNumber)
        {
            track.TurnStamp = _turnNumber;
            track.GainedSeats.Clear();
            track.MarkedCards.Clear();
            track.DebtRequired = 0;
            track.DebtProgress = 0;
            track.Refunded = false;
        }
        return track;
    }

    private MobileShiLuYuShengTrack? PeekMobileShiLuYuSheng(int seat) =>
        _mobileShiLuYuSheng.TryGetValue(seat, out var track) && track.TurnStamp == _turnNumber ? track : null;

    [ProgramCapability]
    private sealed class MobileShiLuYuShengCapability(GameEngine engine) : IMobileShiLuYuShengProgramHost
    {
        public SkillProgramStepOutcome ResolveRunweiGrant(ProgramSkillFrame frame) => engine.BeginMobileShiRunweiGrant(frame);
        public SkillProgramStepOutcome ResolveRunweiLossDebt(ProgramSkillFrame frame) => engine.ResolveMobileShiRunweiLossDebt(frame);
        public SkillProgramStepOutcome ResolveRunweiPhaseEnd(ProgramSkillFrame frame) => engine.ResolveMobileShiRunweiPhaseEnd(frame);
        public SkillProgramStepOutcome ResolveShuanghuaiGuard(ProgramSkillFrame frame) => engine.BeginMobileShiShuanghuaiGuard(frame);
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 67400)]
    private bool CanOfferMobileShiLuYuSheng(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId == ShiLysRunweiSkill)
        {
            var track = PeekMobileShiLuYuSheng(candidate.OwnerSeat);
            if (trigger.Id == MobileShiLuYuShengComposition.RunweiLossDebtTrigger) return track is { DebtRequired: > 0, Refunded: false };
            if (trigger.Id == MobileShiLuYuShengComposition.RunweiPhaseEndTrigger) return track?.MarkedCards.Count > 0;
            return true;
        }
        if (candidate.SkillId != ShiLysShuanghuaiSkill || trigger.Id != MobileShiLuYuShengComposition.ShuanghuaiGuardTrigger ||
            context is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied, TargetSeat: { } victimSeat })
            return true;
        return victimSeat != candidate.OwnerSeat && _players[victimSeat].IsAlive &&
            GetCombatDistance(candidate.OwnerSeat, victimSeat) == 1;
    }

    // 润微：出牌阶段限一次。展示牌堆顶至多五张牌，令一名角色获得其中一种颜色的所有牌，
    // 其余置入弃牌堆；以此法获得的牌记入本轮标记，债务 X=获得数。
    private int[] MobileShiRunweiRecipientSeats(ProgramSkillFrame f)
    {
        var track = PeekMobileShiLuYuSheng(f.OwnerSeat);
        return _players.Where(p => p.IsAlive && (track is null || !track.GainedSeats.Contains(p.Seat)))
            .Select(p => p.Seat).ToArray();
    }

    private void RefundMobileShiRunweiPhaseUse(int ownerSeat)
    {
        var activation = _contentRegistry.GetSkill(ShiLysRunweiSkill).Program!.Activations
            .Single(item => item.Id == MobileShiLuYuShengComposition.RunweiActivation);
        var key = (ownerSeat, ShiLysRunweiSkill, activation.UsageGroup);
        _programPhaseUses[key] = Math.Max(0, _programPhaseUses.GetValueOrDefault(key) - 1);
    }

    private SkillProgramStepOutcome BeginMobileShiRunweiGrant(ProgramSkillFrame frame)
    {
        var f = GetActiveProgramFrame(frame.Id);
        if (f.MobileShiLuYuSheng is not null || _resolutionStack.LastOrDefault()?.Id != f.Id)
            throw new InvalidOperationException("Runwei requires its original unpaid instruction.");
        if (f.SkillId != ShiLysRunweiSkill || f.ActivationId != MobileShiLuYuShengComposition.RunweiActivation ||
            f.TriggerId != null)
            throw new InvalidOperationException("Runwei requires its exact reveal-grant activation.");
        var owner = _players[f.OwnerSeat];
        var pileCount = _cardZones.Count(CardLocation.DrawPile);
        if (_winner != Winner.None || !owner.IsAlive || !HasRuntimeSkillInstance(owner, f.SkillId, f.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        if (pileCount == 0 || MobileShiRunweiRecipientSeats(f).Length == 0)
        {
            // Nothing can be revealed or nobody is targetable: refund the spent
            // activation exactly like the friend-event maximum-cost refund.
            RefundMobileShiRunweiPhaseUse(f.OwnerSeat);
            return SkillProgramStepOutcome.Continue;
        }
        ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = new("runwei", _turnNumber) });
        ContinueMobileShiRunwei(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private static bool ShiLysIsRed(Card card) => card.Suit is Suit.Heart or Suit.Diamond;

    private void ContinueMobileShiRunwei(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiLuYuSheng: { Operation: "runwei" } r } f || f.Id != id) return;
        if (r.Stage == 0)
        {
            var pileCount = _cardZones.Count(CardLocation.DrawPile);
            var counts = Enumerable.Range(1, Math.Min(5, pileCount)).ToArray();
            if (counts.Length == 0) { FinishProgramSkill(f, true); return; }
            if (counts.Length == 1)
            {
                ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = r with { Stage = 1, RevealedCount = counts[0] } });
                ContinueMobileShiRunwei(id);
                return;
            }
            PublishShiLysRunweiCount(f, counts);
            return;
        }
        if (r.Stage == 1)
        {
            RevealProgramTopCards(f.Id, f.OwnerSeat, r.RevealedCount, null,
                MobileShiLuYuShengComposition.RunweiRevealBind, SkillProgramCardSetVisibility.Public);
            f = GetActiveProgramFrame(id);
            r = f.MobileShiLuYuSheng!;
            var recipients = MobileShiRunweiRecipientSeats(f);
            if (recipients.Length == 0)
            {
                DisposeShiLysRunweiLeftovers(f, []);
                return;
            }
            if (recipients.Length == 1)
            {
                ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = r with { Stage = 2, TargetSeat = recipients[0] } });
                ContinueMobileShiRunwei(id);
                return;
            }
            PublishShiLysRunweiRecipient(f, recipients);
            return;
        }
        if (r.Stage == 2)
        {
            var cards = ShiLysRunweiRevealedCards(f);
            var hasRed = cards.Any(pair => ShiLysIsRed(pair.Card));
            var hasBlack = cards.Any(pair => !ShiLysIsRed(pair.Card));
            if (hasRed == hasBlack) { PublishShiLysRunweiColor(f, hasRed, hasBlack); return; }
            ApplyMobileShiRunweiGrant(f, hasRed);
            return;
        }
        if (r.Stage == 9)
        {
            if (DrainShiLysLuYuSheng(f)) return;
            FinishProgramSkill(GetActiveProgramFrame(id), true);
            return;
        }
        throw new InvalidOperationException("Runwei has an unknown native continuation stage.");
    }

    private (Card Card, CardLocation Location)[] ShiLysRunweiRevealedCards(ProgramSkillFrame f)
    {
        var set = GetProgramCardSet(f, MobileShiLuYuShengComposition.RunweiRevealBind);
        var pairs = new (Card, CardLocation)[set.CardIds.Count];
        for (var i = 0; i < set.CardIds.Count; i++)
        {
            var location = set.SourceLocations[i];
            if (_cardZones.GetLocation(set.CardIds[i]) != location)
                throw new InvalidOperationException("Runwei revealed cards left their frozen processing positions.");
            pairs[i] = (_cardZones.CardsAt(location).Single(card => card.Id == set.CardIds[i]), location);
        }
        return pairs;
    }

    private void DisposeShiLysRunweiLeftovers(ProgramSkillFrame f, IReadOnlyList<Card> granted)
    {
        var cards = ShiLysRunweiRevealedCards(f);
        var kept = granted.Select(card => card.Id).ToHashSet();
        var leftovers = cards.Where(pair => !kept.Contains(pair.Card.Id)).ToArray();
        var r = f.MobileShiLuYuSheng!;
        ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = r with { Stage = 9 },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var reason = new CardMoveReason(MobileShiLuYuShengComposition.RunweiLeftoverMoveReason);
        foreach (var (card, location) in leftovers)
            MoveCard(card, location, CardLocation.DiscardPile, reason);
        if (!TryBeginCardsMovedProgramWindow()) FinishProgramSkill(f, true);
    }

    private void ApplyMobileShiRunweiGrant(ProgramSkillFrame f, bool red)
    {
        var r = f.MobileShiLuYuSheng!;
        var recipient = _players[r.TargetSeat];
        var cards = ShiLysRunweiRevealedCards(f);
        var matched = cards.Where(pair => ShiLysIsRed(pair.Card) == red).ToArray();
        var leftovers = cards.Where(pair => !matched.Contains(pair)).ToArray();
        var track = MobileShiLuYuShengTrackAt(f.OwnerSeat);
        ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = r with { Stage = 9 },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var grantReason = new CardMoveReason(MobileShiLuYuShengComposition.RunweiGrantMoveReason);
        foreach (var (card, location) in matched)
            MoveCard(card, location, CardLocation.Hand(recipient.Seat), grantReason);
        var leftoverReason = new CardMoveReason(MobileShiLuYuShengComposition.RunweiLeftoverMoveReason);
        foreach (var (card, location) in leftovers)
            MoveCard(card, location, CardLocation.DiscardPile, leftoverReason);
        track.MarkedCards.AddRange(matched.Select(pair => pair.Card.Id));
        track.GainedSeats.Add(recipient.Seat);
        track.DebtRequired = matched.Length;
        track.DebtProgress = 0;
        track.Refunded = false;
        AdvanceEventRulesAndQueueFact(new MobileShiRunweiGrantIssuedEvent(f.Id, ShiLysSource(f), f.GameplayHash,
            _turnNumber, recipient.Seat, cards.Length, matched.Length, red));
        AddLog("SkillEffect",
            $"{_players[f.OwnerSeat].Name} 润微：展示牌堆顶 {cards.Length} 张牌，{recipient.Name} 获得其中{(red ? "红色" : "黑色")}的 {matched.Length} 张牌，其余 {leftovers.Length} 张置入弃牌堆。", f.OwnerSeat);
        if (!TryBeginCardsMovedProgramWindow()) FinishProgramSkill(f, true);
    }

    private IReadOnlyList<PromptChoice> ShiLysRunweiCountChoices(ProgramSkillFrame f, int[] counts) => Array.AsReadOnly(counts
        .Select(count => new PromptChoice(new($"shi-lu-yu-sheng.{f.Id}.n{count}"),
            $"展示牌堆顶 {count} 张牌", [], [], new Dictionary<string, string>
            { ["program-action"] = "mobile-shi-runwei", ["branch"] = "count",
                ["count"] = count.ToString(CultureInfo.InvariantCulture) })).ToArray());

    private void PublishShiLysRunweiCount(ProgramSkillFrame f, int[] counts)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "润微：选择展示牌堆顶的牌数。", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = ShiLysRunweiCountChoices(f, counts),
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    private IReadOnlyList<PromptChoice> ShiLysRunweiRecipientChoices(ProgramSkillFrame f, int[] seats) => Array.AsReadOnly(seats
        .Select(seat => new PromptChoice(new($"shi-lu-yu-sheng.{f.Id}.r{seat}"),
            $"令 {_players[seat].Name} 获得展示牌中一种颜色的所有牌", [], [seat], new Dictionary<string, string>
            { ["program-action"] = "mobile-shi-runwei", ["branch"] = "target",
                ["seat"] = seat.ToString(CultureInfo.InvariantCulture) })).ToArray());

    private void PublishShiLysRunweiRecipient(ProgramSkillFrame f, int[] seats)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "润微：选择一名角色获得展示牌中一种颜色的所有牌。", [],
            seats, f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = ShiLysRunweiRecipientChoices(f, seats),
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    private IReadOnlyList<PromptChoice> ShiLysRunweiColorChoices(ProgramSkillFrame f, bool hasRed, bool hasBlack)
    {
        var cards = ShiLysRunweiRevealedCards(f);
        List<PromptChoice> choices = [];
        if (hasRed) choices.Add(new(new($"shi-lu-yu-sheng.{f.Id}.red"),
            $"令其获得红色牌（{cards.Count(pair => ShiLysIsRed(pair.Card))} 张）", [], [], new Dictionary<string, string>
            { ["program-action"] = "mobile-shi-runwei", ["branch"] = "color", ["color"] = "red" }));
        if (hasBlack) choices.Add(new(new($"shi-lu-yu-sheng.{f.Id}.black"),
            $"令其获得黑色牌（{cards.Count(pair => !ShiLysIsRed(pair.Card))} 张）", [], [], new Dictionary<string, string>
            { ["program-action"] = "mobile-shi-runwei", ["branch"] = "color", ["color"] = "black" }));
        return Array.AsReadOnly(choices.ToArray());
    }

    private void PublishShiLysRunweiColor(ProgramSkillFrame f, bool hasRed, bool hasBlack)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "润微：选择令其获得哪种颜色的牌。", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = ShiLysRunweiColorChoices(f, hasRed, hasBlack),
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    [ProgramAnswer("mobile-shi-runwei")]
    private void AnswerMobileShiRunwei(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiLuYuSheng: { Operation: "runwei" } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat)
            throw new InvalidOperationException("Runwei choice lost its original frame or chooser.");
        var branch = selected.Parameters["branch"];
        IReadOnlyList<PromptChoice> current = branch switch
        {
            "count" => ShiLysRunweiCountChoices(f, Enumerable.Range(1, Math.Min(5, _cardZones.Count(CardLocation.DrawPile))).ToArray()),
            "target" => ShiLysRunweiRecipientChoices(f, MobileShiRunweiRecipientSeats(f)),
            "color" => ShiLysRunweiColorChoices(f,
                ShiLysRunweiRevealedCards(f).Any(pair => ShiLysIsRed(pair.Card)),
                ShiLysRunweiRevealedCards(f).Any(pair => !ShiLysIsRed(pair.Card))),
            _ => throw new InvalidOperationException("Runwei choice lost its known native branch.")
        };
        if (r.Stage != (branch == "count" ? 0 : branch == "target" ? 1 : 2) ||
            !AssistedChoicesEqual([selected], current.Where(choice => choice.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Runwei choice lost its unpaid stage or legal candidates.");
        ClearPendingDecision();
        switch (branch)
        {
            case "count":
                ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = r with { Stage = 1,
                    RevealedCount = int.Parse(selected.Parameters["count"], CultureInfo.InvariantCulture) } });
                break;
            case "target":
                ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = r with { Stage = 2,
                    TargetSeat = int.Parse(selected.Parameters["seat"], CultureInfo.InvariantCulture) },
                    ChoiceBindings = Array.AsReadOnly(f.ChoiceBindings.Append(new ProgramChoiceResultBinding("runwei-recipient",
                        selected.Parameters["seat"], f.OwnerSeat)).ToArray()) });
                break;
            default:
                var red = selected.Parameters["color"] == "red";
                ApplyMobileShiRunweiGrant(f, red);
                AdvanceRulesAndPublishState();
                return;
        }
        ContinueMobileShiRunwei(f.Id);
        AdvanceRulesAndPublishState();
    }

    // 润微债务：同一出牌阶段内失去 X 张牌（X=其获得数）后退还一次阶段使用，至多一次。
    private SkillProgramStepOutcome ResolveMobileShiRunweiLossDebt(ProgramSkillFrame frame)
    {
        var f = GetActiveProgramFrame(frame.Id);
        if (f.SkillId != ShiLysRunweiSkill || f.TriggerId != MobileShiLuYuShengComposition.RunweiLossDebtTrigger ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.CardsMoved, MovementBatch: { } batch })
            throw new InvalidOperationException("Runwei loss debt requires its exact owner batch loss window.");
        var owner = _players[f.OwnerSeat];
        var track = PeekMobileShiLuYuSheng(f.OwnerSeat);
        if (_winner != Winner.None || !owner.IsAlive || !HasRuntimeSkillInstance(owner, f.SkillId, f.SkillInstanceId) ||
            track is not { DebtRequired: > 0, Refunded: false })
            return SkillProgramStepOutcome.Continue;
        var lost = batch.Movements.Count(movement => movement.From.OwnerSeat == f.OwnerSeat &&
            movement.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment);
        if (lost == 0) return SkillProgramStepOutcome.Continue;
        track.DebtProgress += lost;
        if (track.DebtProgress < track.DebtRequired) return SkillProgramStepOutcome.Continue;
        track.Refunded = true;
        RefundMobileShiRunweiPhaseUse(f.OwnerSeat);
        AdvanceEventRulesAndQueueFact(new MobileShiRunweiDebtRefundedEvent(f.Id, ShiLysSource(f), f.GameplayHash,
            _turnNumber, track.DebtRequired, track.DebtProgress));
        AddLog("SkillEffect",
            $"{owner.Name} 润微：本阶段已失去 {track.DebtProgress} 张牌（需 {track.DebtRequired} 张），润微可再次发动。", f.OwnerSeat);
        return SkillProgramStepOutcome.Continue;
    }

    // 润微阶段末：本阶段内以此法获得且仍为手牌的牌由陆郁生弃置。
    private SkillProgramStepOutcome ResolveMobileShiRunweiPhaseEnd(ProgramSkillFrame frame)
    {
        var f = GetActiveProgramFrame(frame.Id);
        if (f.SkillId != ShiLysRunweiSkill || f.TriggerId != MobileShiLuYuShengComposition.RunweiPhaseEndTrigger ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.PlayEnding })
            throw new InvalidOperationException("Runwei phase end requires its exact play-ending window.");
        var track = PeekMobileShiLuYuSheng(f.OwnerSeat);
        if (_winner != Winner.None || track is not { MarkedCards.Count: > 0 })
            return SkillProgramStepOutcome.Continue;
        var hands = track.MarkedCards.Where(id => _cardZones.GetLocation(id) is { Zone: CardZoneKind.Hand }).ToArray();
        var kept = track.MarkedCards.Count - hands.Length;
        track.MarkedCards.Clear();
        track.DebtRequired = 0;
        track.DebtProgress = 0;
        ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = new("runwei-end", _turnNumber, Stage: 9, Discarded: hands.Length) });
        AdvanceEventRulesAndQueueFact(new MobileShiRunweiPhaseEndDiscardedEvent(f.Id, ShiLysSource(f), f.GameplayHash,
            _turnNumber, hands.Length, kept));
        if (hands.Length == 0) { FinishProgramSkill(f, true); return SkillProgramStepOutcome.Continue; }
        AddLog("SkillEffect",
            $"{_players[f.OwnerSeat].Name} 润微：本阶段结束时弃置以此法获得的手牌 {hands.Length} 张。", f.OwnerSeat);
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var reason = new CardMoveReason(MobileShiLuYuShengComposition.RunweiPhaseEndMoveReason);
        foreach (var id in hands)
        {
            var location = _cardZones.GetLocation(id);
            MoveCard(_cardZones.CardsAt(location).Single(card => card.Id == id), location, CardLocation.DiscardPile, reason);
        }
        if (!TryBeginCardsMovedProgramWindow()) FinishProgramSkill(f, true);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool DrainShiLysLuYuSheng(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);

    // 霜怀：每回合限一次，与你距离1以内的其他角色受到伤害时可防止之或令其从弃牌堆获得【桃】，
    // 并按与上次发动的目标异同结算摸牌或失去体力。
    private SkillProgramStepOutcome BeginMobileShiShuanghuaiGuard(ProgramSkillFrame frame)
    {
        var f = GetActiveProgramFrame(frame.Id);
        if (f.MobileShiLuYuSheng is not null || _resolutionStack.LastOrDefault()?.Id != f.Id)
            throw new InvalidOperationException("Shuanghuai requires its original unpaid instruction.");
        if (f.SkillId != ShiLysShuanghuaiSkill || f.TriggerId != MobileShiLuYuShengComposition.ShuanghuaiGuardTrigger ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied, TargetSeat: { } victim })
            throw new InvalidOperationException("Shuanghuai requires its exact before-damage window.");
        var owner = _players[f.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive || !HasRuntimeSkillInstance(owner, f.SkillId, f.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = new("shuanghuai", _turnNumber, TargetSeat: victim) });
        ContinueMobileShiShuanghuai(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool ShiLysShuanghuaiDamagePending(ProgramSkillFrame f) =>
        f.WindowContext is { } context && _resolutionStack.OfType<BeforeDamageProgramWindowFrame>()
            .SingleOrDefault(window => window.Id == context.ParentFrameId) is { Prevented: false } &&
        CurrentDamageAttempt is { } attempt && attempt.TargetSeat == f.MobileShiLuYuSheng!.TargetSeat;

    private bool ShiLysShuanghuaiPeachAvailable() =>
        _cardZones.CardsAt(CardLocation.DiscardPile).Any(card => card.Kind == CardKind.Peach);

    private IReadOnlyList<PromptChoice> ShiLysShuanghuaiChoices(ProgramSkillFrame f)
    {
        var r = f.MobileShiLuYuSheng!;
        List<PromptChoice> choices = [];
        if (ShiLysShuanghuaiDamagePending(f))
            choices.Add(new(new($"shi-lu-yu-sheng.{f.Id}.prevent"), "防止此伤害", [], [], new Dictionary<string, string>
            { ["program-action"] = "mobile-shi-shuanghuai", ["option"] = "prevent" }));
        if (_players[r.TargetSeat].IsAlive && ShiLysShuanghuaiPeachAvailable())
            choices.Add(new(new($"shi-lu-yu-sheng.{f.Id}.peach"),
                $"令 {_players[r.TargetSeat].Name} 从弃牌堆中获得一张【桃】", [], [], new Dictionary<string, string>
                { ["program-action"] = "mobile-shi-shuanghuai", ["option"] = "peach" }));
        return Array.AsReadOnly(choices.ToArray());
    }

    private void PublishShiLysShuanghuaiChoice(ProgramSkillFrame f)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId);
        var r = f.MobileShiLuYuSheng!;
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat,
            $"霜怀：选择对 {_players[r.TargetSeat].Name} 受到的伤害的效果。", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = ShiLysShuanghuaiChoices(f),
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    [ProgramAnswer("mobile-shi-shuanghuai")]
    private void AnswerMobileShiShuanghuai(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiLuYuSheng: { Operation: "shuanghuai", Stage: 0 } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([selected], ShiLysShuanghuaiChoices(f).Where(choice => choice.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Shuanghuai choice lost its original frame, chooser or legal option.");
        ClearPendingDecision();
        BindShiLysShuanghuaiOption(f, selected.Parameters["option"]);
        ContinueMobileShiShuanghuai(f.Id);
        AdvanceRulesAndPublishState();
    }

    private void BindShiLysShuanghuaiOption(ProgramSkillFrame f, string option)
    {
        var r = f.MobileShiLuYuSheng!;
        ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = r with { Stage = 9, CardId = option == "peach" ? -1 : 0 },
            ChoiceBindings = Array.AsReadOnly(f.ChoiceBindings.Append(new ProgramChoiceResultBinding("shuanghuai-option",
                option, f.OwnerSeat)).ToArray()) });
        AdvanceEventRulesAndQueueFact(new MobileShiShuanghuaiGuardIssuedEvent(f.Id, ShiLysSource(f), f.GameplayHash,
            _turnNumber, r.TargetSeat, option, SameShiLysShuanghuaiTarget(f)));
    }

    private bool SameShiLysShuanghuaiTarget(ProgramSkillFrame f) =>
        _mobileShiLuYuShengLastGuardVictim.GetValueOrDefault(f.OwnerSeat, -1) == f.MobileShiLuYuSheng!.TargetSeat;

    private void ContinueMobileShiShuanghuai(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiLuYuSheng: { Operation: "shuanghuai" } r } f || f.Id != id) return;
        if (r.Stage == 0)
        {
            var choices = ShiLysShuanghuaiChoices(f);
            if (choices.Count == 0) { FinishProgramSkill(f, true); return; }
            if (choices.Count == 1) { BindShiLysShuanghuaiOption(f, choices[0].Parameters["option"]); ContinueMobileShiShuanghuai(id); return; }
            PublishShiLysShuanghuaiChoice(f);
            return;
        }
        if (r.Stage == 9)
        {
            if (DrainShiLysLuYuSheng(f)) return;
            f = GetActiveProgramFrame(id);
            r = f.MobileShiLuYuSheng!;
            if (r.CardId == 0)
            {
                if (ShiLysShuanghuaiDamagePending(f))
                {
                    PreventProgramCurrentDamage(GetActiveProgramFrame(id));
                    AddLog("SkillEffect", $"{_players[f.OwnerSeat].Name} 霜怀：防止了 {_players[r.TargetSeat].Name} 受到的此伤害。", f.OwnerSeat);
                }
                ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { MobileShiLuYuSheng = r with { Stage = 10 } });
                ContinueMobileShiShuanghuai(id);
                return;
            }
            if (r.CardId == -1 && ShiLysShuanghuaiPeachAvailable() && _players[r.TargetSeat].IsAlive)
            {
                var peach = _cardZones.CardsAt(CardLocation.DiscardPile).Reverse().First(card => card.Kind == CardKind.Peach);
                ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = r with { Stage = 10, CardId = peach.Id },
                    PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                MoveCard(peach, CardLocation.DiscardPile, CardLocation.Hand(r.TargetSeat),
                    new(MobileShiLuYuShengComposition.ShuanghuaiPeachMoveReason));
                AddLog("SkillEffect",
                    $"{_players[f.OwnerSeat].Name} 霜怀：{_players[r.TargetSeat].Name} 从弃牌堆获得一张【桃】。", f.OwnerSeat);
                if (!TryBeginCardsMovedProgramWindow()) ContinueMobileShiShuanghuai(id);
                return;
            }
            ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = r with { Stage = 10 } });
            ContinueMobileShiShuanghuai(id);
            return;
        }
        if (r.Stage == 10)
        {
            if (DrainShiLysLuYuSheng(f)) return;
            f = GetActiveProgramFrame(id);
            r = f.MobileShiLuYuSheng!;
            var same = _mobileShiLuYuShengLastGuardVictim.GetValueOrDefault(f.OwnerSeat, -1) == r.TargetSeat;
            _mobileShiLuYuShengLastGuardVictim[f.OwnerSeat] = r.TargetSeat;
            if (same)
            {
                ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = r with { Stage = 11 } });
                DrawProgramCards(f.Id, f.OwnerSeat, 1, null, null, SkillProgramCardSetVisibility.Private,
                    new(MobileShiLuYuShengComposition.ShuanghuaiDrawMoveReason));
                if (_players[r.TargetSeat].IsAlive)
                    DrawProgramCards(f.Id, r.TargetSeat, 1, null, null, SkillProgramCardSetVisibility.Private,
                        new(MobileShiLuYuShengComposition.ShuanghuaiDrawMoveReason));
                AddLog("SkillEffect",
                    $"{_players[f.OwnerSeat].Name} 霜怀：与上次发动的目标相同，其与 {_players[r.TargetSeat].Name} 各摸一张牌。", f.OwnerSeat);
                ContinueMobileShiShuanghuai(id);
                return;
            }
            ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = r with { Stage = 11 } });
            AddLog("SkillEffect",
                $"{_players[f.OwnerSeat].Name} 霜怀：与上次发动的目标不同，失去 1 点体力。", f.OwnerSeat);
            if (ApplyProgramHpLoss(f.Id, f.SkillId, f.OwnerSeat, 1) == SkillProgramStepOutcome.AwaitChild) return;
            ContinueMobileShiShuanghuai(id);
            return;
        }
        if (r.Stage == 11)
        {
            if (DrainShiLysLuYuSheng(f)) return;
            FinishProgramSkill(GetActiveProgramFrame(id), true);
            return;
        }
        throw new InvalidOperationException("Shuanghuai has an unknown native continuation stage.");
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 67400)]
    private bool ResumeShiLysLuYuSheng(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiLuYuSheng: { } r } f || f.Id != id) return false;
        if (r.Operation == "shuanghuai") ContinueMobileShiShuanghuai(id);
        else if (r.Operation == "runwei") ContinueMobileShiRunwei(id);
        else
        {
            if (DrainShiLysLuYuSheng(f)) return true;
            FinishProgramSkill(GetActiveProgramFrame(id), true);
        }
        return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 67400)]
    private bool ReturnShiLysLuYuShengMovement(ProgramSkillFrame f)
    {
        if (f.MobileShiLuYuSheng is not { Operation: var operation }) return false;
        if (operation == "shuanghuai") ContinueMobileShiShuanghuai(f.Id);
        else if (operation == "runwei") ContinueMobileShiRunwei(f.Id);
        else
        {
            if (DrainShiLysLuYuSheng(f)) return true;
            FinishProgramSkill(GetActiveProgramFrame(f.Id), true);
        }
        return true;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 67400)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 67400)]
    private bool ContinuePaidShiLysLuYuSheng(ProgramSkillFrame f) => f.MobileShiLuYuSheng is { Stage: > 0 };
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 67400)]
    private void AssertShiLysLuYuSheng(ProgramSkillFrame f)
    {
        if (f.MobileShiLuYuSheng is not { } r) return;
        if (r.ActualTurn != _turnNumber || r.Operation is not ("runwei" or "runwei-end" or "shuanghuai"))
            throw new InvalidOperationException("Shi Lu Yusheng lost her original typed trigger or paid stage receipt.");
        if (r.Operation == "runwei" && (f.SkillId != ShiLysRunweiSkill || f.ActivationId != MobileShiLuYuShengComposition.RunweiActivation ||
                r.Stage is not (0 or 1 or 2 or 9) || r.RevealedCount is > 5 ||
                r.Stage > 0 && r.RevealedCount < 1 ||
                r.Stage is 2 or 9 && !IsValidPlayerSeat(r.TargetSeat)))
            throw new InvalidOperationException("Runwei lost its reveal-grant activation receipt.");
        if (r.Operation == "runwei-end" && (f.SkillId != ShiLysRunweiSkill || f.TriggerId != MobileShiLuYuShengComposition.RunweiPhaseEndTrigger ||
                r.Stage != 9 || r.TargetSeat != -1 || r.Discarded < 0))
            throw new InvalidOperationException("Runwei phase end lost its locked trigger receipt.");
        if (r.Operation == "shuanghuai" && (f.SkillId != ShiLysShuanghuaiSkill || f.TriggerId != MobileShiLuYuShengComposition.ShuanghuaiGuardTrigger ||
                !IsValidPlayerSeat(r.TargetSeat) || r.Stage is not (0 or 9 or 10 or 11)))
            throw new InvalidOperationException("Shuanghuai lost its before-damage guard receipt.");
        if (r.Operation == "shuanghuai" && r.Stage > 0 &&
            !ProgramEventHistory<MobileShiShuanghuaiGuardIssuedEvent>().Any(issued => issued.FrameId == f.Id &&
                issued.Source == ShiLysSource(f) && issued.GameplayHash == f.GameplayHash && issued.ActualTurn == r.ActualTurn &&
                issued.VictimSeat == r.TargetSeat))
            throw new InvalidOperationException("Shuanghuai lost its issued guard obligation.");
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiRunweiGrant)]
    private PromptChoice ChooseShiLysRunwei(PendingDecision prompt) =>
        prompt.Choices.OrderByDescending(choice => choice.Parameters.TryGetValue("count", out var count) && int.TryParse(count, out var parsed) ? parsed : 0).FirstOrDefault() ?? prompt.Choices[0];
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiShuanghuaiGuard)]
    private PromptChoice ChooseShiLysShuanghuai(PendingDecision prompt) => prompt.Choices[0];

    private bool IsMobileShiLuYuShengChoiceResult(ProgramSkillFrame frame, ProgramChoiceResultBinding binding) =>
        frame.MobileShiLuYuSheng is { } r && binding.ChooserSeat == frame.OwnerSeat &&
        (binding.Name == "runwei-recipient" && r.Operation == "runwei" && r.Stage >= 2 && r.TargetSeat >= 0 &&
            binding.OptionId == r.TargetSeat.ToString(CultureInfo.InvariantCulture) ||
         binding.Name == "shuanghuai-option" && r.Operation == "shuanghuai" && r.Stage > 0 &&
            binding.OptionId is ("prevent" or "peach"));
}
