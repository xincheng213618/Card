namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void ResolveUnexpectedAssault(
        CharacterState source,
        CharacterState target,
        Card card,
        CardKind? playedCardKind = null)
    {
        if (!BuildLegalActions(source).Any(action => action.Kind == LegalActionKind.UnexpectedAssault &&
                action.CardId == card.Id && action.TargetSeat == target.Seat &&
                action.PlayedCardKind == playedCardKind))
            throw new InvalidOperationException("Unexpected Assault became illegal before its real use.");

        var id = BeginCardUse(card, source.Seat, [target.Seat], playedCardKind);
        MoveCard(card, FindOwnedCardLocation(source, card), CardLocation.Processing, CardMoveReasons.Use);
        BeginJizhiOrNullificationWindow(id, card, source.Seat, [target.Seat],
            LegalActionKind.UnexpectedAssault, playedCardKind: playedCardKind);
    }

    private void ResolveUnexpectedAssaultEffect(NullificationWindowFrame pending)
    {
        var targetSeat = GetNullificationTargetSeat(pending) ??
            throw new InvalidOperationException("Unexpected Assault lost its current target.");
        if (_resolutionStack.LastOrDefault() is not CardUseFrame use ||
            use.Id != pending.ParentFrameId || use.CardId != pending.EffectCardId ||
            use.CardKind != CardKind.UnexpectedAssault || pending.EffectCardKind != use.CardKind ||
            pending.ActionKind != LegalActionKind.UnexpectedAssault || use.SourceSeat != pending.SourceSeat ||
            use.TargetIndex >= use.TargetSeats.Count || use.TargetSeats[use.TargetIndex] != targetSeat ||
            use.Action is not { Type: CardActionType.Use, EffectiveKind: CardKind.UnexpectedAssault, EffectiveSuit: not null })
            throw new InvalidOperationException("Unexpected Assault lost its exact owning native use and suit.");

        var target = _players[targetSeat];
        if (!target.IsAlive || GetHand(target).Count == 0)
        {
            SkipUnavailableTargetCardEffect(pending, target, CardEffectSkipReason.TargetHandEmpty);
            return;
        }
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(use.CardId, use.CardKind, use.SourceSeat, targetSeat));
        AddLog("CardUsed", $"{_players[use.SourceSeat].Name} 对 {target.Name} 使用【出其不意】，选择展示其一张手牌。",
            use.SourceSeat, targetSeat);
        BeginTargetCardSelection(pending, target, GetHand(target).Count);
    }

    // Called after the ordinary opaque-slot child has completed and restored
    // this exact use to ResolvingEffect. It deliberately performs no card move.
    private bool TryApplyUnexpectedAssaultTargetCardEffect(
        long resolutionId,
        Card effectCard,
        CardKind effectiveCardKind,
        int sourceSeat,
        int targetSeat,
        LegalActionKind actionKind,
        Card targetCard,
        CardLocation targetCardFromZone)
    {
        if (effectiveCardKind != CardKind.UnexpectedAssault && actionKind != LegalActionKind.UnexpectedAssault)
            return false;
        if (effectiveCardKind != CardKind.UnexpectedAssault || actionKind != LegalActionKind.UnexpectedAssault ||
            _resolutionStack.LastOrDefault() is not CardUseFrame use || use.Id != resolutionId ||
            use.CardKind != effectiveCardKind || use.CardId != effectCard.Id || use.SourceSeat != sourceSeat ||
            use.Step != ResolutionFrameStep.ResolvingEffect || use.TargetIndex >= use.TargetSeats.Count ||
            use.TargetSeats[use.TargetIndex] != targetSeat || targetCardFromZone != CardLocation.Hand(targetSeat) ||
            !_players[targetSeat].IsAlive || !GetHand(_players[targetSeat]).Any(card => card.Id == targetCard.Id) ||
            use.UnexpectedAssaultReveal?.TargetIndex == use.TargetIndex ||
            use.Action is not { Type: CardActionType.Use, EffectiveKind: CardKind.UnexpectedAssault,
                EffectiveSuit: { } useSuit } action || action.ActorSeat != sourceSeat)
            throw new InvalidOperationException("Unexpected Assault reveal lost its exact current hand, action or target cursor.");

        var receipt = new UnexpectedAssaultRevealReceipt(use.Id, action.ActionId, use.TargetIndex,
            sourceSeat, targetSeat, targetCard.Id, targetCard.Kind, targetCard.Suit, targetCard.Rank,
            useSuit, EffectiveSuit(_players[targetSeat], targetCard));
        ReplaceRuntimeTop(use with { UnexpectedAssaultReveal = receipt });
        AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(use.Id, Array.AsReadOnly(new[] { ToSnapshot(targetCard) })));
        AdvanceEventRulesAndQueueFact(new UnexpectedAssaultRevealedEvent(receipt));
        AddLog("CardRevealed", $"{_players[sourceSeat].Name} 展示了 { _players[targetSeat].Name } 的【{targetCard.DisplayName}】（{GetSuitDisplayName(receipt.EffectiveRevealedSuit)}）。",
            sourceSeat, targetSeat);

        if (!receipt.CanCauseDamage)
        {
            MoveFinishedTrickCard(use.Id, effectCard);
            FinishCardUse(use.Id, effectCard, effectiveCardKind);
            return true;
        }

        var attack = new CardAttackHandle(this, use.Id, sourceSeat, targetSeat, effectCard,
            playedCardKind: effectiveCardKind, damageNatureOverride: DamageNature.Normal,
            physicalCards: GetCardUsePhysicalCards(use.Id));
        ActiveCardAttack = attack;
        if (!ApplyAttackDamage(attack)) CompleteAttack(attack);
        return true;
    }

    private void AssertUnexpectedAssaultReceipts()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(frame => frame.UnexpectedAssaultReveal is not null))
        {
            var receipt = use.UnexpectedAssaultReveal!;
            if (use.CardKind != CardKind.UnexpectedAssault || receipt.CardUseFrameId != use.Id ||
                use.Action is not { Type: CardActionType.Use, EffectiveKind: CardKind.UnexpectedAssault } action ||
                action.ActionId != receipt.ActionId || action.ActorSeat != use.SourceSeat ||
                action.EffectiveSuit != receipt.EffectiveUseSuit || receipt.SourceSeat != use.SourceSeat ||
                receipt.TargetIndex < 0 || receipt.TargetIndex >= use.TargetSeats.Count || receipt.TargetIndex > use.TargetIndex ||
                use.TargetSeats[receipt.TargetIndex] != receipt.TargetSeat || receipt.RevealedCardId <= 0 ||
                !IsValidPlayerSeat(receipt.SourceSeat) || !IsValidPlayerSeat(receipt.TargetSeat) ||
                !Enum.IsDefined(receipt.EffectiveUseSuit) || !Enum.IsDefined(receipt.EffectiveRevealedSuit) ||
                CompleteProgramEventHistory().OfType<UnexpectedAssaultRevealedEvent>().Count(e => e.Receipt == receipt) != 1 ||
                !CompleteProgramEventHistory().OfType<CardsRevealedEvent>().Any(e => e.ResolutionId == use.Id &&
                    e.Cards is [var card] && card.Id == receipt.RevealedCardId && card.Kind == receipt.RevealedCardKind &&
                    card.Suit == receipt.RevealedPrintedSuit && card.Rank == receipt.RevealedRank))
                throw new InvalidOperationException("Unexpected Assault lost its one actual public reveal and frozen scalar comparison.");
        }
    }
}
