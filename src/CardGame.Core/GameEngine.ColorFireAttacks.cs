namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ColorFireAttackReceipt? FreezeColorFireAttackPolicy(int sourceSeat, CardActionContext? action)
    {
        if (action?.EffectiveKind != CardKind.FireAttack) return null;
        var binding = CardPolicies(_players[sourceSeat], SkillProgramCardPolicyKind.RandomRevealColorFireAttack,
            CardKind.FireAttack).FirstOrDefault();
        return binding.Source is null ? null : new(new(binding.Source.SkillId, binding.Policy.Id,
            sourceSeat, binding.Source.SkillInstanceId), action.ActionId);
    }

    private bool TryBeginColorFireAttackReveal(CardUseFrame pending)
    {
        if (pending.ColorFireAttack is not { } issued) return false;
        if (_resolutionStack.LastOrDefault()?.Id != pending.Id ||
            pending.Action?.ActionId != issued.ActionId || pending.FireAttackSelection?.RevealedCardId is not null ||
            issued.PaidCardId is not null)
            throw new InvalidOperationException("Random color Fire Attack lost its issued use.");
        var target = _players[GetFireAttackTargetSeat(pending)];
        if (_winner != Winner.None || !target.IsAlive || GetHand(target).Count == 0)
        { CompleteColorFireAttackWithoutDamage(pending); return true; }
        var hand = GetHand(target);
        var revealed = hand[_random.Next(hand.Count)];
        var receipt = issued with { TargetSeat = target.Seat, RevealedCardId = revealed.Id,
            RevealedSuit = revealed.Suit, RevealedIsRed = SuitColor(EffectiveSuit(target, revealed)) };
        ReplaceRuntimeTop(pending with { ColorFireAttack = receipt });
        ResolveFireAttackReveal((CardUseFrame)_resolutionStack.Last(), revealed.Id);
        return true;
    }

    private IReadOnlyList<Card> ColorFireAttackCosts(CardUseFrame pending) =>
        pending.ColorFireAttack is { RevealedIsRed: { } isRed } && _players[pending.SourceSeat].IsAlive
            ? GetHand(_players[pending.SourceSeat]).Concat(GetEquipment(_players[pending.SourceSeat]))
                .Where(card => SuitColor(EffectiveSuit(_players[pending.SourceSeat], card)) == isRed)
                .OrderBy(card => card.Id).ToArray() : [];

    private bool TryBeginColorFireAttackDiscard(CardUseFrame pending, Card revealed)
    {
        if (pending.ColorFireAttack is not { } receipt) return false;
        if (receipt.RevealedCardId != revealed.Id || receipt.TargetSeat != GetFireAttackTargetSeat(pending) ||
            pending.FireAttackSelection?.RevealedCardId != revealed.Id || receipt.PaidCardId is not null)
            throw new InvalidOperationException("Color Fire Attack lost its frozen reveal.");
        var costs = ColorFireAttackCosts(pending);
        if (_winner != Winner.None || costs.Count == 0 || !_players[receipt.TargetSeat!.Value].IsAlive)
        { CompleteColorFireAttackWithoutDamage(pending); return true; }
        var source = _players[pending.SourceSeat];
        _pendingDecision = new(DecisionKind.FireAttackDiscard, source.Seat,
            "火攻随机展示已完成，弃置一张同颜色的手牌或装备牌，或放弃伤害。",
            costs.Select(card => card.Id).ToArray(), [], source.Seat, CardKind.FireAttack)
        {
            PromptId = CreatePromptId(), TargetSeat = receipt.TargetSeat,
            Choices = costs.Select(card => new PromptChoice(new($"fire-attack.discard.card-{card.Id}"),
                $"弃置【{card.DisplayName}】，造成火焰伤害。", [card.Id], [],
                new Dictionary<string, string> { ["response"] = "fire-attack-discard", ["action"] = "discard-same-color" }))
                .Append(new PromptChoice(new("fire-attack.skip"), "放弃伤害", [], [],
                    new Dictionary<string, string> { ["response"] = "fire-attack-skip", ["action"] = "skip-fire-attack" })).ToArray()
        };
        _status = source.IsHuman ? EngineStatus.AwaitingHumanCardSelection : EngineStatus.Running;
        return true;
    }

    private bool TryResolveColorFireAttackDiscard(CardUseFrame pending, int? selectedCardId)
    {
        if (pending.ColorFireAttack is not { } receipt) return false;
        if (_resolutionStack.LastOrDefault()?.Id != pending.Id || receipt.PaidCardId is not null ||
            receipt.RevealedCardId != pending.FireAttackSelection?.RevealedCardId)
            throw new InvalidOperationException("Color Fire Attack payment is no longer current.");
        if (selectedCardId is null) { CompleteColorFireAttackWithoutDamage(pending); return true; }
        var cost = ColorFireAttackCosts(pending).SingleOrDefault(card => card.Id == selectedCardId) ??
            throw new InvalidOperationException("Color Fire Attack requires one current same-color HE card.");
        var source = _players[pending.SourceSeat];
        var from = FindOwnedCardLocation(source, cost);
        var color = SuitColor(EffectiveSuit(source, cost))!.Value;
        receipt = receipt with { PaidCardId = cost.Id, PaidFrom = from, PaidIsRed = color,
            PaymentStartSequence = _cardMovements.LastOrDefault()?.Sequence ?? 0 };
        ReplaceRuntimeTop(pending with { ColorFireAttack = receipt });
        MoveCard(cost, from, CardLocation.DiscardPile, CardMoveReasons.FireAttackDiscard);
        pending = (CardUseFrame)_resolutionStack.Single(frame => frame.Id == pending.Id);
        ReplaceRuntimeTop(pending with { ColorFireAttack = receipt with { PaymentEndSequence = _cardMovements.LastOrDefault()?.Sequence ?? 0 } });
        AdvanceEventRulesAndQueueFact(new ColorFireAttackPaidEvent(pending.Id, source.Seat,
            receipt.TargetSeat!.Value, color, from.Zone == CardZoneKind.Equipment));
        ContinueColorFireAttackPayment(pending.Id);
        return true;
    }

    private void ContinueColorFireAttackPayment(long useId)
    {
        if (_resolutionStack.LastOrDefault() is not CardUseFrame pending || pending.Id != useId ||
            pending.ColorFireAttack is not { PaidCardId: { } costId } receipt)
            throw new InvalidOperationException("Color Fire Attack lost its exact paid owning frame.");
        if (TryBeginHpChangedProgramWindow(useId, PostEventContinuation.ColorFireAttackPayment) ||
            TryBeginCardsMovedProgramWindow(useId)) return;
        var targetSeat = receipt.TargetSeat ?? throw new InvalidOperationException("Paid color Fire Attack lost its frozen target.");
        var canDamage = _winner == Winner.None && _players[pending.SourceSeat].IsAlive &&
            _players[targetSeat].IsAlive;
        AdvanceEventRulesAndQueueFact(new FireAttackResolvedEvent(useId, pending.SourceSeat,
            targetSeat, receipt.RevealedCardId!.Value, receipt.RevealedSuit!.Value, costId, canDamage));
        if (!canDamage) { CompleteColorFireAttackWithoutDamage(pending, publishFailure: false); return; }
        var effect = GetFireAttackEffectCard(pending);
        ReplaceRuntimeTop(pending with { FireAttackSelection = null,
            ColorFireAttack = new(receipt.Source, receipt.ActionId) });
        var attack = new CardAttackHandle(this, useId, pending.SourceSeat, targetSeat,
            effect, damageAmount: 1, playedCardKind: CardKind.FireAttack, physicalCards: GetCardUsePhysicalCards(useId));
        ActiveCardAttack = attack;
        if (!ApplyAttackDamage(attack)) CompleteAttack(attack);
    }

    private void CompleteColorFireAttackWithoutDamage(CardUseFrame pending, bool publishFailure = true)
    {
        var receipt = pending.ColorFireAttack!;
        if (publishFailure && receipt.RevealedCardId is { } revealed)
            AdvanceEventRulesAndQueueFact(new FireAttackResolvedEvent(pending.Id, pending.SourceSeat,
                receipt.TargetSeat!.Value, revealed, receipt.RevealedSuit!.Value, null, false));
        var effect = GetFireAttackEffectCard(pending);
        ReplaceRuntimeTop(pending with { FireAttackSelection = null,
            ColorFireAttack = new(receipt.Source, receipt.ActionId) });
        MoveFinishedTrickCard(pending.Id, effect);
        FinishCardUse(pending.Id, effect, pending.CardKind);
    }

    private bool TryResolveAiColorFireAttack(CardUseFrame pending, PendingDecision decision)
    {
        if (pending.ColorFireAttack is not { RevealedIsRed: { } red } || decision.Kind != DecisionKind.FireAttackDiscard)
            return false;
        var actor = _players[decision.PlayerSeat];
        var (id, thought) = _aiBrains[actor.Seat].ChooseColorFireAttackDiscard(CreateSnapshot(actor.Seat),
            GetFireAttackTargetSeat(pending), red, decision.ValidCardIds, ++_thoughtSequence);
        AddThought(thought); ClearPendingDecision(); ResolveFireAttackDiscard(pending, id);
        AdvanceRulesAndPublishState(); return true;
    }

    private void AssertColorFireAttackReceipts()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(frame => frame.ColorFireAttack is not null))
        {
            var receipt = use.ColorFireAttack!;
            if (use.CardKind != CardKind.FireAttack || use.Action?.ActionId != receipt.ActionId ||
                receipt.Source.OwnerSeat != use.SourceSeat || string.IsNullOrWhiteSpace(receipt.Source.SkillId) ||
                string.IsNullOrWhiteSpace(receipt.Source.BindingId) || string.IsNullOrWhiteSpace(receipt.Source.SkillInstanceId))
                throw new InvalidOperationException("Color Fire Attack has an invalid issued source identity.");
            if (receipt.PaidCardId is not { } cost) continue;
            if (receipt.PaidFrom is not { } from || from.OwnerSeat != use.SourceSeat ||
                from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                receipt.RevealedCardId != use.FireAttackSelection?.RevealedCardId ||
                receipt.TargetSeat != GetFireAttackTargetSeat(use) || receipt.PaidIsRed != receipt.RevealedIsRed ||
                receipt.RevealedIsRed is null || receipt.PaymentEndSequence <= receipt.PaymentStartSequence ||
                !_cardMovements.Where(move => move.Sequence > receipt.PaymentStartSequence && move.Sequence <= receipt.PaymentEndSequence)
                    .Any(move => move.CardId == cost && move.From == from &&
                        move.To.Zone is CardZoneKind.DiscardPile or CardZoneKind.OutsideGame && move.Reason == CardMoveReasons.FireAttackDiscard))
                throw new InvalidOperationException("Color Fire Attack payment lost its exact original HE movement.");
        }
        foreach (var movement in _resolutionStack.OfType<CardsMovedTriggerWindowFrame>()
                     .Where(frame => frame.ResumeColorFireAttackFrameId is not null))
            if (_resolutionStack.OfType<CardUseFrame>().SingleOrDefault(frame => frame.Id == movement.ResumeColorFireAttackFrameId)
                    is not { ColorFireAttack.PaidCardId: not null } owner || movement.Batch.ParentFrameId != owner.Id ||
                movement.Batch.AwaitingProgramFrameId is not null || movement.ResumeProgramFrameId is not null)
                throw new InvalidOperationException("Color Fire Attack movement lost its exact paid CardUse return.");
    }
}
