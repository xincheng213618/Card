namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool UsesSequentialTrickTargets(LegalActionKind kind) => kind is
        LegalActionKind.DrawTwo or LegalActionKind.Dismantlement or LegalActionKind.Snatch or
        LegalActionKind.FireAttack or LegalActionKind.Duel;

    private IReadOnlyList<int> FreezeSequentialTrickTarget(long frameId, IReadOnlyList<int> targets,
        LegalActionKind kind, int? firstTargetCardId, CardKind? requiredCardKind)
    {
        var index = _resolutionStack.FindIndex(frame => frame.Id == frameId);
        if (index < 0 || _resolutionStack[index] is not CardUseFrame use)
            throw new InvalidOperationException("A trick target window lost its card use.");
        if (use.SequentialTrick is null && targets.Count > 1 && UsesSequentialTrickTargets(kind))
        {
            use = use with { SequentialTrick = new(kind, firstTargetCardId, requiredCardKind) };
            _resolutionStack[index] = use;
        }
        return use.SequentialTrick is null ? targets : [use.TargetSeats[use.TargetIndex]];
    }

    private bool HasRemainingSequentialTrickTargets(long frameId) => _winner == Winner.None &&
        _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(frame => frame.Id == frameId) is
            { SequentialTrick: not null } use &&
        use.TargetSeats.Skip(use.TargetIndex + 1).Any(seat => _players[seat].IsAlive);

    private void MoveFinishedTrickCard(long frameId, Card card)
    {
        if (HasRemainingSequentialTrickTargets(frameId) || HasRemainingAdjustedBorrowedSwordTargets(frameId)) return;
        MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.UseFinished);
    }

    private bool TryContinueSequentialTrick(long frameId, bool afterAttack = false)
    {
        var index = _resolutionStack.FindIndex(frame => frame.Id == frameId);
        if (index < 0 || _resolutionStack[index] is not CardUseFrame { SequentialTrick: { } continuation } use)
            return false;
        var next = use.TargetIndex + 1;
        while (next < use.TargetSeats.Count && !_players[use.TargetSeats[next]].IsAlive) next++;
        if (_winner != Winner.None || next == use.TargetSeats.Count)
        {
            _resolutionStack[index] = use with { TargetIndex = use.TargetSeats.Count, SequentialTrick = null };
            return false;
        }
        if (!ReferenceEquals(use, _resolutionStack.LastOrDefault()))
            throw new InvalidOperationException("A trick must finish its child before the next target.");
        var card = _cardZones.CardsAt(CardLocation.Processing).Single(card => card.Id == use.CardId);
        _resolutionStack[index] = use with { TargetIndex = next, Step = ResolutionFrameStep.ResolvingEffect };
        if (afterAttack)
        {
            _pendingAttack = null;
            _pendingDuel = null;
            ClearPendingDecision();
        }
        BeginNullificationWindow(frameId, card, use.SourceSeat, [use.TargetSeats[next]],
            continuation.ActionKind, null, continuation.RequiredCardKind, use.CardKind);
        return true;
    }

    private void AssertSequentialTrickTargets()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(frame => frame.SequentialTrick is not null))
        {
            if (!UsesSequentialTrickTargets(use.SequentialTrick!.ActionKind) || use.TargetSeats.Count < 2 ||
                use.TargetSeats.Distinct().Count() != use.TargetSeats.Count ||
                use.TargetSeats.Any(seat => !IsValidPlayerSeat(seat)) ||
                use.TargetIndex < 0 || use.TargetIndex >= use.TargetSeats.Count ||
                _cardZones.GetLocation(use.CardId) != CardLocation.Processing)
                throw new InvalidOperationException("A sequential trick lost its targets, cursor or physical card.");
        }
    }
}
