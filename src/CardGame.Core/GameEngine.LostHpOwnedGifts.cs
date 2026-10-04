using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string LostHpGiftDrawReason = "program.lost-hp-owned-gift.draw";
    private const string LostHpGiftMoveReason = "program.lost-hp-owned-gift.give";
    private long PaidTargetMovementSequence => _cardMovements.Count == 0 ? 0 : _cardMovements[^1].Sequence;
    private SkillProgramStepOutcome AwaitLostHpOwnedGiftMovements(long frameId)
    {
        var current = GetActiveProgramFrame(frameId);
        ReplaceRuntimeTop(current with { PendingMovementContinuation = new(current.OwnerSeat, 0, null) });
        if (TryBeginQueuedRecoveryReplacement(frameId, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginHpChangedProgramWindow(frameId, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(frameId)) return SkillProgramStepOutcome.AwaitChild;
        ReplaceRuntimeTop(GetActiveProgramFrame(frameId) with { PendingMovementContinuation = null });
        return SkillProgramStepOutcome.Continue;
    }

    private SkillProgramStepOutcome RunLostHpOwnedGift(ProgramSkillFrame frame)
    {
        frame = GetActiveProgramFrame(frame.Id);
        if (!MatchesLostHpOwnedGiftEnding(frame)) throw new InvalidOperationException("Lost-HP gifting requires its exact actual Ending item.");
        if (frame.LostHpOwnedGift is null)
        {
            var x = Math.Max(0, _players[frame.OwnerSeat].MaxHp - Math.Max(0, _players[frame.OwnerSeat].Hp));
            var before = PaidTargetMovementSequence;
            DrawProgramCards(frame.Id, frame.OwnerSeat, x, null, null, SkillProgramCardSetVisibility.Private, new(LostHpGiftDrawReason));
            var after = PaidTargetMovementSequence;
            var drawn = _cardMovements.Count(m => m.Sequence > before && m.Sequence <= after && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(frame.OwnerSeat) && m.Reason.Value == LostHpGiftDrawReason);
            frame = GetActiveProgramFrame(frame.Id);
            ReplaceRuntimeTop(frame with { ReexecuteParticipantInstruction = true,
                LostHpOwnedGift = new(0, new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId),
                    frame.GameplayHash, _turnNumber, _currentSeat, x, drawn, before, after, LostHpOwnedGiftStage.Drawing, []) });
            AdvanceEventRulesAndQueueFact(new LostHpDrawGiftFrozenEvent(frame.Id,
                new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId), frame.GameplayHash,
                _turnNumber, _currentSeat, x, drawn));
            return AwaitLostHpOwnedGiftMovements(frame.Id);
        }
        if (!IsValidLostHpOwnedGift(frame)) throw new InvalidOperationException("Lost-HP gifting lost its committed draw/payment ledger.");
        if (frame.LostHpOwnedGift.Stage == LostHpOwnedGiftStage.Complete) return SkillProgramStepOutcome.Continue;
        if (_winner != Winner.None) { FinishLostHpOwnedGift(frame); return SkillProgramStepOutcome.Continue; }
        frame = frame with { LostHpOwnedGift = frame.LostHpOwnedGift with { Stage = LostHpOwnedGiftStage.Offering } };
        ReplaceRuntimeTop(frame);
        return PublishLostHpOwnedGift(frame);
    }
    private SkillProgramStepOutcome PublishLostHpOwnedGift(ProgramSkillFrame frame)
    {
        var gift = frame.LostHpOwnedGift!;
        var cards = new[] { CardZoneKind.Hand, CardZoneKind.Equipment }.SelectMany(zone =>
            _cardZones.CardsAt(new(zone, frame.OwnerSeat)).Where(c => !gift.PaidCardIds.Contains(c.Id)).Select(c => (Card: c, Zone: zone))).ToArray();
        var targets = _players.Where(p => p.IsAlive && p.Seat != frame.OwnerSeat).Select(p => p.Seat).ToArray();
        if (gift.PaidCardIds.Count >= gift.MaximumGiftCount || cards.Length == 0 || targets.Length == 0)
        { FinishLostHpOwnedGift(frame); return SkillProgramStepOutcome.Continue; }
        var choices = cards.SelectMany(item => targets.Select(target => new PromptChoice(new ChoiceId(
            $"lost-hp-gift.{frame.Id}.{gift.PaidCardIds.Count}.{item.Card.Id}.{target}"),
            $"交给 {_players[target].Name} 一张【{item.Card.DisplayName}】。", [item.Card.Id], [target],
            new Dictionary<string, string> { ["program-action"] = "limited-owned-gift", ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture),
                ["gift-index"] = gift.PaidCardIds.Count.ToString(CultureInfo.InvariantCulture), ["source-zone"] = item.Zone.ToString() }))).ToList();
        choices.Add(new(new ChoiceId($"lost-hp-gift.{frame.Id}.finish"), "结束分配。", [], [], new Dictionary<string, string>
        { ["program-action"] = "finish-limited-owned-gift", ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture),
            ["gift-index"] = gift.PaidCardIds.Count.ToString(CultureInfo.InvariantCulture) }));
        ReplaceRuntimeTop(frame with { ReexecuteParticipantInstruction = true });
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, $"还可以交出至多 {gift.MaximumGiftCount - gift.PaidCardIds.Count} 张自己的牌。",
            cards.Select(c => c.Card.Id).ToArray(), targets, SourceSeat: frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = frame.OwnerSeat,
            SkillPrompt = new(frame.SkillId, skill.Name, $"{skill.Name} · 分配卡牌", skill.Description), Choices = Array.AsReadOnly(choices.ToArray()) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanCardSelection : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private void FinishLostHpOwnedGift(ProgramSkillFrame frame)
    {
        var receipt = frame.LostHpOwnedGift!;
        ReplaceRuntimeTop(frame with { ReexecuteParticipantInstruction = false, LostHpOwnedGift = receipt with { Stage = LostHpOwnedGiftStage.Complete } });
        AdvanceEventRulesAndQueueFact(new LostHpOwnedGiftFinishedEvent(frame.Id, receipt.PaidCardIds.Count, receipt.DeliveredCount, receipt.MaximumGiftCount, true));
    }
    private void ResolveLostHpOwnedGift(ProgramSkillFrame frame, SkillProgramEffect effect, PromptChoice choice)
    {
        var gift = frame.LostHpOwnedGift ?? throw new InvalidOperationException("Missing limited owned gift.");
        if (effect.Op != SkillProgramEffectOp.DrawLostHpThenOfferOwnedCardsUpTo || gift.Stage != LostHpOwnedGiftStage.Offering ||
            choice.Parameters.GetValueOrDefault("gift-index") != gift.PaidCardIds.Count.ToString(CultureInfo.InvariantCulture) ||
            !IsValidLostHpOwnedGift(frame)) throw new InvalidOperationException("A limited gift must match its exact suspended producer.");
        if (choice.Parameters.GetValueOrDefault("program-action") == "finish-limited-owned-gift")
        {
            if (choice.Cards.Count != 0 || choice.Targets.Count != 0) throw new InvalidOperationException("Finishing a gift cannot pay a card.");
            ClearPendingDecision(); FinishLostHpOwnedGift(frame); AdvanceRuntimeProgram(frame.Id); return;
        }
        if (choice.Cards is not [var id] || choice.Targets is not [var target] || !IsValidPlayerSeat(target) ||
            target == frame.OwnerSeat || !_players[target].IsAlive || gift.PaidCardIds.Count >= gift.MaximumGiftCount ||
            gift.PaidCardIds.Contains(id) || !Enum.TryParse<CardZoneKind>(choice.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
            zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) throw new InvalidOperationException("Invalid limited gift card or recipient.");
        var from = new CardLocation(zone, frame.OwnerSeat);
        if (_cardZones.GetLocation(id) != from) throw new InvalidOperationException("The gift entity has left its frozen owned zone.");
        var card = _cardZones.CardsAt(from).Single(c => c.Id == id);
        var before = PaidTargetMovementSequence;
        ClearPendingDecision();
        MoveCard(card, from, CardLocation.Processing, new(LostHpGiftMoveReason));
        MoveProcessingCardUnlessDestroyed(card, CardLocation.Hand(target), new(LostHpGiftMoveReason));
        // Native Silver Lion removal can append a queued recovery to this owning frame.
        // Merge the paid receipt into that current frame, never the pre-movement copy.
        frame = GetActiveProgramFrame(frame.Id);
        var after = PaidTargetMovementSequence;
        var delivered = _cardMovements.Any(m => m.Sequence > before && m.Sequence <= after && m.CardId == id &&
            m.From == CardLocation.Processing && m.To == CardLocation.Hand(target) && m.Reason.Value == LostHpGiftMoveReason);
        var ids = Array.AsReadOnly(gift.PaidCardIds.Append(id).ToArray());
        gift = gift with { Stage = LostHpOwnedGiftStage.Moving, PaidCardIds = ids,
            DeliveredCount = gift.DeliveredCount + (delivered ? 1 : 0), LastPayment = new(id, from, target, before, after, delivered) };
        ReplaceRuntimeTop(frame with { LostHpOwnedGift = gift, ReexecuteParticipantInstruction = true });
        AdvanceEventRulesAndQueueFact(new LostHpOwnedCardGivenEvent(frame.Id, gift.Source, id, from, target,
            ids.Count, gift.DeliveredCount, gift.MaximumGiftCount, before, after));
        if (AwaitLostHpOwnedGiftMovements(frame.Id) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
    }
    private PromptChoice SelectAiLostHpOwnedGift(PendingDecision decision, ProgramSkillFrame frame)
    {
        var stop = decision.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "finish-limited-owned-gift");
        var owner = _players[frame.OwnerSeat];
        var view = CreateSnapshot(owner.Seat);
        var hint = new SkillProgramAiHint(0, 0, 0, 1, 0, 0, true, false);
        return decision.Choices.Where(c => c.Parameters.GetValueOrDefault("program-action") == "limited-owned-gift")
            .Select(c => (Choice: c, Value: _aiBrains[owner.Seat].ScoreProgramTarget(view, c.Targets.Single(), hint)))
            .Where(c => c.Value > 0).OrderByDescending(c => c.Value)
            .ThenBy(c => GetKeepValue(_cardZones.CardsAt(new(Enum.Parse<CardZoneKind>(c.Choice.Parameters["source-zone"]), owner.Seat))
                .Single(card => card.Id == c.Choice.Cards.Single()), owner))
            .ThenBy(c => view.Players.Single(p => p.Seat == c.Choice.Targets.Single()).HandCount)
            .ThenBy(c => c.Choice.Targets.Single()).ThenBy(c => c.Choice.Cards.Single()).Select(c => c.Choice).FirstOrDefault() ?? stop;
    }
    private bool IsValidLostHpOwnedGift(ProgramSkillFrame root)
    {
        if (root.LostHpOwnedGift is not { } gift || !MatchesLostHpOwnedGiftEnding(root) || gift.InstructionIndex != 0 ||
            root.InstructionIndex != 1 || gift.Source != new CardConversionSource(root.SkillId, GetProgramBindingId(root), root.OwnerSeat, root.SkillInstanceId) ||
            gift.GameplayHash != root.GameplayHash || gift.ActualTurnNumber != _turnNumber || gift.ActualTurnOwnerSeat != _currentSeat ||
            gift.MaximumGiftCount < 0 || gift.ActualDrawCount < 0 || gift.ActualDrawCount > gift.MaximumGiftCount ||
            gift.DrawSequenceBefore < 0 || gift.DrawSequenceAfter < gift.DrawSequenceBefore || gift.PaidCardIds.Count > gift.MaximumGiftCount ||
            gift.PaidCardIds.Distinct().Count() != gift.PaidCardIds.Count || gift.DeliveredCount < 0 || gift.DeliveredCount > gift.PaidCardIds.Count) return false;
        if (CompleteProgramEventHistory().OfType<LostHpDrawGiftFrozenEvent>().Count(e => e.ProgramFrameId == root.Id && e.Source == gift.Source &&
            e.GameplayHash == gift.GameplayHash && e.ActualTurnNumber == gift.ActualTurnNumber && e.ActualTurnOwnerSeat == gift.ActualTurnOwnerSeat &&
            e.MaximumGiftCount == gift.MaximumGiftCount && e.ActualDrawCount == gift.ActualDrawCount) != 1) return false;
        var draws = _cardMovements.Where(m => m.Sequence > gift.DrawSequenceBefore && m.Sequence <= gift.DrawSequenceAfter).ToArray();
        bool ActualDraw(CardMovementRecord m) => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(root.OwnerSeat) && m.Reason.Value == LostHpGiftDrawReason;
        if (draws.Count(ActualDraw) != gift.ActualDrawCount || draws.Any(m => !ActualDraw(m) &&
            !(m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle))) return false;
        var given = CompleteProgramEventHistory().OfType<LostHpOwnedCardGivenEvent>().Where(e => e.ProgramFrameId == root.Id).ToArray();
        if (!given.Select(e => e.CardId).SequenceEqual(gift.PaidCardIds) || given.Any(e => e.Source != gift.Source ||
            e.From.OwnerSeat != root.OwnerSeat || e.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            e.MaximumGiftCount != gift.MaximumGiftCount || !IsValidPlayerSeat(e.RecipientSeat) || e.RecipientSeat == root.OwnerSeat ||
            e.SequenceBefore < gift.DrawSequenceAfter || e.SequenceAfter <= e.SequenceBefore ||
            _cardMovements.Count(m => m.Sequence > e.SequenceBefore && m.Sequence <= e.SequenceAfter && m.CardId == e.CardId &&
                m.From == e.From && m.To == CardLocation.Processing && m.Reason.Value == LostHpGiftMoveReason) != 1)) return false;
        bool Delivered(LostHpOwnedCardGivenEvent e) => _cardMovements.Any(m => m.Sequence > e.SequenceBefore && m.Sequence <= e.SequenceAfter &&
            m.CardId == e.CardId && m.From == CardLocation.Processing && m.To == CardLocation.Hand(e.RecipientSeat) && m.Reason.Value == LostHpGiftMoveReason);
        if (given.Where((e, index) => e.PaidCount != index + 1 ||
            e.DeliveredCount != (index == 0 ? 0 : given[index - 1].DeliveredCount) + (Delivered(e) ? 1 : 0) ||
            index > 0 && e.SequenceBefore < given[index - 1].SequenceAfter).Any() ||
            gift.DeliveredCount != (given.LastOrDefault()?.DeliveredCount ?? 0)) return false;
        if (gift.LastPayment is not { } payment) return gift.PaidCardIds.Count == 0;
        var last = given.LastOrDefault();
        return last is not null && last.CardId == payment.CardId && last.From == payment.From && last.RecipientSeat == payment.RecipientSeat &&
            last.SequenceBefore == payment.SequenceBefore && last.SequenceAfter == payment.SequenceAfter &&
            gift.PaidCardIds.Last() == payment.CardId && payment.SequenceBefore >= gift.DrawSequenceAfter && payment.SequenceAfter > payment.SequenceBefore &&
            _cardMovements.Any(m => m.Sequence > payment.SequenceBefore && m.Sequence <= payment.SequenceAfter && m.CardId == payment.CardId &&
                m.From == payment.From && m.To == CardLocation.Processing && m.Reason.Value == LostHpGiftMoveReason) &&
            payment.Delivered == _cardMovements.Any(m => m.Sequence > payment.SequenceBefore && m.Sequence <= payment.SequenceAfter && m.CardId == payment.CardId &&
                m.From == CardLocation.Processing && m.To == CardLocation.Hand(payment.RecipientSeat) && m.Reason.Value == LostHpGiftMoveReason);
    }
    private ProgramSkillFrame? LostHpOwnedGiftObserverRoot()
    {
        for (var i = 1; i < _resolutionStack.Count; i++)
        {
            if (_resolutionStack[i - 1] is not TurnEndingBoundaryFrame || _resolutionStack[i] is not ProgramSkillFrame root || !IsValidLostHpOwnedGift(root)) continue;
            if (i == _resolutionStack.Count - 1) return root;
            if (root.PendingMovementContinuation is not { SubjectSeat: var subject } || subject != root.OwnerSeat ||
                !PaidTargetObserverEdge(i + 1)) continue;
            var gift = root.LostHpOwnedGift!;
            var before = gift.Stage == LostHpOwnedGiftStage.Drawing ? gift.DrawSequenceBefore : gift.LastPayment!.SequenceBefore;
            var after = gift.Stage == LostHpOwnedGiftStage.Drawing ? gift.DrawSequenceAfter : gift.LastPayment!.SequenceAfter;
            if (_resolutionStack[i + 1] is CardsMovedTriggerWindowFrame movement)
            {
                if (movement.Batch.ParentFrameId != root.Id || movement.Batch.OriginSkillId != root.SkillId ||
                    movement.Batch.OriginSkillInstanceId != root.SkillInstanceId || movement.Batch.OriginOwnerSeat != root.OwnerSeat ||
                    movement.Batch.Movements.Count == 0 || movement.Batch.Movements.Any(m => m.Sequence <= before || m.Sequence > after || !_cardMovements.Contains(m))) continue;
            }
            else
            {
                if (gift.Stage != LostHpOwnedGiftStage.Moving || gift.LastPayment is not { From.Zone: CardZoneKind.Equipment } lion ||
                    lion.From != CardLocation.Equipment(root.OwnerSeat) || !_cardMovements.Any(m => m.Sequence > lion.SequenceBefore &&
                        m.Sequence <= lion.SequenceAfter && m.CardId == lion.CardId && m.CardKind == CardKind.SilverLion &&
                        m.From == lion.From && m.To == CardLocation.Processing && m.Reason.Value == LostHpGiftMoveReason)) continue;
                if (_resolutionStack[i + 1] is HpChangedTriggerWindowFrame hp)
                {
                    if (hp.Change.Kind != HpChangeKind.Recovery || hp.Change.ParentFrameId != root.Id || hp.ResumeFrameId != root.Id ||
                        hp.Continuation != PostEventContinuation.AwaitedProgramMovement || hp.Change.Amount != 1 ||
                        hp.Change.TargetSeat != root.OwnerSeat || hp.Change.SourceSeat != root.OwnerSeat) continue;
                }
                else if (_resolutionStack[i + 1] is RecoveryReplacementFrame recovery)
                {
                    if (!RecoveryReplacementFrameRidesOn(recovery, root) || recovery.Return.Continuation != PostEventContinuation.AwaitedProgramMovement ||
                        recovery.Attempt.SourceSeat != root.OwnerSeat || recovery.Attempt.TargetSeat != root.OwnerSeat || recovery.Attempt.Amount != 1 ||
                        recovery.Attempt.Completion.Producer != RecoveryAttemptProducer.SilverLion ||
                        recovery.Attempt.Completion.MoveReason?.Value != LostHpGiftMoveReason) continue;
                }
                else continue;
            }
            var valid = true;
            for (var child = i + 1; child < _resolutionStack.Count; child++)
            {
                if (!PaidTargetObserverEdge(child)) { valid = false; break; }
                if (_resolutionStack[child] is DyingFrame d && (IsPaidHandRepaymentRescueRide(child, d) || IsPaidHandRepaymentProgramAlcoholRide(child, d) || PolicyCounterspellVirtualAlcoholRide(child, d))) break;
            }
            if (valid) return root;
        }
        return null;
    }
    private bool IsLostHpOwnedGiftProgramDying() => ActiveDying is { ResumesProgramSkill: true } && LostHpOwnedGiftObserverRoot() is not null;
    private void AssertLostHpOwnedGifts()
    {
        foreach (var root in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.LostHpOwnedGift is not null))
            if (!IsValidLostHpOwnedGift(root) || LostHpOwnedGiftObserverRoot()?.Id != root.Id)
                throw new InvalidOperationException("A lost-HP owned gift lost its exact actual Ending/draw/payment subtree.");
    }
}
