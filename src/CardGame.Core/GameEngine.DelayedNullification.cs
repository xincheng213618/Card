namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void BeginDelayedNullificationWindow(CharacterState owner, Card card, CardKind kind)
    {
        if (ActiveNullificationWindow is not null)
            throw new InvalidOperationException("A delayed effect cannot open a second counterspell window.");
        var parent = _resolutionStack.LastOrDefault();
        var delayed = new DelayedEffectContinuation(card.Id, kind, owner.Seat, _turnNumber, _phase,
            parent?.Id ?? 0, parent?.Kind, parent?.Step);
        var action = kind switch
        {
            CardKind.Indulgence => LegalActionKind.Indulgence,
            CardKind.SupplyShortage => LegalActionKind.SupplyShortage,
            CardKind.Lightning => LegalActionKind.Lightning,
            _ => throw new InvalidOperationException("Only delayed tricks can enter delayed counterspell resolution.")
        };
        var window = new NullificationWindowFrame(++_resolutionSequence, delayed.ParentFrameId,
            owner.Seat, Array.AsReadOnly(new[] { owner.Seat }), card.Id, kind, action, null, null,
            BuildNullificationCandidateSeats(owner.Seat)) { DelayedReturn = delayed };
        PushRuntimeFrame(window);
        ContinueNullificationWindow(window);
    }

    private bool HasNullificationEffectOwner(NullificationWindowFrame window)
    {
        if (window.DelayedReturn is not { } delayed)
            return LifecycleCardUse(window.ParentFrameId) is not null;
        var index = _resolutionStack.FindIndex(frame => frame.Id == window.Id);
        var parent = index > 0 ? _resolutionStack[index - 1] : null;
        return index >= 0 && (parent?.Id ?? 0) == delayed.ParentFrameId &&
            parent?.Kind == delayed.ParentKind && parent?.Step == delayed.ParentStep &&
            window.ParentFrameId == delayed.ParentFrameId && window.EffectCardId == delayed.CardId &&
            window.EffectCardKind == delayed.EffectiveKind && IsDelayedCard(delayed.EffectiveKind) &&
            window.SourceSeat == delayed.OwnerSeat && window.TargetSeats.SequenceEqual([delayed.OwnerSeat]) &&
            _turnNumber == delayed.TurnNumber && _phase == delayed.Phase && _currentSeat == delayed.OwnerSeat &&
            _cardZones.GetLocation(delayed.CardId) == CardLocation.Judgment(delayed.OwnerSeat) &&
            GetJudgmentEffectiveCardKind(GetNullificationEffectCard(window)) == delayed.EffectiveKind;
    }

    private long? NullificationParentActionId(NullificationWindowFrame window) =>
        window.DelayedReturn is null ? LifecycleCardUse(window.ParentFrameId)?.Action?.ActionId : null;

    private void CompleteDelayedNullification(NullificationWindowFrame window, DelayedEffectContinuation delayed)
    {
        var parent = _resolutionStack.LastOrDefault();
        if ((parent?.Id ?? 0) != delayed.ParentFrameId || parent?.Kind != delayed.ParentKind ||
            parent?.Step != delayed.ParentStep || _turnNumber != delayed.TurnNumber ||
            _phase != delayed.Phase || _currentSeat != delayed.OwnerSeat)
            throw new InvalidOperationException("Delayed counterspell lost its exact turn and parent return.");
        var owner = _players[delayed.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive) return;
        var card = GetJudgment(owner).SingleOrDefault(card => card.Id == delayed.CardId);
        if (card is null || GetJudgmentEffectiveCardKind(card) != delayed.EffectiveKind)
            throw new InvalidOperationException("Delayed counterspell lost its held card identity.");
        if (window.EffectNullified)
        {
            AddLog("CardEffect", $"【{CardCatalog.Get(delayed.EffectiveKind).DisplayName}】的效果被无懈。", owner.Seat);
            if (delayed.EffectiveKind == CardKind.Lightning)
                CompleteLightningWithoutHit(window.Id, card, owner, null);
            else
            {
                MoveCard(card, CardLocation.Judgment(owner.Seat), CardLocation.DiscardPile, CardMoveReasons.DelayedCardFinish);
                BeginDelayedJudgmentOrTurnStart(owner);
            }
            return;
        }
        if (TryBeginDelayedCardEffectPrograms(owner, card, delayed.EffectiveKind)) return;
        BeginActualDelayedJudgment(owner, card, delayed.EffectiveKind);
    }
}
