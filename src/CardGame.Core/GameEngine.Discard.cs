namespace CardGame.Core;

public sealed record ManualDiscardFrame(long Id, int OwnerSeat, int ActualTurnNumber,
    int InitialRequiredCount, int RemainingCount)
    : ResolutionFrame(Id, ResolutionFrameKind.ManualDiscard, ResolutionFrameStep.ResolvingEffect);

public sealed partial class GameEngine
{
    private void RequestHumanDiscard(CharacterState player)
    {
        var frame = _resolutionStack.LastOrDefault() as ManualDiscardFrame;
        if (frame is null)
        {
            var required = RequiredSelfHandLimitDiscard(player);
            frame = new ManualDiscardFrame(++_resolutionSequence, player.Seat, _turnNumber, required, required);
            PushRuntimeFrame(frame);
        }
        var hand = GetSelfDiscardableHand(player);
        var handLimit = GetHandLimit(player);
        var count = Math.Min(frame.RemainingCount, hand.Count);
        _pendingDecision = new PendingDecision(
            DecisionKind.DiscardCards,
            player.Seat,
            $"手牌上限为 {handLimit}，请选择 {count} 张手牌弃置。",
            hand.Select(card => card.Id).ToArray(),
            [])
        {
            PromptId = CreatePromptId(),
            RequiredCardCount = count
        };
        _status = EngineStatus.AwaitingHumanDiscard;
        AdvanceRulesAndPublishState();
    }

    [NativeFrameAutomaticStep(NativeFrameAutomaticPhase.AfterOpening, 171,
        NativeFrameAutomaticPending.RequireNoneBeforeFrame, NativeFrameAutomaticPublish.AfterAdvance)]
    [NativeFrameRoute(typeof(ManualDiscardFrame), 351)]
    private void ContinueManualDiscard(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ManualDiscardFrame frame || frame.Id != frameId ||
            frame.OwnerSeat != _currentSeat || frame.ActualTurnNumber != _turnNumber || _phase != TurnPhase.Discard)
            throw new InvalidOperationException("Manual discard lost its owning turn.");
        if (_pendingDecision is not null) return;
        if (TryBeginCardsMovedProgramWindow(frame.Id)) return;
        var owner = _players[frame.OwnerSeat];
        if (_winner == Winner.None && owner.IsAlive && frame.RemainingCount > 0 && GetSelfDiscardableHand(owner).Count > 0)
        {
            RequestHumanDiscard(owner);
            return;
        }
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ManualDiscard);
        if (_winner != Winner.None) return;
        if (TryBeginDiscardPhaseEndedProgramWindow(owner)) return;
        EndTurn();
    }

    private bool HasManualDiscardBoundaryFrame()
    {
        if (_resolutionStack.FirstOrDefault() is not ManualDiscardFrame frame) return false;
        if (frame.OwnerSeat != _currentSeat || frame.ActualTurnNumber != _turnNumber || _phase != TurnPhase.Discard ||
            frame.InitialRequiredCount <= 0 || frame.RemainingCount < 0 || frame.RemainingCount > frame.InitialRequiredCount)
            throw new InvalidOperationException("Manual discard lost its frozen remaining quota.");
        return true;
    }

    private CommandResult SubmitDiscardCards(DiscardCardsCommand command)
    {
        var validation = ValidateHumanPrompt(command.ActorSeat, DecisionKind.DiscardCards,
            command.PromptId, CommandErrorCode.IllegalAction);
        if (validation is not null) return Reject(validation.Code, validation.Message);

        var prompt = _pendingDecision!;
        var actor = _players[command.ActorSeat];
        if (_phase != TurnPhase.Discard || _currentSeat != actor.Seat || !actor.IsAlive)
            return Reject(CommandErrorCode.IllegalAction, "The actor is not in their discard phase.");

        var ids = command.CardIds.ToArray();
        if (ids.Length == 0 || ids.Length > prompt.RequiredCardCount || ids.Distinct().Count() != ids.Length)
            return Reject(CommandErrorCode.InvalidChoice, $"Select between 1 and {prompt.RequiredCardCount} distinct cards.");

        var hand = GetSelfDiscardableHand(actor);
        var handIds = hand.Select(card => card.Id).ToHashSet();
        if (ids.Any(id => !prompt.ValidCardIds.Contains(id) || !handIds.Contains(id)))
            return Reject(CommandErrorCode.InvalidCard, "Every selected card must be in the published hand.");

        if (_resolutionStack.LastOrDefault() is not ManualDiscardFrame frame || frame.OwnerSeat != actor.Seat ||
            frame.ActualTurnNumber != _turnNumber || Math.Min(frame.RemainingCount, hand.Count) != prompt.RequiredCardCount)
            return Reject(CommandErrorCode.IllegalAction, "The remaining discard obligation changed; refresh the prompt.");

        var selected = ids.ToHashSet();
        var cards = hand.Where(card => selected.Contains(card.Id)).OrderBy(card => card.Id).ToArray();
        return Accept(() =>
        {
            ReplaceRuntimeTop(frame with { RemainingCount = frame.RemainingCount - cards.Length });
            var discardInvocation = CaptureNativeLogicalDiscardInvocation(actor.Seat, actor.Seat,
                cards.Select(card => card.Id).ToArray(), CardMoveReasons.HandLimitDiscard);
            MoveCards(cards, CardLocation.Hand(actor.Seat), CardLocation.DiscardPile,
                CardMoveReasons.HandLimitDiscard);
            CompleteNativeLogicalDiscardInvocation(discardInvocation);
            ClearPendingDecision();
            AddLog("CardsDiscarded", $"{actor.Name} 弃置 {cards.Length} 张手牌。", actor.Seat);
            AdvanceEventRulesAndQueueFact(new HandLimitDiscardedEvent(actor.Seat,
                Array.AsReadOnly(cards.Select(card => card.Id).ToArray())));
            ContinueManualDiscard(frame.Id);
        });
    }

    private void AssertDiscardPromptInvariant()
    {
        if (_pendingDecision is { Kind: DecisionKind.DiscardCards } prompt)
        {
            var owner = _players[prompt.PlayerSeat];
            var hand = GetSelfDiscardableHand(owner);
            if (_resolutionStack.LastOrDefault() is not ManualDiscardFrame frame || frame.OwnerSeat != owner.Seat ||
                frame.ActualTurnNumber != _turnNumber || !HasManualDiscardBoundaryFrame() ||
                !owner.IsHuman || !owner.IsAlive || _phase != TurnPhase.Discard ||
                _currentSeat != owner.Seat || _status != EngineStatus.AwaitingHumanDiscard ||
                prompt.RequiredCardCount <= 0 || prompt.RequiredCardCount != Math.Min(frame.RemainingCount, hand.Count) ||
                !prompt.ValidCardIds.Order().SequenceEqual(hand.Select(card => card.Id).Order()) ||
                prompt.Choices.Count != 0 || prompt.ValidTargetSeats.Count != 0 || !prompt.IsPrivate)
                throw new InvalidOperationException("The private discard prompt must match its current owner's hand limit.");
        }
        else if (_status == EngineStatus.AwaitingHumanDiscard)
        {
            throw new InvalidOperationException("Discard status requires a private discard prompt.");
        }
    }
}
