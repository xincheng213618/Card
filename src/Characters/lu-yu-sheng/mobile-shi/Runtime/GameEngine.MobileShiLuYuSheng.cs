using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShiLysRunweiSkill = "mobile:shi-runwei", ShiLysShuanghuaiSkill = "mobile:shi-shuanghuai";

    private static CardConversionSource ShiLysSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);

    // This ledger separates the actual phase's one refund from the turn-wide
    // recipient history. Recovery rebuilds it from committed facts and movements.
    private sealed record MobileShiLuYuShengTrack(ActualDiscardRecoveryPhaseKey? Phase,
        IReadOnlyList<int> GainedSeats, IReadOnlyList<int> MarkedCards, int DebtRequired, int DebtProgress, bool Refunded);
    private MobileShiLuYuShengTrack MobileShiLuYuShengTrackAt(int seat)
    {
        var phase = CurrentActualDiscardRecoveryPhase();
        var all = ProgramEventHistory<MobileShiRunweiGrantIssuedEvent>().Where(e => e.Source.OwnerSeat == seat && e.ActualTurn == _turnNumber).ToArray();
        var grant = all.FirstOrDefault(e => e.Phase == phase && e.GainedCount > 0);
        var marked = ProgramEventHistory<MobileShiRunweiCardGrantedEvent>().Where(e => e.OwnerSeat == seat && e.TargetSeat == seat && e.Phase == phase).Select(e => e.CardId).Distinct().ToArray();
        var refunded = phase is not null && ProgramEventHistory<MobileShiRunweiDebtRefundedEvent>().Any(e => e.Source.OwnerSeat == seat && e.Phase == phase);
        var progress = grant is null ? 0 : _cardMovements.Count(m => m.Sequence > grant.SequenceAfter && m.TurnNumber == _turnNumber && m.From.OwnerSeat == seat && m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment);
        return new(phase, all.Where(e => e.GainedCount > 0).Select(e => e.TargetSeat).Distinct().ToArray(), marked, grant?.GainedCount ?? 0, progress, refunded);
    }
    private MobileShiLuYuShengTrack? PeekMobileShiLuYuSheng(int seat) => MobileShiLuYuShengTrackAt(seat);
    private MobileShiRunweiGrantIssuedEvent? ShiLysRunweiGrant(long id) => ProgramEventHistory<MobileShiRunweiGrantIssuedEvent>().SingleOrDefault(e => e.FrameId == id);
    private IReadOnlyList<ActualPhaseEndItem> CollectShiLysPhaseEndCandidates(ActualDiscardRecoveryPhaseKey phase) =>
        ProgramEventHistory<MobileShiRunweiGrantIssuedEvent>().Where(e => e.Phase == phase && e.TargetSeat == e.Source.OwnerSeat && e.GainedCount > 0 && _players[e.Source.OwnerSeat].IsAlive)
            .GroupBy(e => (e.Source.OwnerSeat, e.Source.SkillInstanceId)).Select(g => g.First())
            .Where(e => !ProgramEventHistory<MobileShiRunweiPhaseEndConsumedEvent>().Any(c => c.GrantId == e.FrameId && c.Phase == phase))
            .Select(e => new ActualPhaseEndItem(new(e.Source.OwnerSeat, ShiLysRunweiSkill, MobileShiLuYuShengComposition.RunweiPhaseEndTrigger,
                e.Source.SkillInstanceId, e.GameplayHash, 0), e.FrameId)).ToArray();
    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 67400)]
    private bool IsIssuedShiLysPhaseEnd(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        c.SkillId == ShiLysRunweiSkill && c.BindingId == MobileShiLuYuShengComposition.RunweiPhaseEndTrigger &&
        IsExactActualPhaseEndCandidate(c, context) && context.ActualPhaseEnd is { } due && ShiLysRunweiGrant(due.EntitlementId) is { } grant &&
        grant.Source.OwnerSeat == c.OwnerSeat && grant.Source.SkillInstanceId == c.SkillInstanceId && grant.GameplayHash == c.GameplayHash &&
        grant.Phase == due.Phase && grant.TargetSeat == c.OwnerSeat && grant.GainedCount > 0;

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
            if (trigger.Id == MobileShiLuYuShengComposition.RunweiLossDebtTrigger) return _phase == TurnPhase.Play && track is { DebtRequired: > 0, Refunded: false } && context.MovementBatch?.DiscardRecoveryPhase == track.Phase;
            if (trigger.Id == MobileShiLuYuShengComposition.RunweiPhaseEndTrigger) return IsIssuedShiLysPhaseEnd(candidate, context);
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
        return _players.Where(p => p.IsAlive && (track is null || !track.Refunded || !track.GainedSeats.Contains(p.Seat)))
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
        var phase = CurrentActualDiscardRecoveryPhase();
        if (phase is not { Kind: ActualDiscardRecoveryPhaseKind.Play } || phase.ActorSeat != f.OwnerSeat) throw new InvalidOperationException("Runwei needs its original actual Play phase.");
        ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = new("runwei", _turnNumber) { Source = ShiLysSource(f), GameplayHash = f.GameplayHash, Phase = phase } });
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
            if (counts.Length == 0) { FinishShiLys(f); return; }
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
            FinishShiLys(GetActiveProgramFrame(id));
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
        StampShiLysBefore(f.Id);
        if (leftovers.Length > 0) MoveProgramCardsFromMultipleSources(leftovers.Select(p => p.Card.Id).ToArray(), CardLocation.DiscardPile,
            new(MobileShiLuYuShengComposition.RunweiLeftoverMoveReason));
        SealShiLys(f.Id); ContinueMobileShiRunwei(f.Id);
    }

    private void ApplyMobileShiRunweiGrant(ProgramSkillFrame f, bool red)
    {
        var r = f.MobileShiLuYuSheng!; var cards = ShiLysRunweiRevealedCards(f);
        var matched = cards.Where(pair => ShiLysIsRed(pair.Card) == red).Select(p => p.Card.Id).ToArray();
        var leftovers = cards.Where(pair => ShiLysIsRed(pair.Card) != red).Select(p => p.Card.Id).ToArray();
        if (r.Phase is null || !MobileShiRunweiRecipientSeats(f).Contains(r.TargetSeat) || matched.Length == 0)
            throw new InvalidOperationException("Runwei lost its original phase, legal recipient or chosen real color group.");
        ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = r with { Stage = 9, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources(matched, CardLocation.Hand(r.TargetSeat), new(MobileShiLuYuShengComposition.RunweiGrantMoveReason), (batch, moves) =>
        {
            var gained = moves.Where(m => m.To == CardLocation.Hand(r.TargetSeat)).ToArray();
            foreach (var m in gained) AdvanceEventRulesAndQueueFact(new MobileShiRunweiCardGrantedEvent(f.Id, f.OwnerSeat, r.TargetSeat, m.CardId, r.Phase));
            AdvanceEventRulesAndQueueFact(new MobileShiRunweiGrantIssuedEvent(f.Id, ShiLysSource(f), f.GameplayHash, _turnNumber, r.TargetSeat,
                cards.Length, gained.Length, red) { Phase = r.Phase, SequenceAfter = moves.Max(m => m.Sequence) });
        });
        if (leftovers.Length > 0) MoveProgramCardsFromMultipleSources(leftovers, CardLocation.DiscardPile, new(MobileShiLuYuShengComposition.RunweiLeftoverMoveReason));
        AddLog("SkillEffect", $"{_players[f.OwnerSeat].Name} 润微：展示 {cards.Length} 张，令 {_players[r.TargetSeat].Name} 获得其中{(red ? "红色" : "黑色")}牌。", f.OwnerSeat, r.TargetSeat);
        SealShiLys(f.Id); ContinueMobileShiRunwei(f.Id);
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

    private SkillProgramStepOutcome ResolveMobileShiRunweiLossDebt(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id); var track = MobileShiLuYuShengTrackAt(f.OwnerSeat);
        if (f.SkillId != ShiLysRunweiSkill || f.TriggerId != MobileShiLuYuShengComposition.RunweiLossDebtTrigger || f.WindowContext?.MovementBatch is not { } batch)
            throw new InvalidOperationException("Runwei refund needs the exact native owner-loss batch.");
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || _phase != TurnPhase.Play || batch.DiscardRecoveryPhase != track.Phase ||
            track.Phase?.ActorSeat != f.OwnerSeat || track.Refunded || track.DebtRequired <= 0 || track.DebtProgress < track.DebtRequired)
            return SkillProgramStepOutcome.Continue;
        RefundMobileShiRunweiPhaseUse(f.OwnerSeat);
        AdvanceEventRulesAndQueueFact(new MobileShiRunweiDebtRefundedEvent(f.Id, ShiLysSource(f), f.GameplayHash, _turnNumber, track.DebtRequired, track.DebtProgress) { Phase = track.Phase });
        AddLog("SkillEffect", $"{_players[f.OwnerSeat].Name} 润微：本阶段失去牌数已达到 X={track.DebtRequired}，本阶段唯一一次重新发动机会已解锁。", f.OwnerSeat);
        return SkillProgramStepOutcome.Continue;
    }

    private SkillProgramStepOutcome ResolveMobileShiRunweiPhaseEnd(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0);
        if (f.WindowContext is not { } context || !IsIssuedShiLysPhaseEnd(candidate, context) || context.ActualPhaseEnd is not { } due ||
            ShiLysRunweiGrant(due.EntitlementId) is not { Phase: { } phase } grant)
            throw new InvalidOperationException("Runwei cleanup requires its original issued actual-phase grant.");
        var marked = ProgramEventHistory<MobileShiRunweiCardGrantedEvent>().Where(e => e.OwnerSeat == f.OwnerSeat && e.TargetSeat == f.OwnerSeat && e.Phase == phase &&
            ShiLysRunweiGrant(e.FrameId)?.Source.SkillInstanceId == f.SkillInstanceId).Select(e => e.CardId).Distinct().ToArray();
        var allowed = GetSelfDiscardableOwnedHand(_players[f.OwnerSeat]).Select(c => c.Id).ToHashSet();
        var hands = marked.Where(id => _cardZones.GetLocation(id) == CardLocation.Hand(f.OwnerSeat) && allowed.Contains(id)).ToArray();
        ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = new("runwei-end", _turnNumber, Stage: 9, Discarded: hands.Length)
            { Source = grant.Source, GameplayHash = grant.GameplayHash, Phase = phase, GrantId = grant.FrameId, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MobileShiRunweiPhaseEndConsumedEvent(f.Id, grant.FrameId, phase));
        AdvanceEventRulesAndQueueFact(new MobileShiRunweiPhaseEndDiscardedEvent(f.Id, grant.Source, grant.GameplayHash, _turnNumber, hands.Length, marked.Length - hands.Length) { Phase = phase });
        if (hands.Length > 0) MoveProgramLogicalDiscard(f, f.OwnerSeat, hands, new(MobileShiLuYuShengComposition.RunweiPhaseEndMoveReason));
        SealShiLys(f.Id); ResumeShiLysLuYuSheng(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private void StampShiLysBefore(long id)
    { var f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { MobileShiLuYuSheng = f.MobileShiLuYuSheng! with { Before = _movementSequence, After = _movementSequence } }); }
    private void SealShiLys(long id)
    { var f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { MobileShiLuYuSheng = f.MobileShiLuYuSheng! with { After = _movementSequence } }); }

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
        ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = new("shuanghuai", _turnNumber, TargetSeat: victim) { Source = ShiLysSource(f), GameplayHash = f.GameplayHash } });
        ContinueMobileShiShuanghuai(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private BeforeDamageProgramWindowFrame? ShiLysGuardParent(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied, TargetSeat: { } victim, Amount: > 0 } c ||
            _resolutionStack.SingleOrDefault(x => x.Id == c.ParentFrameId) is not BeforeDamageProgramWindowFrame w || w.TargetSeat != victim || w.Amount != c.Amount ||
            w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || w.Candidates[w.CandidateIndex].Candidate is not { } candidate ||
            candidate.OwnerSeat != f.OwnerSeat || candidate.SkillId != f.SkillId || candidate.BindingId != f.TriggerId || candidate.SkillInstanceId != f.SkillInstanceId || candidate.GameplayHash != f.GameplayHash)
            return null;
        return w;
    }
    private bool ShiLysShuanghuaiDamagePending(ProgramSkillFrame f) => ShiLysGuardParent(f) is { Prevented: false } w &&
        CurrentDamageAttempt is { } attempt && attempt.TargetSeat == f.MobileShiLuYuSheng!.TargetSeat &&
        attempt.ResolutionId == (w.ContinuationAttackResolutionId ?? w.ParentFrameId);

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
        ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = r with { Stage = 9, CardId = option == "peach" ? -1 : 0, SameAsLast = SameShiLysShuanghuaiTarget(f) },
            ChoiceBindings = Array.AsReadOnly(f.ChoiceBindings.Append(new ProgramChoiceResultBinding("shuanghuai-option",
                option, f.OwnerSeat)).ToArray()) });
        AdvanceEventRulesAndQueueFact(new MobileShiShuanghuaiGuardIssuedEvent(f.Id, ShiLysSource(f), f.GameplayHash,
            _turnNumber, r.TargetSeat, option, SameShiLysShuanghuaiTarget(f)));
    }

    private bool SameShiLysShuanghuaiTarget(ProgramSkillFrame f) => ProgramEventHistory<MobileShiShuanghuaiGuardIssuedEvent>()
        .LastOrDefault(e => e.Source.OwnerSeat == f.OwnerSeat && e.FrameId != f.Id)?.VictimSeat == f.MobileShiLuYuSheng!.TargetSeat;

    private void ContinueMobileShiShuanghuai(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiLuYuSheng: { Operation: "shuanghuai" } r } f || f.Id != id) return;
        if (r.Stage == 0)
        {
            var choices = ShiLysShuanghuaiChoices(f);
            if (choices.Count == 0) { FinishShiLys(f); return; }
            if (choices.Count == 1) { BindShiLysShuanghuaiOption(f, choices[0].Parameters["option"]); ContinueMobileShiShuanghuai(id); return; }
            PublishShiLysShuanghuaiChoice(f); return;
        }
        if (r.Stage == 9)
        {
            ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = r = r with { Stage = 10, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            if (r.CardId == 0)
            { if (ShiLysShuanghuaiDamagePending(f)) PreventProgramCurrentDamage(f); }
            else if (ShiLysShuanghuaiPeachAvailable() && _players[r.TargetSeat].IsAlive)
            {
                var peach = _cardZones.CardsAt(CardLocation.DiscardPile).Reverse().First(c => c.Kind == CardKind.Peach);
                ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = r with { CardId = peach.Id } });
                MoveProgramCardsFromMultipleSources([peach.Id], CardLocation.Hand(r.TargetSeat), new(MobileShiLuYuShengComposition.ShuanghuaiPeachMoveReason));
            }
            SealShiLys(id); ContinueMobileShiShuanghuai(id); return;
        }
        if (r.Stage is 10 or 11 or 13)
        {
            if (DrainShiLysLuYuSheng(f)) return;
            ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null }); r = f.MobileShiLuYuSheng!;
            if (r.Stage == 11) { FinishShiLys(f); return; }
            if (r.SameAsLast)
            {
                if (r.DrawCursor >= 2 || _winner != Winner.None) { FinishShiLys(f); return; }
                var recipient = r.DrawCursor == 0 ? f.OwnerSeat : r.TargetSeat;
                if (!_players[recipient].IsAlive)
                { ReplaceRuntimeTop(f with { MobileShiLuYuSheng = r with { Stage = 13, DrawCursor = r.DrawCursor + 1 } }); ContinueMobileShiShuanghuai(id); return; }
                ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = r with { Stage = 12, Before = _movementSequence, After = _movementSequence } });
                BeginOperationProgramDraw(f, recipient, 1, new(MobileShiLuYuShengComposition.ShuanghuaiDrawMoveReason)); return;
            }
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishShiLys(f); return; }
            if (_players[f.OwnerSeat].IdentityConcealed)
            {
                ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = r with { Stage = 11, HpAppearance = true } });
                ApplyProgramHpLoss(id, f.SkillId, f.OwnerSeat, 1); return;
            }
            var result = ApplyProgramHpLoss(id, f.SkillId, f.OwnerSeat, 1, (before, after, lost) =>
            {
                var active = GetActiveProgramFrame(id);
                ReplaceRuntimeTop(active with { MobileShiLuYuSheng = active.MobileShiLuYuSheng! with { Stage = 11, HpPaid = true } });
                AdvanceEventRulesAndQueueFact(new MobileShiShuanghuaiHpPaidEvent(id, f.OwnerSeat, before, after));
            });
            if (result == SkillProgramStepOutcome.AwaitChild) return;
            ContinueMobileShiShuanghuai(id); return;
        }
        if (r.Stage == 12) return;
        throw new InvalidOperationException("Shuanghuai has an unknown native continuation stage.");
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMobileShiShuanghuaiGuard)]
    private void ReturnShiLysShuanghuaiDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.MobileShiLuYuSheng is not { Operation: "shuanghuai", Stage: 12, SameAsLast: true } r || outcome.RequestedCount != 1 ||
            outcome.RecipientSeat != (r.DrawCursor == 0 ? f.OwnerSeat : r.TargetSeat) || r.DrawCursor is < 0 or > 1 ||
            ProgramEventHistory<MobileShiShuanghuaiDrawReturnedEvent>().Any(e => e.FrameId == f.Id && e.Cursor == r.DrawCursor))
            throw new InvalidOperationException("Shuanghuai draw lost its exact once-issued real recipient or native return.");
        ReplaceRuntimeTop(f = f with { MobileShiLuYuSheng = r with { Stage = 13, DrawCursor = r.DrawCursor + 1, Before = outcome.SequenceBefore, After = outcome.SequenceAfter },
            PendingMovementContinuation = new(outcome.RecipientSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MobileShiShuanghuaiDrawReturnedEvent(f.Id, r.DrawCursor, outcome.RecipientSeat, outcome.ActualDrawIds.Count));
        ContinueMobileShiShuanghuai(f.Id);
    }
    private void FinishShiLys(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { PendingMovementContinuation = null }); FinishProgramSkill(f, true); }

    [ProgramContinuation(ProgramContinuationKind.Resume, 67400)]
    private bool ResumeShiLysLuYuSheng(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiLuYuSheng: { } r } f || f.Id != id) return false;
        if (r.Operation == "shuanghuai") ContinueMobileShiShuanghuai(id);
        else if (r.Operation == "runwei") ContinueMobileShiRunwei(id);
        else
        {
            if (DrainShiLysLuYuSheng(f)) return true;
            FinishShiLys(GetActiveProgramFrame(id));
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
            FinishShiLys(GetActiveProgramFrame(f.Id));
        }
        return true;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 67400)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 67400)]
    private bool ContinuePaidShiLysLuYuSheng(ProgramSkillFrame f) => f.MobileShiLuYuSheng is { Stage: > 0 } ||
        f.InstructionIndex == 0 && f.TriggerId == MobileShiLuYuShengComposition.RunweiPhaseEndTrigger && f.WindowContext is { } c &&
        IsIssuedShiLysPhaseEnd(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0), c);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 67400)]
    private void AssertShiLysLuYuSheng(ProgramSkillFrame f)
    {
        if (f.MobileShiLuYuSheng is not { } r) return;
        if (r.Source is null || r.Source.OwnerSeat != f.OwnerSeat || r.Source.SkillId != f.SkillId || r.Source.SkillInstanceId != f.SkillInstanceId || r.GameplayHash != f.GameplayHash || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.ActualTurn != _turnNumber || r.Operation is not ("runwei" or "runwei-end" or "shuanghuai"))
            throw new InvalidOperationException("Shi Lu Yusheng lost her original typed trigger or paid stage receipt.");
        if (r.Operation != "runwei-end" && r.Source != ShiLysSource(f)) throw new InvalidOperationException("Shi Lu Yusheng changed its exact original binding source.");
        if (r.Operation == "runwei" && (r.Phase is not { Kind: ActualDiscardRecoveryPhaseKind.Play } || r.Phase.ActorSeat != f.OwnerSeat || f.SkillId != ShiLysRunweiSkill || f.ActivationId != MobileShiLuYuShengComposition.RunweiActivation ||
                r.Stage is not (0 or 1 or 2 or 9) || r.RevealedCount is > 5 ||
                r.Stage > 0 && r.RevealedCount < 1 ||
                r.Stage is 2 or 9 && !IsValidPlayerSeat(r.TargetSeat)))
            throw new InvalidOperationException("Runwei lost its reveal-grant activation receipt.");
        if (r.Operation == "runwei-end" && (f.SkillId != ShiLysRunweiSkill || f.TriggerId != MobileShiLuYuShengComposition.RunweiPhaseEndTrigger ||
                r.Stage != 9 || r.TargetSeat != -1 || r.Discarded < 0))
            throw new InvalidOperationException("Runwei phase end lost its locked trigger receipt.");
        if (r.Operation == "runwei-end" && (r.Phase is null || f.WindowContext?.ActualPhaseEnd?.EntitlementId != r.GrantId || ShiLysRunweiGrant(r.GrantId) is not { } grant || grant.Phase != r.Phase || grant.Source != r.Source))
            throw new InvalidOperationException("Runwei cleanup lost its exact original granted phase obligation.");
        if (r.Operation == "shuanghuai" && (f.SkillId != ShiLysShuanghuaiSkill || f.TriggerId != MobileShiLuYuShengComposition.ShuanghuaiGuardTrigger ||
                !IsValidPlayerSeat(r.TargetSeat) || ShiLysGuardParent(f) is null || f.WindowContext?.TargetSeat != r.TargetSeat || r.Stage is not (0 or 9 or 10 or 11 or 12 or 13)))
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
    private IReadOnlyList<ProgramPublicRuleStateSnapshot> MobileShiLuYuShengPublicStates(CharacterState owner, string skillId)
    {
        if (skillId is not (ShiLysRunweiSkill or ShiLysShuanghuaiSkill)) return [];
        var result = new List<ProgramPublicRuleStateSnapshot>();
        foreach (var instance in GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == skillId))
        {
            if (skillId == ShiLysRunweiSkill)
            {
                var state = MobileShiLuYuShengTrackAt(owner.Seat);
                result.Add(new(instance.SkillInstanceId, "runwei-required", ProgramPublicRuleStateKind.GameActivationProgress, state.DebtRequired));
                result.Add(new(instance.SkillInstanceId, "runwei-progress", ProgramPublicRuleStateKind.GameActivationProgress, Math.Min(state.DebtRequired, state.DebtProgress)));
                result.Add(new(instance.SkillInstanceId, "runwei-refunded", ProgramPublicRuleStateKind.GameActivationProgress, state.Refunded ? 1 : 0));
                result.Add(new(instance.SkillInstanceId, "runwei-own-hand", ProgramPublicRuleStateKind.GameActivationProgress, state.MarkedCards.Count(id => _cardZones.GetLocation(id) == CardLocation.Hand(owner.Seat))));
            }
            else
            {
                var last = ProgramEventHistory<MobileShiShuanghuaiGuardIssuedEvent>().LastOrDefault(e => e.Source.OwnerSeat == owner.Seat);
                result.Add(new(instance.SkillInstanceId, "shuanghuai-last-target", ProgramPublicRuleStateKind.GameActivationProgress, (last?.VictimSeat ?? -1) + 1));
                result.Add(new(instance.SkillInstanceId, "shuanghuai-turn-used", ProgramPublicRuleStateKind.GameActivationProgress,
                    _skillRuntimeState.GetUsage(owner.Seat, skillId, MobileShiLuYuShengComposition.ShuanghuaiGuardTrigger + "@" + instance.SkillInstanceId, SkillUsageScope.Turn)));
            }
        }
        return result.ToArray();
    }

}
